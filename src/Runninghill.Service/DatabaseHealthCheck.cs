using Microsoft.Extensions.Diagnostics.HealthChecks;
using Runninghill.Application;

namespace Runninghill.Service;

// A health probe uses the same database check as the application, so they cannot disagree
// about which schema version is needed. It returns no connection details to the caller.
public sealed class DatabaseHealthCheck(IDatabaseReadiness database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await database.IsReadyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Required database schema is unavailable.");
}
