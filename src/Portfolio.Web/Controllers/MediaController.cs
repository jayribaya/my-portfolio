using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Application;
using Portfolio.Domain;
namespace Portfolio.Web.Controllers;
public sealed class MediaController(IPortfolioStore store, IMediaStorage storage, PortfolioService service) : Controller
{
    [Authorize(Policy = "Admin"), HttpGet("/admin/media")] public async Task<IActionResult> Index(CancellationToken ct) => View(await store.Media(ct));
    [Authorize(Policy = "Admin"), HttpPost("/admin/media/upload"), RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Upload(List<IFormFile> files, string altText, CancellationToken ct)
    {
        try
        {
            if (files.Count is < 1 or > 4 || string.IsNullOrWhiteSpace(altText) || altText.Length > 300) throw new InputException("Choose 1–4 files and descriptive alt text up to 300 characters.");
            var pending = new List<(IFormFile File, byte[] Bytes, string Extension)>();
            foreach (var file in files)
            {
                if (file.Length > UploadRules.MaxBytes) throw new InputException("Each file must be at most 8 MB.");
                using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
                pending.Add((file, bytes, UploadRules.Validate(bytes, file.ContentType)));
            }
            foreach (var upload in pending)
            {
                var key = Guid.NewGuid().ToString("N") + upload.Extension;
                await storage.Upload(key, upload.Bytes, upload.File.ContentType, ct);
                var asset = new MediaAsset { FileName = key, ContentType = upload.File.ContentType, StorageKey = key, Size = upload.Bytes.Length, AltText = altText };
                asset.Url = "/media/" + asset.Id; await store.SaveMedia(asset, ct); await service.Log(User.FindFirstValue(ClaimTypes.NameIdentifier)!, "media:upload", asset.Id, ct);
            }
            TempData["Message"] = "Files uploaded. Assign them to a project or resume to publish."; return Redirect("/admin/media");
        }
        catch (InputException ex) { ModelState.AddModelError("", ex.Message); Response.StatusCode = 400; return View("Index", await store.Media(ct)); }
    }
    [HttpGet("/media/{id}")]
    public async Task<IActionResult> FileAsset(string id, CancellationToken ct)
    {
        var asset = (await store.Media(ct)).FirstOrDefault(x => x.Id == id); if (asset == null) return NotFound();
        if (!User.IsInRole("Admin"))
        {
            var publicProject = (await store.Projects(ct)).Any(x => x.IsPublic && (x.CoverImageId == id || x.GalleryImageIds.Contains(id)));
            var publicResume = (await store.Content("resume", ct)).Any(x => x.IsEnabled && x.MediaId == id);
            if (!publicProject && !publicResume) return NotFound();
        }
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Robots-Tag"] = "noindex";
        var file = await storage.Open(asset.StorageKey, ct);
        return asset.ContentType == "application/pdf" ? File(file.Stream, file.ContentType, asset.FileName) : File(file.Stream, file.ContentType);
    }
}
