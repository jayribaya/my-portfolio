using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application;
using Portfolio.Domain;
namespace Portfolio.Tests;
public sealed class TestApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> { ["DemoMode"] = "true", ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "portfolio-test-keys") }));
    }
}
public sealed partial class WebTests
{
    private const string Email = "test@example.invalid";
    private const string Password = "Test-Only!2026-Long";
    private static HttpClient Client(TestApp app) => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")] private static partial Regex TokenPattern();
    private static async Task<string> Token(HttpClient client, string path) { var html = await client.GetStringAsync(path); return TokenPattern().Match(html).Groups[1].Value; }
    private static async Task SignIn(TestApp app, HttpClient client)
    {
        var store = app.Services.GetRequiredService<IPortfolioStore>();
        var user = new AdminUser { Email = Email }; user.PasswordHash = app.Services.GetRequiredService<IPasswordHasher<AdminUser>>().HashPassword(user, Password); await store.SaveUser(user, 0, CancellationToken.None);
        var token = await Token(client, "/admin/login");
        var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Email"] = Email, ["Password"] = Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/admin", response.Headers.Location?.OriginalString);
        Assert.Contains("httponly", string.Join(";", response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", string.Join(";", response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public async Task Public_pages_render_and_admin_requires_authentication()
    {
        await using var app = new TestApp(); using var client = Client(app);
        foreach (var path in new[] { "/", "/about", "/projects", "/projects/pruone", "/skills", "/experience", "/education", "/certifications", "/articles", "/resume", "/contact", "/admin/login", "/health", "/sitemap.xml", "/robots.txt" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        var denied = await client.GetAsync("/admin"); Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode); Assert.StartsWith("http://localhost/admin/login", denied.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/register")).StatusCode);
        var notFound = await client.GetAsync("/does-not-exist"); Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode); Assert.Contains("A little off course", await notFound.Content.ReadAsStringAsync());
        var response = await client.GetAsync("/"); Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }
    [Fact] public async Task Csrf_is_required_on_login_contacts_and_admin_posts()
    {
        await using var app = new TestApp(); using var client = Client(app);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Email"] = Email, ["Password"] = Password }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string,string> { ["Name"] = "Test", ["Email"] = Email, ["Message"] = "Test message content" }))).StatusCode);
        await SignIn(app, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/api/projects/reorder", new ReorderRequest([], 0))).StatusCode);
    }
    [Fact] public async Task Crud_publication_restore_and_encoded_output_work_through_http()
    {
        await using var app = new TestApp(); using var client = Client(app); await SignIn(app, client);
        var token = await Token(client, "/admin/projects/new");
        var fields = new Dictionary<string,string> { ["Id"] = "test-project", ["Title"] = "<script>alert(1)</script>", ["Summary"] = "Test summary", ["Visibility"] = "Draft", ["ProjectStatus"] = "InProgress", ["__RequestVerificationToken"] = token };
        var response = await client.PostAsync("/admin/projects/save", new FormUrlEncodedContent(fields)); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var store = app.Services.GetRequiredService<IPortfolioStore>(); var project = (await store.Project("test-project", CancellationToken.None))!;
        using var anonymous = Client(app); Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/projects/" + project.Slug)).StatusCode);
        Assert.DoesNotContain(project.Slug, await anonymous.GetStringAsync("/sitemap.xml"));
        token = await Token(client, "/admin/projects/" + project.Id + "/edit");
        var publish = await client.PostAsync($"/admin/projects/{project.Id}/action", new FormUrlEncodedContent(new Dictionary<string,string> { ["action"] = "publish", ["version"] = project.Version.ToString(), ["__RequestVerificationToken"] = token })); Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
        var html = await anonymous.GetStringAsync("/projects/" + project.Slug); Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>alert(1)</script>", html);
        project = (await store.Project(project.Id, CancellationToken.None))!;
        fields["Version"] = "0"; fields["__RequestVerificationToken"] = token; Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/admin/projects/save", new FormUrlEncodedContent(fields))).StatusCode);
        foreach (var action in new[] { "unpublish", "archive", "restore", "delete", "restore" })
        {
            project = (await store.Project(project.Id, CancellationToken.None))!;
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/admin/projects/{project.Id}/action", new FormUrlEncodedContent(new Dictionary<string,string> { ["action"] = action, ["version"] = project.Version.ToString(), ["__RequestVerificationToken"] = token }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/projects/" + project.Slug)).StatusCode);
        }
        foreach (var path in new[] { "/admin", "/admin/projects", "/admin/content/sections", "/admin/content/skills", "/admin/content/settings", "/admin/media", "/admin/contacts", "/admin/audit", "/admin/security", "/admin/projects/test-project/preview" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }
    [Fact] public async Task Reorder_endpoint_checks_complete_scope_csrf_and_versions()
    {
        await using var app = new TestApp(); using var client = Client(app); await SignIn(app, client);
        var token = await Token(client, "/admin/projects/new"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        var service = app.Services.GetRequiredService<PortfolioService>(); var order = await service.GetOrder("projects", CancellationToken.None); var ids = order.Ids.AsEnumerable().Reverse().ToList();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/admin/api/projects/reorder", new ReorderRequest(ids, 0))).StatusCode);
        Assert.Equal(ids, (await service.GetOrder("projects", CancellationToken.None)).Ids);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/admin/api/projects/reorder", new ReorderRequest(ids, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/api/projects/reorder", new ReorderRequest([ids[0]], 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/api/projects/reorder", new ReorderRequest(["unknown"], 1))).StatusCode);
    }
    [Fact] public async Task Content_changes_hide_sections_immediately()
    {
        await using var app = new TestApp(); using var client = Client(app); await SignIn(app, client);
        var store = app.Services.GetRequiredService<IPortfolioStore>(); var section = (await store.ContentItem("section-skills", CancellationToken.None))!;
        var token = await Token(client, "/admin/content/sections/section-skills/edit");
        var response = await client.PostAsync("/admin/content/save", new FormUrlEncodedContent(new Dictionary<string,string> { ["Id"] = section.Id, ["Version"] = section.Version.ToString(), ["Kind"] = "sections", ["Key"] = "skills", ["Title"] = section.Title, ["IsEnabled"] = "false", ["Configuration.ButtonUrl"] = "/projects", ["__RequestVerificationToken"] = token })); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("id=\"skills\"", await client.GetStringAsync("/"));
    }
    [Fact] public async Task Draft_media_is_private_and_upload_signatures_are_checked()
    {
        await using var app = new TestApp(); using var client = Client(app); await SignIn(app, client); var token = await Token(client, "/admin/media");
        using var invalid = new MultipartFormDataContent(); var payload = new ByteArrayContent("fake image bytes here"u8.ToArray()); payload.Headers.ContentType = new("image/png"); invalid.Add(payload, "files", "evil.png"); invalid.Add(new StringContent("Test image"), "altText"); invalid.Add(new StringContent(token), "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/admin/media/upload", invalid)).StatusCode);
        using var valid = new MultipartFormDataContent(); var image = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6x8AAAAASUVORK5CYII=")); image.Headers.ContentType = new("image/png"); valid.Add(image, "files", "safe.png"); valid.Add(new StringContent("Test image"), "altText"); valid.Add(new StringContent(token), "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/admin/media/upload", valid)).StatusCode);
        var store = app.Services.GetRequiredService<IPortfolioStore>(); var asset = Assert.Single(await store.Media(CancellationToken.None)); using var anonymous = Client(app);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(asset.Url)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(asset.Url)).StatusCode);
        var p = new Project { Title = "Media project", Summary = "Test", CoverImageId = asset.Id, Visibility = Visibility.Published }; await app.Services.GetRequiredService<PortfolioService>().SaveProject(p, "test", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(asset.Url)).StatusCode); await app.Services.GetRequiredService<PortfolioService>().ChangeVisibility(p.Id, "unpublish", p.Version, "test", CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(asset.Url)).StatusCode);
    }
    [Fact] public async Task Lockout_and_stamp_revocation_work()
    {
        await using var app = new TestApp(); using var client = Client(app); await SignIn(app, client);
        var store = app.Services.GetRequiredService<IPortfolioStore>(); var user = (await store.UserByEmail(Email, CancellationToken.None))!; user.SecurityStamp = "changed"; await store.SaveUser(user, user.Version, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
        using var other = Client(app); var token = await Token(other, "/admin/login");
        for (var i = 0; i < 5; i++) await other.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["Email"] = Email, ["Password"] = "incorrect", ["__RequestVerificationToken"] = token }));
        user = (await store.UserByEmail(Email, CancellationToken.None))!; Assert.True(user.LockoutEnd > DateTime.UtcNow);
    }
}
