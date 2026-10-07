using Microsoft.Extensions.Logging;
using Npgsql;
using Runninghill.Application;

namespace Runninghill.Database;

public sealed class PostgresReadiness(NpgsqlDataSource dataSource, ILogger<PostgresReadiness> logger)
    : IDatabaseReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT version FROM runninghill.schema_info WHERE id = 1");
            command.CommandTimeout = 5;
            return await command.ExecuteScalarAsync(cancellationToken) is int version && version == 1;
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning(exception, "Database or required schema is unavailable.");
            return false;
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Database readiness check timed out.");
            return false;
        }
    }
}
