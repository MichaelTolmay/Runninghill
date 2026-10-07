using Runninghill.Database;

namespace Runninghill.Application;

/// <summary>Application use cases and business rules belong in this assembly.</summary>
public sealed class RunninghillApplication(IDatabaseConnectionFactory connections) : IRunninghillApplication
{
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (Convert.ToInt64(result) != 1)
        {
            throw new InvalidOperationException("The database connectivity check failed.");
        }

        return "Runninghill is ready. Database connection verified.";
    }
}
