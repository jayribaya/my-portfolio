using System.Text.Json;
using Portfolio.Application;
using Portfolio.Domain;
namespace Portfolio.Infrastructure;
// Explicit opt-in development/test adapter. Production always requires MongoDB.
public sealed class MemoryPortfolioStore : IPortfolioStore, IMediaStorage
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entity> records = [];
    private readonly Dictionary<string, OrderDocument> orders = [];
    private readonly Dictionary<string, (byte[], string)> files = [];
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private Task<List<T>> All<T>() where T : Entity { lock (gate) return Task.FromResult(records.Values.OfType<T>().Select(Copy).ToList()); }
    private Task<T?> Get<T>(string id) where T : Entity { lock (gate) return Task.FromResult(records.GetValueOrDefault(id) is T value ? Copy(value) : null); }
    private Task Save<T>(T value, long version) where T : Entity
    {
        lock (gate)
        {
            var current = records.GetValueOrDefault(value.Id);
            if ((current == null && version != 0) || (current != null && current.Version != version)) throw new ConflictException("Record changed.");
            if (value is Project p && records.Values.OfType<Project>().Any(x => x.Id != p.Id && x.Slug == p.Slug)) throw new ConflictException("Slug exists.");
            if (value is AdminUser u && records.Values.OfType<AdminUser>().Any(x => x.Id != u.Id && x.Email == u.Email)) throw new ConflictException("Account exists.");
            value.Version = version + 1; value.UpdatedAt = DateTime.UtcNow; records[value.Id] = Copy(value);
        }
        return Task.CompletedTask;
    }
    public Task Initialize(CancellationToken ct) => Task.CompletedTask;
    public Task<List<Project>> Projects(CancellationToken ct) => All<Project>();
    public Task<Project?> Project(string id, CancellationToken ct) => Get<Project>(id);
    public Task SaveProject(Project project, long expectedVersion, CancellationToken ct) => Save(project, expectedVersion);
    public Task<OrderDocument> Order(string scope, CancellationToken ct) { lock (gate) return Task.FromResult(Copy(orders.GetValueOrDefault(scope) ?? new OrderDocument { Id = scope })); }
    public Task<OrderDocument> ReplaceOrder(string scope, List<string> ids, long version, CancellationToken ct)
    {
        lock (gate) { var current = orders.GetValueOrDefault(scope) ?? new OrderDocument { Id = scope }; if (current.Version != version) throw new ConflictException("Ordering changed."); var next = new OrderDocument { Id = scope, Ids = ids.ToList(), Version = version + 1 }; orders[scope] = Copy(next); return Task.FromResult(Copy(next)); }
    }
    public async Task<List<ContentItem>> Content(string? kind, CancellationToken ct) => (await All<ContentItem>()).Where(x => kind == null || x.Kind == kind).OrderBy(x => x.SortOrder).ToList();
    public Task<ContentItem?> ContentItem(string id, CancellationToken ct) => Get<ContentItem>(id);
    public Task SaveContent(ContentItem item, long expectedVersion, CancellationToken ct) => Save(item, expectedVersion);
    public Task DeleteContent(string id, long expectedVersion, CancellationToken ct) { lock (gate) { if (records.GetValueOrDefault(id)?.Version != expectedVersion) throw new ConflictException("Record changed."); records.Remove(id); return Task.CompletedTask; } }
    public Task<List<MediaAsset>> Media(CancellationToken ct) => All<MediaAsset>();
    public Task SaveMedia(MediaAsset asset, CancellationToken ct) => Save(asset, 0);
    public Task<List<ContactSubmission>> Contacts(CancellationToken ct) => All<ContactSubmission>();
    public Task SaveContact(ContactSubmission item, CancellationToken ct) => Save(item, 0);
    public Task MarkContact(string id, bool delete, CancellationToken ct) { lock (gate) { if (delete) records.Remove(id); else if (records.GetValueOrDefault(id) is ContactSubmission contact) contact.IsRead = true; return Task.CompletedTask; } }
    public Task<List<AuditEvent>> Audit(CancellationToken ct) => All<AuditEvent>();
    public Task AddAudit(AuditEvent item, CancellationToken ct) => Save(item, 0);
    public async Task<AdminUser?> UserByEmail(string email, CancellationToken ct) => (await All<AdminUser>()).FirstOrDefault(x => x.Email == email.ToLowerInvariant());
    public Task<AdminUser?> UserById(string id, CancellationToken ct) => Get<AdminUser>(id);
    public Task SaveUser(AdminUser user, long expectedVersion, CancellationToken ct) => Save(user, expectedVersion);
    public Task Ping(CancellationToken ct) => Task.CompletedTask;
    public Task<string> Upload(string key, byte[] bytes, string contentType, CancellationToken ct) { lock (gate) files[key] = (bytes.ToArray(), contentType); return Task.FromResult(key); }
    public Task<(Stream Stream, string ContentType)> Open(string key, CancellationToken ct) { lock (gate) { var file = files[key]; return Task.FromResult<(Stream, string)>((new MemoryStream(file.Item1), file.Item2)); } }
}
