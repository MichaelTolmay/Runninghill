using Runninghill.Application;
using Xunit;

namespace Runninghill.Tests;

/// <summary>
/// Checks how application status handles database readiness and cancellation.
/// </summary>
public sealed class ApplicationTests
{
    /// <summary>
    /// Verifies that a ready schema produces the application ready message.
    /// </summary>
    [Fact]
    public async Task ReadyDatabaseAllowsApplicationStatus()
        => Assert.Contains("schema verified", await new RunninghillApplication(new StubDatabase(true)).GetStatusAsync());

    /// <summary>
    /// Verifies that an unavailable schema becomes an application-unavailable error.
    /// </summary>
    [Fact]
    public async Task MissingSchemaCannotReportReady()
        => await Assert.ThrowsAsync<ApplicationUnavailableException>(() =>
            new RunninghillApplication(new StubDatabase(false)).GetStatusAsync());

    /// <summary>
    /// Verifies that cancellation reaches the readiness check instead of producing a ready result.
    /// </summary>
    [Fact]
    public async Task CancellationReachesDatabase()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RunninghillApplication(new StubDatabase(true)).GetStatusAsync(source.Token));
    }
}

/// <summary>
/// Provides a fixed readiness answer for tests without connecting to a database.
/// </summary>
internal sealed class StubDatabase(bool ready) : IDatabaseReadiness
{
    /// <summary>
    /// Honours a cancelled token, otherwise returns the readiness value supplied by the test.
    /// </summary>
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ready);
    }
}
