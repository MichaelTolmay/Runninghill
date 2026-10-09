namespace Runninghill.Application;

// The application asks this question without needing to know how PostgreSQL works.
// The database project supplies the answer. The token lets the caller stop waiting.
/// <summary>
/// Lets application code check whether its required database schema is ready without knowing the
/// database engine.
/// </summary>
public interface IDatabaseReadiness
{
    /// <summary>
    /// Checks whether the required schema is available, allowing the caller to cancel the wait.
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
