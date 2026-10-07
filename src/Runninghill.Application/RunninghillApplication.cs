namespace Runninghill.Application;

public sealed class RunninghillApplication(IDatabaseReadiness database) : IRunninghillApplication
{
    public async Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!await database.IsReadyAsync(cancellationToken))
            throw new ApplicationUnavailableException();

        return "Runninghill is ready. Database schema verified.";
    }
}
