using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Runninghill.Diagnostics;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>
/// Checks the actual schema version through EF so an empty or outdated database is not reported as
/// ready.
/// </summary>
public sealed partial class EfDatabaseReadiness(CollectionContextFactory factory, ILogger<EfDatabaseReadiness> logger) : IDatabaseReadiness
{
    /// <summary>
    /// Returns whether the stored schema version matches the application. Database failures are logged
    /// as unavailable; caller cancellation still propagates.
    /// </summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var operation = new OperationLog(logger, "Database.Readiness", diagnostic: true, cancellation: cancellationToken);
        try
        {
            await using var db = await factory.CreateAsync(cancellationToken);
            // Checking connectivity alone misses an empty or outdated database.
            var version = await db.Schema.AsNoTracking().Where(s => s.Id == 1)
                .Select(s => (int?)s.Version).SingleOrDefaultAsync(cancellationToken);
            if (version == DatabaseMigrator.SchemaVersion) return operation.Complete(true);
            LogSchemaMismatch(logger);
            return false;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            LogDatabaseUnavailable(logger, exception.GetType().Name);
            return false;
        }
        // Cancellation belongs to the caller; do not turn it into an outage.
    }

    /// <summary>
    /// Logs guidance to run migrations when the schema marker is absent or out of date.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database schema is not ready. Run the EF database migration command.")]
    private static partial void LogSchemaMismatch(ILogger logger);

    /// <summary>
    /// Records a database-readiness failure for operators to diagnose connectivity or schema problems.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database readiness failed. Error type: {ErrorType}. Check the configured provider, connectivity and migrations.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, string errorType);
}
