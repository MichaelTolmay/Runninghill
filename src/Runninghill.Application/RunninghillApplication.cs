namespace Runninghill.Application;

public sealed class RunninghillApplication(IDatabaseReadiness database) : IRunninghillApplication
{
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        // This sample status operation checks the schema itself. Future business operations
        // should do their useful query directly, not add a readiness query before every query.
        if (!await database.IsReadyAsync(cancellationToken))
            throw new ApplicationUnavailableException();

        return "Runninghill is ready. Database schema verified.";
    }
}
