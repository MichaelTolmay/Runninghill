using Microsoft.Extensions.Diagnostics.HealthChecks;
using Runninghill.Application;

namespace Runninghill.Service;

public sealed class DatabaseHealthCheck(IDatabaseReadiness database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await database.IsReadyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Required database schema is unavailable.");
}
