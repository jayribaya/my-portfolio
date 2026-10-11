using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portfolio.Application;
namespace Portfolio.Web;
public static class SecurityRules
{
    public static bool StrongPassword(string value) => value.Length is >= 14 and <= 128 && value.Any(char.IsUpper) && value.Any(char.IsLower) && value.Any(char.IsDigit) && value.Any(c => !char.IsLetterOrDigit(c));
}
public sealed class MongoHealthCheck(IPortfolioStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { await store.Ping(cancellationToken); return HealthCheckResult.Healthy(); }
        catch { return HealthCheckResult.Unhealthy("Database unavailable."); }
    }
}
