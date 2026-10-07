using Microsoft.Extensions.DependencyInjection;
using Runninghill.Sdk;

if (args.Length > 1 || (args.Length == 1 && args[0] != "status"))
{
    Console.Error.WriteLine("Usage: Runninghill.Cli [status]");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var services = new ServiceCollection();
services.AddRunninghillSdk(Environment.GetEnvironmentVariable("RUNNINGHILL_CONNECTION_STRING")
    ?? "Data Source=runninghill.db");
await using var provider = services.BuildServiceProvider();

try
{
    Console.WriteLine(await provider.GetRequiredService<IRunninghillSdk>().GetStatusAsync(cancellation.Token));
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Unable to get application status: {exception.Message}");
    return 1;
}
