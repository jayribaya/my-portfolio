using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Portfolio.Application;
using Portfolio.Domain;
using Portfolio.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var demo = builder.Configuration.GetValue<bool>("DemoMode");
if (demo && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing")) throw new InvalidOperationException("DemoMode is forbidden outside Development/Testing.");
builder.Host.UseSerilog((_, logger) => logger.WriteTo.Console().MinimumLevel.Information().MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning));
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSingleton<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();
builder.Services.AddScoped<PortfolioService>();
if (demo)
{
    builder.Services.AddSingleton<MemoryPortfolioStore>();
    builder.Services.AddSingleton<IPortfolioStore>(sp => sp.GetRequiredService<MemoryPortfolioStore>());
    builder.Services.AddSingleton<IMediaStorage>(sp => sp.GetRequiredService<MemoryPortfolioStore>());
}
else
{
    var connection = builder.Configuration["Mongo:ConnectionString"] ?? throw new InvalidOperationException("Set Mongo__ConnectionString. See README, or explicitly enable DemoMode in Development.");
    builder.Services.AddSingleton(new MongoPortfolioStore(connection, builder.Configuration["Mongo:Database"] ?? "jay_portfolio"));
    builder.Services.AddSingleton<IPortfolioStore>(sp => sp.GetRequiredService<MongoPortfolioStore>());
    builder.Services.AddSingleton<IMediaStorage>(sp => new GridFsMediaStorage(sp.GetRequiredService<MongoPortfolioStore>().Database));
}
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/admin/login"; options.AccessDeniedPath = "/admin/login";
    options.Cookie.Name = "Portfolio.Admin"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30); options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var store = context.HttpContext.RequestServices.GetRequiredService<IPortfolioStore>();
        var user = await store.UserById(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", context.HttpContext.RequestAborted);
        if (user == null || context.Principal?.FindFirstValue("stamp") != user.SecurityStamp || user.LockoutEnd > DateTime.UtcNow) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    foreach (var name in new[] { "login", "contact" }) options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = name == "login" ? 10 : 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddDataProtection().SetApplicationName("JayPortfolio").PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
builder.Services.AddHealthChecks().AddCheck<Portfolio.Web.MongoHealthCheck>("database");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (System.Net.IPAddress.TryParse(builder.Configuration["TrustedProxy"], out var proxy)) options.KnownProxies.Add(proxy);
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<IPortfolioStore>();
    await store.Initialize(CancellationToken.None);
    if (demo || args.Contains("--seed")) await SeedData.Seed(store, CancellationToken.None);
    if (args.Contains("--create-admin") || (demo && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PORTFOLIO_ADMIN_EMAIL"))))
    {
        var email = Environment.GetEnvironmentVariable("PORTFOLIO_ADMIN_EMAIL")?.Trim().ToLowerInvariant() ?? "";
        var password = Environment.GetEnvironmentVariable("PORTFOLIO_ADMIN_PASSWORD") ?? "";
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email) || !Portfolio.Web.SecurityRules.StrongPassword(password)) throw new InvalidOperationException("Set a valid PORTFOLIO_ADMIN_EMAIL and a password of 14+ characters containing upper/lowercase, a digit and a symbol.");
        if (await store.UserByEmail(email, CancellationToken.None) != null) throw new InvalidOperationException("Admin already exists. Use the authenticated security page to change credentials.");
        var user = new AdminUser { Email = email };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>().HashPassword(user, password);
        await store.SaveUser(user, 0, CancellationToken.None);
        Console.WriteLine("Admin created. Clear the setup environment variables.");
    }
    if (args.Contains("--seed") || args.Contains("--create-admin")) return;
}
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseExceptionHandler("/error/500");
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    if (context.Request.Path.StartsWithSegments("/admin")) { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; }
    try { await next(); }
    catch (Exception ex) when (ex is InputException or ConflictException)
    {
        context.Response.StatusCode = ex is ConflictException ? 409 : 400;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = context.Response.StatusCode, Title = ex is ConflictException ? "Refresh required" : "Check your input", Detail = ex.Message });
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        Log.Error(ex, "Request failed at {Path}", context.Request.Path);
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) throw;
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 500, Title = "Something went wrong", Detail = "Try again later. The error has been logged." });
    }
});
app.UseWhen(context => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method), branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();

app.MapHealthChecks("/health"); app.MapControllers(); app.Run();
public partial class Program;
