using Portfolio.Domain;
namespace Portfolio.Application;
public interface IPortfolioStore
{
    Task Initialize(CancellationToken ct);
    Task<List<Project>> Projects(CancellationToken ct);
    Task<Project?> Project(string id, CancellationToken ct);
    Task SaveProject(Project project, long expectedVersion, CancellationToken ct);
    Task<OrderDocument> Order(string scope, CancellationToken ct);
    Task<OrderDocument> ReplaceOrder(string scope, List<string> ids, long version, CancellationToken ct);
    Task<List<ContentItem>> Content(string? kind, CancellationToken ct);
    Task<ContentItem?> ContentItem(string id, CancellationToken ct);
    Task SaveContent(ContentItem item, long expectedVersion, CancellationToken ct);
    Task DeleteContent(string id, long expectedVersion, CancellationToken ct);
    Task<List<MediaAsset>> Media(CancellationToken ct);
    Task SaveMedia(MediaAsset asset, CancellationToken ct);
    Task<List<ContactSubmission>> Contacts(CancellationToken ct);
    Task SaveContact(ContactSubmission item, CancellationToken ct);
    Task MarkContact(string id, bool delete, CancellationToken ct);
    Task<List<AuditEvent>> Audit(CancellationToken ct);
    Task AddAudit(AuditEvent item, CancellationToken ct);
    Task<AdminUser?> UserByEmail(string email, CancellationToken ct);
    Task<AdminUser?> UserById(string id, CancellationToken ct);
    Task SaveUser(AdminUser user, long expectedVersion, CancellationToken ct);
    Task Ping(CancellationToken ct);
}
public interface IMediaStorage
{
    Task<string> Upload(string key, byte[] bytes, string contentType, CancellationToken ct);
    Task<(Stream Stream, string ContentType)> Open(string key, CancellationToken ct);
}
public sealed class ConflictException(string message) : Exception(message);
public sealed class InputException(string message) : Exception(message);
public record ReorderRequest(List<string> Ids, long Version);
