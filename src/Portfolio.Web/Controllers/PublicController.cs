using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Web.Models;
namespace Portfolio.Web.Controllers;
public sealed class PublicController(IPortfolioStore store, PortfolioService service) : Controller
{
    [HttpGet("/")] public async Task<IActionResult> Home(CancellationToken ct) => View(await Model(ct));
    private async Task<PublicModel> Model(CancellationToken ct) => new() { Projects = await service.OrderedProjects(true, false, ct), Sections = await service.Sections(ct), Content = (await store.Content(null, ct)).Where(x => x.IsEnabled).ToList(), Media = await store.Media(ct) };
    [HttpGet("/projects")] public async Task<IActionResult> Projects(string? q, string? category, string? technology, int page = 1, CancellationToken ct = default)
    {
        var model = await Model(ct);
        model.Projects = model.Projects.Where(x => (string.IsNullOrWhiteSpace(q) || x.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(category) || x.CategoryIds.Contains(category)) && (string.IsNullOrEmpty(technology) || x.Technologies.Contains(technology))).ToList();
        model.Total = model.Projects.Count; model.Page = Math.Clamp(page, 1, 10000); model.Projects = model.Projects.Skip((model.Page - 1) * 9).Take(9).ToList(); return View(model);
    }
    [HttpGet("/projects/{slug}")] public async Task<IActionResult> Project(string slug, CancellationToken ct)
    {
        var project = (await store.Projects(ct)).FirstOrDefault(x => x.Slug == slug && x.IsPublic);
        return project == null ? NotFound() : View(new CaseStudyModel(project, await store.Media(ct), false));
    }
    [HttpGet("/about")][HttpGet("/skills")][HttpGet("/experience")][HttpGet("/education")][HttpGet("/certifications")][HttpGet("/articles")][HttpGet("/resume")]
    public async Task<IActionResult> ContentPage(CancellationToken ct) { ViewData["Kind"] = Request.Path.Value!.Trim('/'); return View("Content", await Model(ct)); }
    [HttpGet("/contact")] public IActionResult Contact() => View(new ContactForm());
    [HttpPost("/contact"), EnableRateLimiting("contact")]
    public async Task<IActionResult> Contact(ContactForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        if (!string.IsNullOrEmpty(form.Website)) { TempData["Message"] = "Thanks for your message."; return Redirect("/contact"); }
        await store.SaveContact(new ContactSubmission { Name = form.Name, Email = form.Email, Message = form.Message }, ct);
        TempData["Message"] = "Thanks for reaching out. Your message has been saved securely."; return Redirect("/contact");
    }
    [HttpGet("/api/projects")] public async Task<IActionResult> Api(int page = 1, CancellationToken ct = default) => Ok((await service.OrderedProjects(true, false, ct)).Skip((Math.Clamp(page, 1, 10000) - 1) * 20).Take(20).Select(x => new { x.Title, x.Slug, x.Summary, x.Technologies, x.IsFeatured }));
    [HttpGet("/sitemap.xml")] public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = new[] { "/", "/about", "/projects", "/skills", "/experience", "/education", "/contact" }.Concat((await service.OrderedProjects(true, false, ct)).Select(x => "/projects/" + x.Slug));
        var origin = $"{Request.Scheme}://{Request.Host}";
        return Content(new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement(ns + "urlset", urls.Select(x => new System.Xml.Linq.XElement(ns + "url", new System.Xml.Linq.XElement(ns + "loc", origin + x))))).ToString(), "application/xml");
    }
    [HttpGet("/robots.txt")] public IActionResult Robots() => Content("User-agent: *\nDisallow: /admin\nDisallow: /media\nAllow: /\n", "text/plain");
    [HttpGet("/error/{code:int}")] public IActionResult Error(int code) { Response.StatusCode = code; ViewData["Code"] = code; return View(); }
}
