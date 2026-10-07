namespace Runninghill.Application;

public interface IDatabaseReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
