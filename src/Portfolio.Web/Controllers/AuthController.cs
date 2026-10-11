using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Web.Models;
namespace Portfolio.Web.Controllers;
public sealed class AuthController(IPortfolioStore store, IPasswordHasher<AdminUser> hasher, PortfolioService service) : Controller
{
    [HttpGet("/admin/login")] public IActionResult Login() => View(new LoginForm());
    [HttpPost("/admin/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(form);
        var user = await store.UserByEmail(form.Email.Trim().ToLowerInvariant(), ct);
        var dummy = new AdminUser();
        var hash = user?.PasswordHash ?? hasher.HashPassword(dummy, "unused-dummy-value");
        var result = hasher.VerifyHashedPassword(user ?? dummy, hash, form.Password);
        if (user == null || user.LockoutEnd > DateTime.UtcNow || result == PasswordVerificationResult.Failed)
        {
            if (user != null && !(user.LockoutEnd > DateTime.UtcNow)) { user.AccessFailedCount++; if (user.AccessFailedCount >= 5) { user.LockoutEnd = DateTime.UtcNow.AddMinutes(15); user.AccessFailedCount = 0; } await store.SaveUser(user, user.Version, ct); }
            ModelState.AddModelError("", "Sign-in failed. Check your credentials or wait before trying again."); return View(form);
        }
        user.AccessFailedCount = 0; user.LockoutEnd = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, form.Password);
        await store.SaveUser(user, user.Version, ct);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.Email), new Claim(ClaimTypes.Role, "Admin"), new Claim("stamp", user.SecurityStamp) }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        await service.Log(user.Id, "auth:login", user.Id, ct); return Redirect("/admin");
    }
    [Authorize(Policy = "Admin"), HttpPost("/admin/logout")]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return Redirect("/"); }
    [Authorize(Policy = "Admin"), HttpGet("/admin/security")] public IActionResult Security() => View(new PasswordForm());
    [Authorize(Policy = "Admin"), HttpPost("/admin/security"), EnableRateLimiting("login")]
    public async Task<IActionResult> Security(PasswordForm form, CancellationToken ct)
    {
        var user = await store.UserById(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (!ModelState.IsValid || user == null) return View(form);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, form.CurrentPassword) == PasswordVerificationResult.Failed || !SecurityRules.StrongPassword(form.NewPassword)) { ModelState.AddModelError("", "Verify the current password. New password needs 14+ characters, upper/lowercase, digit and symbol."); return View(form); }
        user.PasswordHash = hasher.HashPassword(user, form.NewPassword); user.SecurityStamp = Guid.NewGuid().ToString("N");
        await store.SaveUser(user, user.Version, ct); await service.Log(user.Id, "auth:password-changed", user.Id, ct);
        await HttpContext.SignOutAsync(); TempData["Message"] = "Password changed. All previous sessions are now invalid."; return Redirect("/admin/login");
    }
}
