using Microsoft.Extensions.DependencyInjection;
using Runninghill.Database;

namespace Runninghill.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRunninghillApplication(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IDatabaseConnectionFactory>(new SqliteConnectionFactory(connectionString));
        services.AddTransient<IRunninghillApplication, RunninghillApplication>();
        return services;
    }
}
