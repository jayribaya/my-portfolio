using MongoDB.Driver;
using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Infrastructure;
namespace Portfolio.Tests;
public sealed class MongoTests
{
    public static bool HasMongo => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PORTFOLIO_TEST_MONGO"));
    [Fact(Skip = "Set PORTFOLIO_TEST_MONGO to enable the disposable database integration test.", SkipUnless = nameof(HasMongo))]
    public async Task Mongo_indexes_concurrency_restart_visibility_and_durable_media()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = Environment.GetEnvironmentVariable("PORTFOLIO_TEST_MONGO")!;
        var database = "portfolio_test_" + Guid.NewGuid().ToString("N");
        var store = new MongoPortfolioStore(connection, database);
        try
        {
            await store.Initialize(ct); await SeedData.Seed(store, ct); await store.Ping(ct);
            var service = new PortfolioService(store); var order = await service.GetOrder("projects", ct);
            var reverse = order.Ids.AsEnumerable().Reverse().ToList(); await service.Reorder("projects", new(reverse, 0), "test", ct);
            var reopened = new MongoPortfolioStore(connection, database); await reopened.Initialize(ct);
            Assert.Equal(reverse, (await new PortfolioService(reopened).GetOrder("projects", ct)).Ids);
            await Assert.ThrowsAsync<ConflictException>(() => reopened.ReplaceOrder("projects", order.Ids, 0, ct));
            var p = (await reopened.Project(reverse[0], ct))!;
            await Assert.ThrowsAsync<ConflictException>(() => reopened.SaveProject(new Project { Title = "Duplicate", Summary = "Duplicate", Slug = p.Slug }, 0, ct));
            var stale = (await store.Project(p.Id, ct))!; await service.ChangeVisibility(p.Id, "unpublish", p.Version, "test", ct);
            await Assert.ThrowsAsync<ConflictException>(() => reopened.SaveProject(stale, stale.Version, ct));
            Assert.DoesNotContain(await new PortfolioService(reopened).OrderedProjects(true, false, ct), x => x.Id == p.Id);
            var storage = new GridFsMediaStorage(store.Database); var bytes = "durable media payload"u8.ToArray();
            await storage.Upload("test-image.png", bytes, "image/png", ct);
            var file = await new GridFsMediaStorage(reopened.Database).Open("test-image.png", ct);
            await using var stream = file.Stream; using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct);
            Assert.Equal(bytes, buffer.ToArray()); Assert.Equal("image/png", file.ContentType);
            var indexNames = await store.Database.GetCollection<Project>("projects").Indexes.ListAsync(ct);
            Assert.True((await indexNames.ToListAsync(ct)).Count >= 5);
            var section = (await store.ContentItem("section-skills", ct))!; section.IsEnabled = false; await service.SaveContent(section, "test", ct);
            Assert.DoesNotContain(await new PortfolioService(reopened).Sections(ct), x => x.Key == "skills");
        }
        finally
        {
            // Only the generated database for this test is ever dropped.
            if (database.StartsWith("portfolio_test_", StringComparison.Ordinal)) await new MongoClient(connection).DropDatabaseAsync(database, CancellationToken.None);
        }
    }
}

