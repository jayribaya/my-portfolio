using System.ComponentModel.DataAnnotations;

namespace Portfolio.Domain;

public enum ProjectStatus { Completed, InProgress, Planned }
public enum Visibility { Draft, Published, Archived }
public abstract class Entity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class Project : Entity
{
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [StringLength(140)] public string Slug { get; set; } = "";
    [Required, StringLength(400)] public string Summary { get; set; } = "";
    [StringLength(12000)] public string Description { get; set; } = "";
    [StringLength(6000)] public string ProblemStatement { get; set; } = "";
    [StringLength(6000)] public string Solution { get; set; } = "";
    [StringLength(300)] public string Role { get; set; } = "";
    public string[] Responsibilities { get; set; } = [];
    [StringLength(6000)] public string Architecture { get; set; } = "";
    public string[] Technologies { get; set; } = [];
    public string[] CategoryIds { get; set; } = [];
    public string[] Features { get; set; } = [];
    public string[] Outcomes { get; set; } = [];
    public string? CoverImageId { get; set; }
    public string[] GalleryImageIds { get; set; } = [];
    public string? GitHubUrl { get; set; }
    public string? DemoUrl { get; set; }
    public string? CaseStudyUrl { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public ProjectStatus ProjectStatus { get; set; }
    public Visibility Visibility { get; set; }
    public bool IsFeatured { get; set; }
    public int FeaturedSortOrder { get; set; }
    public int SortOrder { get; set; }
    [StringLength(70)] public string SeoTitle { get; set; } = "";
    [StringLength(170)] public string SeoDescription { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public DateTime? PublishedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsPublic => Visibility == Visibility.Published && DeletedAt == null;
}
public sealed class OrderDocument
{
    public string Id { get; set; } = "projects";
    public List<string> Ids { get; set; } = [];
    public long Version { get; set; }
}
// Separate typed payloads share an editorial envelope; no arbitrary HTML is rendered.
public class ContentItem : Entity
{
    [Required, StringLength(80)] public string Kind { get; set; } = "";
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [StringLength(16000)] public string Content { get; set; } = "";
    [StringLength(400)] public string Subtitle { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    [StringLength(80)] public string Key { get; set; } = "";
    public string? Url { get; set; }
    public string? MediaId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public SectionConfiguration Configuration { get; set; } = new();
}
public sealed class SectionConfiguration
{
    [Range(1, 24)] public int ItemLimit { get; set; } = 6;
    [StringLength(80)] public string ButtonLabel { get; set; } = "View selected work";
    public string ButtonUrl { get; set; } = "/projects";
}
public sealed class PageSection : ContentItem;
public sealed class Skill : ContentItem;
public sealed class Technology : ContentItem;
public sealed class Category : ContentItem;
public sealed class WorkExperience : ContentItem;
public sealed class Education : ContentItem;
public sealed class Certification : ContentItem;
public sealed class SiteSettings : ContentItem;
public sealed class ResumeAsset : ContentItem;
public sealed class Article : ContentItem;
public sealed class MediaAsset : Entity
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string AltText { get; set; } = "";
}
public sealed class ContactSubmission : Entity
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(4000, MinimumLength = 10)] public string Message { get; set; } = "";
    public bool IsRead { get; set; }
}
public sealed class AuditEvent : Entity
{
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
}
public sealed class AdminUser : Entity
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
}
