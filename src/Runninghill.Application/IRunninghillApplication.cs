namespace Runninghill.Application;

// The service calls these use cases. UI, HTTP, and database-specific code stay outside this contract.
public interface IRunninghillApplication
{
    Task<string> GetStatusAsync(CancellationToken cancellationToken = default);
}
