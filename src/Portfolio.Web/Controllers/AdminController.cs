using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Web.Models;
namespace Portfolio.Web.Controllers;
[Authorize(Policy = "Admin")]
public sealed class AdminController(IPortfolioStore store, PortfolioService service) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("/admin/order/{scope}")]
    public async Task<IActionResult> Ordering(string scope, CancellationToken ct)
    {
        var order = await service.GetOrder(scope, ct);
        var titles = scope == "sections" ? (await store.Content("sections", ct)).ToDictionary(x => x.Id, x => x.Title) : (await store.Projects(ct)).ToDictionary(x => x.Id, x => x.Title);
        return View(new OrderingModel(order, titles));
    }
    [HttpGet("/admin")] public async Task<IActionResult> Dashboard(CancellationToken ct) => View(new DashboardModel(await store.Projects(ct), await store.Audit(ct), (await store.Contacts(ct)).Count(x => !x.IsRead)));
    [HttpGet("/admin/projects")] public async Task<IActionResult> Projects(string? q, string? visibility, string? category, int page = 1, CancellationToken ct = default)
    {
        var projects = await service.OrderedProjects(false, false, ct);
        bool filtered = !string.IsNullOrEmpty(q) || !string.IsNullOrEmpty(visibility) || !string.IsNullOrEmpty(category);
        projects = projects.Where(x => (string.IsNullOrEmpty(q) || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(visibility) || (visibility == "Deleted" ? x.DeletedAt != null : x.DeletedAt == null && x.Visibility.ToString() == visibility)) && (string.IsNullOrEmpty(category) || x.CategoryIds.Contains(category))).ToList();
        var total = projects.Count; page = Math.Clamp(page, 1, 10000);
        return View(new ProjectListModel(projects.Skip((page - 1) * 30).Take(30).ToList(), await service.GetOrder("projects", ct), await service.GetOrder("featured", ct), await store.Content("categories", ct), page, total, filtered || total > 30));
    }
    [HttpGet("/admin/projects/new")] public async Task<IActionResult> NewProject(CancellationToken ct) { await Options(ct); return View("EditProject", new Project()); }
    [HttpPost("/admin/projects/bulk")]
    public async Task<IActionResult> Bulk(string[] selected, string action, CancellationToken ct)
    {
        if (selected == null || selected.Length is < 1 or > 30 || selected.Distinct().Count() != selected.Length || !new[] { "publish", "unpublish", "archive", "delete", "restore" }.Contains(action)) throw new InputException("Select 1–30 distinct projects and a valid action.");
        var entries = new List<(string Id, long Version)>();
        foreach (var entry in selected) { var parts = entry.Split(':'); if (parts.Length != 2 || !long.TryParse(parts[1], out var version) || version < 1) throw new InputException("Invalid selection. Refresh the list."); entries.Add((parts[0], version)); }
        var completed = 0; var errors = new List<string>();
        // Each record is version-checked independently. Report partial successes explicitly.
        foreach (var entry in entries)
        {
            try { await service.ChangeVisibility(entry.Id, action, entry.Version, Actor, ct); completed++; }
            catch (Exception ex) when (ex is InputException or ConflictException) { errors.Add(ex.Message); }
        }
        TempData["Message"] = $"{completed} project(s) updated." + (errors.Count > 0 ? $" {errors.Count} skipped: " + string.Join(" ", errors.Distinct()) : ""); return Redirect("/admin/projects");
    }
    [HttpGet("/admin/projects/{id}/edit")] public async Task<IActionResult> EditProject(string id, CancellationToken ct) { var project = await store.Project(id, ct); if (project == null) return NotFound(); await Options(ct); return View(project); }
    private async Task Options(CancellationToken ct) { ViewData["Media"] = await store.Media(ct); ViewData["Categories"] = await store.Content("categories", ct); ViewData["Technologies"] = await store.Content("technologies", ct); }
    [HttpPost("/admin/projects/save")]
    public async Task<IActionResult> SaveProject(Project project, CancellationToken ct)
    {
        try { if (!ModelState.IsValid) throw new InputException("Check the highlighted fields."); await service.SaveProject(project, Actor, ct); TempData["Message"] = "Project saved."; return Redirect("/admin/projects/" + project.Id + "/edit"); }
        catch (Exception ex) when (ex is InputException or ConflictException) { ModelState.AddModelError("", ex.Message); Response.StatusCode = ex is ConflictException ? 409 : 400; await Options(ct); return View("EditProject", project); }
    }
    [HttpPost("/admin/projects/{id}/action")]
    public async Task<IActionResult> ProjectAction(string id, string action, long version, CancellationToken ct) { await service.ChangeVisibility(id, action, version, Actor, ct); TempData["Message"] = "Project " + action + " completed."; return Redirect("/admin/projects"); }
    [HttpGet("/admin/projects/{id}/preview")]
    public async Task<IActionResult> Preview(string id, CancellationToken ct) { var project = await store.Project(id, ct); return project == null ? NotFound() : View("~/Views/Public/Project.cshtml", new CaseStudyModel(project, await store.Media(ct), true)); }
    [HttpPost("/admin/api/projects/reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderRequest? request, CancellationToken ct) => await SaveOrder("projects", request, ct);
    [HttpPost("/admin/api/featured/reorder")]
    public async Task<IActionResult> Featured([FromBody] ReorderRequest? request, CancellationToken ct) => await SaveOrder("featured", request, ct);
    [HttpPost("/admin/api/sections/reorder")]
    public async Task<IActionResult> Sections([FromBody] ReorderRequest? request, CancellationToken ct) => await SaveOrder("sections", request, ct);
    private async Task<IActionResult> SaveOrder(string scope, ReorderRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null) return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid ordering request", Detail = "Send an ordered IDs array and an integer version." });
        return Ok(await service.Reorder(scope, request, Actor, ct));
    }
    [HttpGet("/admin/content/{kind}")]
    public async Task<IActionResult> ContentList(string kind, CancellationToken ct) { if (!PortfolioService.ContentKinds.Contains(kind)) return NotFound(); return View("Content", new ContentListModel(kind, await store.Content(kind, ct), kind == "sections" ? await service.GetOrder("sections", ct) : null)); }
    [HttpGet("/admin/content/{kind}/new")]
    public async Task<IActionResult> NewContent(string kind, CancellationToken ct) { if (!PortfolioService.ContentKinds.Contains(kind)) return NotFound(); await Options(ct); return View("EditContent", new ContentItem { Kind = kind }); }
    [HttpGet("/admin/content/{kind}/{id}/edit")]
    public async Task<IActionResult> EditContent(string kind, string id, CancellationToken ct) { var item = await store.ContentItem(id, ct); if (item == null || item.Kind != kind) return NotFound(); await Options(ct); return View(item); }
    [HttpPost("/admin/content/save")]
    public async Task<IActionResult> SaveContent(ContentItem item, CancellationToken ct)
    {
        try { if (!ModelState.IsValid) throw new InputException("Check the highlighted fields."); await service.SaveContent(item, Actor, ct); TempData["Message"] = "Content saved."; return Redirect("/admin/content/" + item.Kind); }
        catch (Exception ex) when (ex is InputException or ConflictException) { ModelState.AddModelError("", ex.Message); Response.StatusCode = ex is ConflictException ? 409 : 400; await Options(ct); return View("EditContent", item); }
    }
    [HttpPost("/admin/content/{id}/delete")]
    public async Task<IActionResult> DeleteContent(string id, long version, CancellationToken ct) { var item = await store.ContentItem(id, ct); if (item == null) return NotFound(); await store.DeleteContent(id, version, ct); await service.Log(Actor, "content:delete", id, ct); return Redirect("/admin/content/" + item.Kind); }
    [HttpGet("/admin/contacts")] public async Task<IActionResult> Contacts(CancellationToken ct) => View(await store.Contacts(ct));
    [HttpPost("/admin/contacts/{id}/action")] public async Task<IActionResult> ContactAction(string id, bool delete, CancellationToken ct) { await store.MarkContact(id, delete, ct); await service.Log(Actor, delete ? "contact:delete" : "contact:read", id, ct); return Redirect("/admin/contacts"); }
    [HttpGet("/admin/audit")] public async Task<IActionResult> Audit(CancellationToken ct) => View(await store.Audit(ct));
    [HttpGet("/admin/export")]
    public async Task<IActionResult> Export(CancellationToken ct) => File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { projects = await store.Projects(ct), content = await store.Content(null, ct), orders = new[] { await service.GetOrder("projects", ct), await service.GetOrder("featured", ct), await service.GetOrder("sections", ct) }, media = await store.Media(ct) }), "application/json", "portfolio-content-export.json");
}
