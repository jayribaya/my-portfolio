using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Portfolio.Domain;
namespace Portfolio.Application;

public sealed partial class PortfolioService(IPortfolioStore store)
{
    public static readonly string[] ContentKinds = ["sections", "skills", "experience", "education", "certifications", "technologies", "categories", "settings", "resume", "articles"];
    public static readonly string[] SectionKeys = ["hero", "about", "projects", "skills", "experience", "contact", "social"];
    public static string Slug(string title)
    {
        var text = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(c);
        var slug = SlugCharacters().Replace(text.ToString().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "project" : slug[..Math.Min(110, slug.Length)];
    }
    [GeneratedRegex("[^a-z0-9]+") ] private static partial Regex SlugCharacters();
    public static bool SafeUrl(string? value, bool local = false) => string.IsNullOrWhiteSpace(value) ||
        (local && value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\')) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo));
    public static void Validate(object value)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), errors, true))
            throw new InputException(string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }
    public static List<string> Canonical(IEnumerable<string> saved, IEnumerable<string> eligible)
    {
        var all = eligible.Distinct().ToList(); var set = all.ToHashSet();
        var result = saved.Where(set.Contains).Distinct().ToList();
        result.AddRange(all.Where(x => !result.Contains(x)));
        return result;
    }
    public async Task<List<Project>> OrderedProjects(bool publicOnly, bool featured, CancellationToken ct)
    {
        var projects = (await store.Projects(ct)).Where(p => (!publicOnly || p.IsPublic) && (!featured || p.IsFeatured)).OrderBy(p => p.CreatedAt).ToList();
        var order = await store.Order(featured ? "featured" : "projects", ct);
        var ids = Canonical(order.Ids, projects.Select(p => p.Id));
        return ids.Select(id => projects.Single(p => p.Id == id)).ToList();
    }
    public async Task<OrderDocument> GetOrder(string scope, CancellationToken ct)
    {
        var order = await store.Order(scope, ct);
        order.Ids = Canonical(order.Ids, await Eligible(scope, ct));
        return order;
    }
    private async Task<List<string>> Eligible(string scope, CancellationToken ct) => scope switch
    {
        "projects" => (await store.Projects(ct)).OrderBy(x => x.CreatedAt).Select(x => x.Id).ToList(),
        "featured" => (await store.Projects(ct)).Where(x => x.IsFeatured && x.DeletedAt == null && x.Visibility != Visibility.Archived).OrderBy(x => x.CreatedAt).Select(x => x.Id).ToList(),
        "sections" => (await store.Content("sections", ct)).OrderBy(x => x.SortOrder).Select(x => x.Id).ToList(),
        _ => throw new InputException("Unknown ordering scope.")
    };
    public async Task<OrderDocument> Reorder(string scope, ReorderRequest request, string actor, CancellationToken ct)
    {
        if (request.Version < 0 || request.Ids == null || request.Ids.Count > 5000 || request.Ids.Count != request.Ids.Distinct().Count())
            throw new InputException("Ordering contains duplicate IDs or exceeds the limit.");
        var eligible = await Eligible(scope, ct);
        if (!eligible.ToHashSet().SetEquals(request.Ids)) throw new InputException("Send all eligible IDs for this scope. Refresh the unfiltered list before reordering.");
        var result = await store.ReplaceOrder(scope, request.Ids, request.Version, ct);
        await Log(actor, "reorder:" + scope, scope, ct);
        return result;
    }
    public async Task SaveProject(Project project, string actor, CancellationToken ct)
    {
        Validate(project);
        if (project.StartDate > project.EndDate) throw new InputException("End date cannot be before start date.");
        if (!Enum.IsDefined(project.Visibility) || !Enum.IsDefined(project.ProjectStatus)) throw new InputException("Invalid status.");
        foreach (var url in new[] { project.GitHubUrl, project.DemoUrl, project.CaseStudyUrl })
            if (!SafeUrl(url)) throw new InputException("External links must use HTTPS without embedded credentials.");
        foreach (var list in new[] { project.Technologies, project.CategoryIds, project.GalleryImageIds, project.Tags, project.Features, project.Responsibilities, project.Outcomes })
            if (list == null || list.Length > 40 || list.Any(x => x.Length > 500)) throw new InputException("Lists support up to 40 entries of 500 characters.");
        var all = await store.Projects(ct);
        var previous = all.FirstOrDefault(x => x.Id == project.Id);
        var existingMedia = (await store.Media(ct)).ToDictionary(x => x.Id);
        var imageIds = project.GalleryImageIds.Concat(string.IsNullOrEmpty(project.CoverImageId) ? [] : new[] { project.CoverImageId });
        if (imageIds.Any(id => !existingMedia.TryGetValue(id, out var media) || !media.ContentType.StartsWith("image/"))) throw new InputException("Select existing image assets.");
        var categories = (await store.Content("categories", ct)).Select(x => x.Id).ToHashSet();
        if (project.CategoryIds.Any(id => !categories.Contains(id))) throw new InputException("Unknown category.");
        var baseSlug = Slug(string.IsNullOrWhiteSpace(project.Slug) ? project.Title : project.Slug);
        project.Slug = baseSlug; var suffix = 2;
        while (all.Any(x => x.Id != project.Id && x.Slug == project.Slug)) project.Slug = $"{baseSlug}-{suffix++}";
        project.CreatedAt = previous?.CreatedAt ?? DateTime.UtcNow;
        project.DeletedAt = previous?.DeletedAt;
        project.PublishedAt = project.Visibility == Visibility.Published ? previous?.PublishedAt ?? DateTime.UtcNow : previous?.PublishedAt;
        project.SortOrder = previous?.SortOrder ?? all.Count;
        await store.SaveProject(project, project.Version, ct);
        await Log(actor, previous == null ? "project:create" : "project:update", project.Id, ct);
    }
    public async Task ChangeVisibility(string id, string action, long version, string actor, CancellationToken ct)
    {
        var project = await store.Project(id, ct) ?? throw new InputException("Project not found.");
        if (project.Version != version) throw new ConflictException("Project changed. Refresh before saving.");
        switch (action)
        {
            case "publish": if (project.DeletedAt != null) throw new InputException("Restore the project first."); project.Visibility = Visibility.Published; project.PublishedAt ??= DateTime.UtcNow; break;
            case "unpublish": project.Visibility = Visibility.Draft; break;
            case "archive": project.Visibility = Visibility.Archived; break;
            case "delete": project.DeletedAt = DateTime.UtcNow; project.Visibility = Visibility.Archived; break;
            case "restore": project.DeletedAt = null; project.Visibility = Visibility.Draft; break;
            default: throw new InputException("Unknown action.");
        }
        await store.SaveProject(project, version, ct); await Log(actor, "project:" + action, id, ct);
    }
    public async Task SaveContent(ContentItem item, string actor, CancellationToken ct)
    {
        Validate(item); Validate(item.Configuration); Validate(item.Branding);
        if (item.StartDate > item.EndDate) throw new InputException("End date cannot be before start date.");
        if (!ContentKinds.Contains(item.Kind)) throw new InputException("Unknown content module.");
        if (!SafeUrl(item.Url) || !SafeUrl(item.Configuration.ButtonUrl, true)) throw new InputException("Use valid HTTPS links or a local button path.");
        if (!SafeUrl(item.Branding.GitHubUrl) || !SafeUrl(item.Branding.LinkedInUrl)) throw new InputException("Social links must use HTTPS.");
        if (item.Kind == "sections" && !SectionKeys.Contains(item.Key)) throw new InputException("Choose a supported section key.");
        var list = await store.Content(item.Kind, ct);
        if ((item.Kind is "settings" or "resume" || item.Kind == "sections") && list.Any(x => x.Id != item.Id && (item.Kind != "sections" || x.Key == item.Key))) throw new InputException("This singleton already exists. Edit its existing record.");
        if (item.MediaId != null && item.MediaId.Length > 0 && !(await store.Media(ct)).Any(x => x.Id == item.MediaId && (item.Kind != "resume" || x.ContentType == "application/pdf"))) throw new InputException("Select a valid media asset (PDF for resumes).");
        var previous = await store.ContentItem(item.Id, ct);
        if (previous != null && previous.Kind != item.Kind) throw new InputException("Cannot change content module.");
        item.CreatedAt = previous?.CreatedAt ?? DateTime.UtcNow;
        await store.SaveContent(item, item.Version, ct); await Log(actor, "content:save:" + item.Kind, item.Id, ct);
    }
    public async Task<List<ContentItem>> Sections(CancellationToken ct)
    {
        var list = await store.Content("sections", ct); var order = await GetOrder("sections", ct);
        return order.Ids.Select(id => list.Single(x => x.Id == id)).Where(x => x.IsEnabled).ToList();
    }
    public Task Log(string actor, string action, string id, CancellationToken ct) => store.AddAudit(new AuditEvent { ActorId = actor, Action = action, EntityId = id }, ct);
}
