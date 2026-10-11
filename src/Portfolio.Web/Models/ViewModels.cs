using System.ComponentModel.DataAnnotations;
using Portfolio.Domain;
namespace Portfolio.Web.Models;
public sealed class PublicModel
{
    public List<Project> Projects { get; set; } = [];
    public List<ContentItem> Sections { get; set; } = [];
    public List<ContentItem> Content { get; set; } = [];
    public List<MediaAsset> Media { get; set; } = [];
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public ContentItem? Settings => Content.FirstOrDefault(x => x.Kind == "settings");
}
public record CaseStudyModel(Project Project, List<MediaAsset> Media, bool Preview);
public record ProjectListModel(List<Project> Projects, OrderDocument GlobalOrder, OrderDocument FeaturedOrder, List<ContentItem> Categories, int Page, int Total, bool Filtered);
public record ContentListModel(string Kind, List<ContentItem> Items, OrderDocument? Order);
public record DashboardModel(List<Project> Projects, List<AuditEvent> Audit, int Unread);
public sealed class LoginForm
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}
public sealed class PasswordForm
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required] public string NewPassword { get; set; } = "";
}
public sealed class ContactForm
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(4000, MinimumLength = 10)] public string Message { get; set; } = "";
    [StringLength(200)] public string? Website { get; set; }
}
