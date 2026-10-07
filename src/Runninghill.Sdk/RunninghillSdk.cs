using Runninghill.Application;

namespace Runninghill.Sdk;

/// <summary>Pass-through facade: no validation, persistence, mapping, or business rules.</summary>
public sealed class RunninghillSdk(IRunninghillApplication application) : IRunninghillSdk
{
    public Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
        => application.GetStatusAsync(cancellationToken);
}
