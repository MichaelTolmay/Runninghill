using Microsoft.Extensions.DependencyInjection;
using Runninghill.Application;

namespace Runninghill.Sdk;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRunninghillSdk(
        this IServiceCollection services,
        string connectionString = "Data Source=:memory:")
    {
        services.AddRunninghillApplication(connectionString);
        services.AddTransient<IRunninghillSdk, RunninghillSdk>();
        return services;
    }
}
