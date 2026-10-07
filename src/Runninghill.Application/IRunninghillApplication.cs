namespace Runninghill.Application;

public interface IRunninghillApplication
{
    Task<string> GetStatusAsync(CancellationToken cancellationToken = default);
}
