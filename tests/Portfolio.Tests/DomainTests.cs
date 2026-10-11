using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Infrastructure;
namespace Portfolio.Tests;
public sealed class DomainTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Theory][InlineData("Héllo, World!", "hello-world")][InlineData("  C# + .NET  ", "c-net")][InlineData("!!!", "project")]
    public void Slugs_are_safe(string title, string expected) => Assert.Equal(expected, PortfolioService.Slug(title));
    [Fact] public void Canonical_order_keeps_existing_and_appends_new() => Assert.Equal(new[] { "b", "a", "c" }, PortfolioService.Canonical(["b", "gone", "a", "a"], ["a", "b", "c"]));
    [Theory][InlineData(Visibility.Draft, false)][InlineData(Visibility.Archived, false)][InlineData(Visibility.Published, true)]
    public void Publication_rule_is_separate_from_work_status(Visibility visibility, bool expected) => Assert.Equal(expected, new Project { Visibility = visibility, ProjectStatus = ProjectStatus.Planned }.IsPublic);
    [Fact] public void Deleted_project_is_always_private() => Assert.False(new Project { Visibility = Visibility.Published, DeletedAt = DateTime.UtcNow }.IsPublic);
    [Theory][InlineData("javascript:alert(1)", false)][InlineData("http://example.com", false)][InlineData("https://example.com", true)][InlineData("https://user:pass@example.com", false)]
    public void External_links_are_restricted(string value, bool expected) => Assert.Equal(expected, PortfolioService.SafeUrl(value));
    [Theory][InlineData("//evil.example")][InlineData("/\\evil.example")]
    public void Local_button_links_cannot_be_open_redirects(string value) => Assert.False(PortfolioService.SafeUrl(value, true));
    [Fact] public async Task Reorder_is_independent_and_rejects_stale_duplicate_unknown_or_filtered_input()
    {
        var store = new MemoryPortfolioStore(); await SeedData.Seed(store, Ct); var service = new PortfolioService(store);
        var order = await service.GetOrder("projects", Ct); var featured = await service.GetOrder("featured", Ct);
        var reverse = order.Ids.AsEnumerable().Reverse().ToList();
        var saved = await service.Reorder("projects", new(reverse, order.Version), "test", Ct);
        Assert.Equal(reverse, (await service.GetOrder("projects", Ct)).Ids); Assert.Equal(1, saved.Version);
        Assert.Equal(featured.Ids, (await service.GetOrder("featured", Ct)).Ids);
        await Assert.ThrowsAsync<ConflictException>(() => service.Reorder("projects", new(reverse, 0), "test", Ct));
        await Assert.ThrowsAsync<InputException>(() => service.Reorder("projects", new([order.Ids[0], order.Ids[0]], 1), "test", Ct));
        await Assert.ThrowsAsync<InputException>(() => service.Reorder("projects", new(["unknown"], 1), "test", Ct));
        await Assert.ThrowsAsync<InputException>(() => service.Reorder("projects", new(order.Ids.Take(1).ToList(), 1), "test", Ct));
        var next = new Project { Title = "Next", Summary = "New project" }; await service.SaveProject(next, "test", Ct);
        Assert.Equal(next.Id, (await service.GetOrder("projects", Ct)).Ids.Last());
        var featuredReverse = featured.Ids.AsEnumerable().Reverse().ToList(); await service.Reorder("featured", new(featuredReverse, featured.Version), "test", Ct);
        Assert.Equal(reverse, (await service.GetOrder("projects", Ct)).Ids.Take(reverse.Count));
    }
    [Fact] public async Task Concurrent_order_updates_have_one_winner()
    {
        var store = new MemoryPortfolioStore(); await SeedData.Seed(store, Ct); var service = new PortfolioService(store); var order = await service.GetOrder("projects", Ct);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { try { await service.Reorder("projects", new(order.Ids, 0), "test", Ct); return true; } catch (ConflictException) { return false; } }));
        Assert.Single(results, x => x);
    }
    [Fact] public async Task Slug_collisions_and_stale_edits_are_handled()
    {
        var store = new MemoryPortfolioStore(); var service = new PortfolioService(store);
        var first = new Project { Title = "Same", Summary = "First" }; var second = new Project { Title = "Same", Summary = "Second" };
        await service.SaveProject(first, "test", Ct); await service.SaveProject(second, "test", Ct); Assert.Equal("same-2", second.Slug);
        var stale = (await store.Project(first.Id, Ct))!; await service.ChangeVisibility(first.Id, "publish", first.Version, "test", Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveProject(stale, "test", Ct));
        var current = (await store.Project(first.Id, Ct))!; await service.ChangeVisibility(first.Id, "delete", current.Version, "test", Ct);
        current = (await store.Project(first.Id, Ct))!; Assert.False(current.IsPublic);
        await service.ChangeVisibility(first.Id, "restore", current.Version, "test", Ct); current = (await store.Project(first.Id, Ct))!;
        Assert.Null(current.DeletedAt); Assert.Equal(Visibility.Draft, current.Visibility);
    }
    [Fact] public void Upload_security_rejects_spoofed_svg_and_oversize_files()
    {
        var png = new byte[20]; new byte[] {137,80,78,71,13,10,26,10}.CopyTo(png, 0); Assert.Equal(".png", UploadRules.Validate(png, "image/png"));
        Assert.Throws<InputException>(() => UploadRules.Validate(png, "image/jpeg"));
        Assert.Throws<InputException>(() => UploadRules.Validate("<svg onload='alert(1)' />"u8.ToArray(), "image/svg+xml"));
        Assert.Throws<InputException>(() => UploadRules.Validate(new byte[UploadRules.MaxBytes + 1], "image/png"));
    }
}
