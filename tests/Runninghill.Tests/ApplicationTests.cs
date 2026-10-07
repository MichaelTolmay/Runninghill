using Microsoft.Extensions.DependencyInjection;
using Runninghill.Application;
using Runninghill.Sdk;
using Xunit;

namespace Runninghill.Tests;

public sealed class ApplicationTests
{
    [Fact]
    public async Task SdkRunsApplicationAgainstSqlite()
    {
        var services = new ServiceCollection().AddRunninghillSdk();
        await using var provider = services.BuildServiceProvider();
        var sdk = provider.GetRequiredService<IRunninghillSdk>();

        Assert.Equal("Runninghill is ready. Database connection verified.", await sdk.GetStatusAsync());
    }

    [Fact]
    public async Task CancellationPropagatesToApplication()
    {
        var services = new ServiceCollection().AddRunninghillSdk();
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<IRunninghillSdk>().GetStatusAsync(cancellation.Token));
    }

    [Fact]
    public void SdkForwardsTheSameTaskAndCancellationToken()
    {
        var application = new StubApplication();
        var sdk = new RunninghillSdk(application);
        using var cancellation = new CancellationTokenSource();

        Assert.Same(application.Result, sdk.GetStatusAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, application.ReceivedToken);
    }

    [Fact]
    public async Task DatabaseFailureIsNotReportedAsReady()
    {
        var services = new ServiceCollection().AddRunninghillSdk("Data Source=:memory:;Mode=Invalid");
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetRequiredService<IRunninghillSdk>().GetStatusAsync());
    }

    private sealed class StubApplication : IRunninghillApplication
    {
        public Task<string> Result { get; } = Task.FromResult("forwarded");
        public CancellationToken ReceivedToken { get; private set; }

        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            return Result;
        }
    }
}
