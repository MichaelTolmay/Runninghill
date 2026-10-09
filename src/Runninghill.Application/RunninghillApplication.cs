namespace Runninghill.Application;

/// <summary>
/// Provides application status using the database-readiness contract.
/// </summary>
public sealed class RunninghillApplication(IDatabaseReadiness database) : IRunninghillApplication
{
    /// <summary>
    /// Checks the required database schema and returns a ready message, or throws
    /// ApplicationUnavailableException when the check fails.
    /// </summary>
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // This sample status operation checks the schema itself. Future business operations
        // should do their useful query directly, not add a readiness query before every query.
        if (!await database.IsReadyAsync(cancellationToken))
            throw new ApplicationUnavailableException();

        return "Runninghill is ready. Database schema verified.";
    }
}
