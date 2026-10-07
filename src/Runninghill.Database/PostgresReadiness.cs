using Microsoft.Extensions.Logging;
using Npgsql;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>Checks the small schema marker that tells us this database is ready for this app.</summary>
public sealed partial class PostgresReadiness(NpgsqlDataSource dataSource, ILogger<PostgresReadiness> logger)
    : IDatabaseReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Read one value, not an entire table. Disposing the command returns its borrowed
            // connection to the pool. Do not cache "ready": a later outage must be visible.
            await using var command = dataSource.CreateCommand(
                "SELECT version FROM runninghill.schema_info WHERE id = 1");
            // The database gets less time than the API so the API can still send a useful error.
            command.CommandTimeout = 5;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var ready = result is int version && version == 1;
            if (!ready)
                LogSchemaMismatch(logger);
            return ready;
        }
        catch (NpgsqlException exception)
        {
            LogDatabaseUnavailable(logger, exception);
            return false;
        }
        catch (TimeoutException exception)
        {
            LogTimeout(logger, exception);
            return false;
        }
        // Cancellation is intentionally not caught: the caller asked us to stop, not report an outage.
    }

    // Generated log methods avoid making argument arrays and boxing values for each log call.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database schema marker is missing or unsupported. Apply the expected version 1 migration.")]
    private static partial void LogSchemaMismatch(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database or required schema is unavailable. Check PostgreSQL connectivity and migrations.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database readiness check timed out. Check database load and connection pool usage.")]
    private static partial void LogTimeout(ILogger logger, Exception exception);
}
