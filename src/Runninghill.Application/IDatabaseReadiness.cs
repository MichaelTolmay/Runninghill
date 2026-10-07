namespace Runninghill.Application;

// The application asks this question without needing to know how PostgreSQL works.
// The database project supplies the answer. The token lets the caller stop waiting.
public interface IDatabaseReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
