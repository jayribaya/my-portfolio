using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Portfolio.Application;
using Portfolio.Domain;
namespace Portfolio.Infrastructure;

public sealed class MongoPortfolioStore : IPortfolioStore
{
    private readonly IMongoDatabase db;
    public MongoPortfolioStore(string connection, string database)
    {
        var pack = new ConventionPack { new IgnoreExtraElementsConvention(true) };
        ConventionRegistry.Register("portfolio", pack, t => t.Namespace == "Portfolio.Domain");
        if (!BsonClassMap.IsClassMapRegistered(typeof(Project))) BsonClassMap.RegisterClassMap<Project>(cm => { cm.AutoMap(); cm.UnmapMember(x => x.IsPublic); });
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        db = new MongoClient(settings).GetDatabase(database);
    }
    public IMongoDatabase Database => db;
    private IMongoCollection<T> Collection<T>(string name) => db.GetCollection<T>(name);
    public async Task Initialize(CancellationToken ct)
    {
        var projects = Collection<Project>("projects");
        await projects.Indexes.CreateManyAsync([
            new(CreateIndexKeys<Project>().Ascending(x => x.Slug), new CreateIndexOptions { Unique = true }),
            new(CreateIndexKeys<Project>().Ascending(x => x.Visibility).Ascending(x => x.DeletedAt).Ascending(x => x.SortOrder)),
            new(CreateIndexKeys<Project>().Ascending(x => x.CategoryIds)),
            new(CreateIndexKeys<Project>().Ascending(x => x.IsFeatured).Ascending(x => x.FeaturedSortOrder))], ct);
        await Collection<AdminUser>("admins").Indexes.CreateOneAsync(new CreateIndexModel<AdminUser>(CreateIndexKeys<AdminUser>().Ascending(x => x.Email), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
        await Collection<ContentItem>("content").Indexes.CreateOneAsync(new CreateIndexModel<ContentItem>(CreateIndexKeys<ContentItem>().Ascending(x => x.Kind).Ascending(x => x.SortOrder)), cancellationToken: ct);
        await Collection<ContentItem>("content").Indexes.CreateOneAsync(new CreateIndexModel<ContentItem>(CreateIndexKeys<ContentItem>().Ascending(x => x.Kind).Ascending(x => x.Key), new CreateIndexOptions<ContentItem> { Unique = true, PartialFilterExpression = Builders<ContentItem>.Filter.In(x => x.Kind, new[] { "sections", "settings", "resume" }) }), cancellationToken: ct);
        await Collection<AuditEvent>("audit").Indexes.CreateOneAsync(new CreateIndexModel<AuditEvent>(CreateIndexKeys<AuditEvent>().Ascending(x => x.CreatedAt), new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(365) }), cancellationToken: ct);
        await Collection<ContactSubmission>("contacts").Indexes.CreateOneAsync(new CreateIndexModel<ContactSubmission>(CreateIndexKeys<ContactSubmission>().Ascending(x => x.CreatedAt), new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90) }), cancellationToken: ct);
        foreach (var scope in new[] { "projects", "featured", "sections" })
            await Collection<OrderDocument>("orders").UpdateOneAsync(x => x.Id == scope, Builders<OrderDocument>.Update.SetOnInsert(x => x.Ids, []).SetOnInsert(x => x.Version, 0), new UpdateOptions { IsUpsert = true }, ct);
    }
    private static IndexKeysDefinitionBuilder<T> CreateIndexKeys<T>() => Builders<T>.IndexKeys;
    private async Task Save<T>(string collection, T item, long version, CancellationToken ct) where T : Entity
    {
        item.UpdatedAt = DateTime.UtcNow; item.Version = version + 1;
        try
        {
            if (version == 0) await Collection<T>(collection).InsertOneAsync(item, cancellationToken: ct);
            else
            {
                var result = await Collection<T>(collection).ReplaceOneAsync(x => x.Id == item.Id && x.Version == version, item, cancellationToken: ct);
                if (result.MatchedCount == 0) throw new ConflictException("This record changed. Refresh before saving.");
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { throw new ConflictException("A record with this identifier or slug already exists. Refresh and retry."); }
    }
    public Task<List<Project>> Projects(CancellationToken ct) => Collection<Project>("projects").Find(_ => true).ToListAsync(ct);
    public Task<Project?> Project(string id, CancellationToken ct) => Collection<Project>("projects").Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task SaveProject(Project project, long expectedVersion, CancellationToken ct) => Save("projects", project, expectedVersion, ct);
    public async Task<OrderDocument> Order(string scope, CancellationToken ct) => await Collection<OrderDocument>("orders").Find(x => x.Id == scope).FirstOrDefaultAsync(ct) ?? new OrderDocument { Id = scope };
    public async Task<OrderDocument> ReplaceOrder(string scope, List<string> ids, long version, CancellationToken ct)
    {
        var result = await Collection<OrderDocument>("orders").FindOneAndUpdateAsync(x => x.Id == scope && x.Version == version,
            Builders<OrderDocument>.Update.Set(x => x.Ids, ids).Inc(x => x.Version, 1), new FindOneAndUpdateOptions<OrderDocument> { ReturnDocument = ReturnDocument.After }, ct);
        return result ?? throw new ConflictException("Ordering changed in another session. Refresh before reordering.");
    }
    public Task<List<ContentItem>> Content(string? kind, CancellationToken ct) => Collection<ContentItem>("content").Find(kind == null ? Builders<ContentItem>.Filter.Empty : Builders<ContentItem>.Filter.Eq(x => x.Kind, kind)).SortBy(x => x.SortOrder).ToListAsync(ct);
    public Task<ContentItem?> ContentItem(string id, CancellationToken ct) => Collection<ContentItem>("content").Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task SaveContent(ContentItem item, long expectedVersion, CancellationToken ct) => Save("content", item, expectedVersion, ct);
    public async Task DeleteContent(string id, long expectedVersion, CancellationToken ct)
    {
        if ((await Collection<ContentItem>("content").DeleteOneAsync(x => x.Id == id && x.Version == expectedVersion, ct)).DeletedCount == 0) throw new ConflictException("Record changed or was deleted.");
    }
    public Task<List<MediaAsset>> Media(CancellationToken ct) => Collection<MediaAsset>("media").Find(_ => true).SortByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task SaveMedia(MediaAsset asset, CancellationToken ct) => Save("media", asset, 0, ct);
    public Task<List<ContactSubmission>> Contacts(CancellationToken ct) => Collection<ContactSubmission>("contacts").Find(_ => true).SortByDescending(x => x.CreatedAt).Limit(500).ToListAsync(ct);
    public Task SaveContact(ContactSubmission item, CancellationToken ct) => Save("contacts", item, 0, ct);
    public async Task MarkContact(string id, bool delete, CancellationToken ct)
    {
        if (delete) await Collection<ContactSubmission>("contacts").DeleteOneAsync(x => x.Id == id, ct);
        else await Collection<ContactSubmission>("contacts").UpdateOneAsync(x => x.Id == id, Builders<ContactSubmission>.Update.Set(x => x.IsRead, true), cancellationToken: ct);
    }
    public Task<List<AuditEvent>> Audit(CancellationToken ct) => Collection<AuditEvent>("audit").Find(_ => true).SortByDescending(x => x.CreatedAt).Limit(200).ToListAsync(ct);
    public Task AddAudit(AuditEvent item, CancellationToken ct) => Save("audit", item, 0, ct);
    public Task<AdminUser?> UserByEmail(string email, CancellationToken ct) => Collection<AdminUser>("admins").Find(x => x.Email == email.ToLowerInvariant()).FirstOrDefaultAsync(ct)!;
    public Task<AdminUser?> UserById(string id, CancellationToken ct) => Collection<AdminUser>("admins").Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task SaveUser(AdminUser user, long expectedVersion, CancellationToken ct) => Save("admins", user, expectedVersion, ct);
    public Task Ping(CancellationToken ct) => db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
}
// GridFS is durable object storage in MongoDB, never in the web server's ephemeral disk.
// Assets are streamed through authorization-aware routes; their bucket identifiers are private.
public sealed class GridFsMediaStorage(IMongoDatabase database) : IMediaStorage
{
    private readonly GridFSBucket bucket = new(database);
    public async Task<string> Upload(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        await bucket.UploadFromBytesAsync(key, bytes, new GridFSUploadOptions { Metadata = new BsonDocument("contentType", contentType) }, ct);
        return key;
    }
    public async Task<(Stream Stream, string ContentType)> Open(string key, CancellationToken ct)
    {
        var stream = await bucket.OpenDownloadStreamByNameAsync(key, cancellationToken: ct);
        return (stream, stream.FileInfo.Metadata["contentType"].AsString);
    }
}

