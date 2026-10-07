namespace Runninghill.Sdk;

public interface IRunninghillSdk
{
    Task<string> GetStatusAsync(CancellationToken cancellationToken = default);
}
