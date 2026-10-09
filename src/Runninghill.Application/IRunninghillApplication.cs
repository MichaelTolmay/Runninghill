namespace Runninghill.Application;

// The service calls these use cases. UI, HTTP, and database-specific code stay outside this contract.
/// <summary>
/// Defines application operations independently of HTTP, gRPC and database implementation details.
/// </summary>
public interface IRunninghillApplication
{
    /// <summary>
    /// Returns the application status when its database is ready, or signals that the application is
    /// unavailable.
    /// </summary>
    Task<string> GetStatusAsync(CancellationToken cancellationToken = default);
}
