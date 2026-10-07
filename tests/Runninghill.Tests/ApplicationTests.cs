using Runninghill.Application;
using Xunit;

namespace Runninghill.Tests;

public sealed class ApplicationTests
{
    [Fact]
    public async Task ReadyDatabaseAllowsApplicationStatus()
        => Assert.Contains("schema verified", await new RunninghillApplication(new StubDatabase(true)).GetStatusAsync());

    [Fact]
    public async Task MissingSchemaCannotReportReady()
        => await Assert.ThrowsAsync<ApplicationUnavailableException>(() =>
            new RunninghillApplication(new StubDatabase(false)).GetStatusAsync());

    [Fact]
    public async Task CancellationReachesDatabase()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RunninghillApplication(new StubDatabase(true)).GetStatusAsync(source.Token));
    }
}

internal sealed class StubDatabase(bool ready) : IDatabaseReadiness
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ready);
    }
}
