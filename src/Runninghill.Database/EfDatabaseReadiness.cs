using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runninghill.Application;

namespace Runninghill.Database;

public sealed partial class EfDatabaseReadiness(CollectionContextFactory factory, ILogger<EfDatabaseReadiness> logger) : IDatabaseReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateAsync(cancellationToken);
            // Checking connectivity alone misses an empty or outdated database.
            var version = await db.Schema.AsNoTracking().Where(s => s.Id == 1)
                .Select(s => (int?)s.Version).SingleOrDefaultAsync(cancellationToken);
            if (version == DatabaseMigrator.SchemaVersion) return true;
            LogSchemaMismatch(logger);
            return false;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            LogDatabaseUnavailable(logger, exception);
            return false;
        }
        // Cancellation belongs to the caller; do not turn it into an outage.
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database schema is not ready. Run the EF database migration command.")]
    private static partial void LogSchemaMismatch(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database readiness failed. Check the configured provider, connectivity and migrations.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception);
}
