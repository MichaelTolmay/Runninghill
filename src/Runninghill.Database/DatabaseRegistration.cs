using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Runninghill.Application;

namespace Runninghill.Database;

/// <summary>
/// Registers pooled database contexts and the storage services used by the application.
/// </summary>
public static class DatabaseRegistration
{
    /// <summary>
    /// Registers validated settings, context pools, the word repository, readiness checks and the
    /// deployment migrator.
    /// </summary>
    public static IServiceCollection AddCollectionDatabase(this IServiceCollection services, Func<IServiceProvider, DatabaseSettings> settings)
    {
        services.AddSingleton(settings);
        // A context is borrowed per operation, never shared by simultaneous requests.
        // Pools reuse EF bookkeeping; the underlying provider also pools physical connections.
        services.AddPooledDbContextFactory<PostgresContext>((s, o) => Configure(o, s.GetRequiredService<DatabaseSettings>(), DatabaseProvider.PostgreSql));
        services.AddPooledDbContextFactory<SqlServerContext>((s, o) => Configure(o, s.GetRequiredService<DatabaseSettings>(), DatabaseProvider.SqlServer));
        services.AddPooledDbContextFactory<SqliteContext>((s, o) => Configure(o, s.GetRequiredService<DatabaseSettings>(), DatabaseProvider.Sqlite));
        services.AddPooledDbContextFactory<MySqlContext>((s, o) => Configure(o, s.GetRequiredService<DatabaseSettings>(), DatabaseProvider.MySql));
        services.AddSingleton<CollectionContextFactory>();
        services.AddSingleton<IWordRepository, EfWordRepository>();
        services.AddSingleton<IDatabaseReadiness, EfDatabaseReadiness>();
        services.AddSingleton<DatabaseMigrator>();
        return services;
    }

    /// <summary>
    /// Selects the expected EF provider and sets a five-second query timeout, rejecting mismatched
    /// context settings.
    /// </summary>
    internal static void Configure(DbContextOptionsBuilder options, DatabaseSettings settings, DatabaseProvider expected)
    {
        if (settings.Provider != expected) throw new InvalidOperationException("The requested context does not match Database:Provider.");
        // Five seconds leaves time for the API to return a useful failure before its deadline.
        switch (settings.Provider)
        {
            // PostgreSQL's default search_path starts with the user's own schema. Creating
            // "runninghill" must not move/shadow migration history halfway through an upgrade.
            case DatabaseProvider.PostgreSql: options.UseNpgsql(settings.ConnectionString,
                p => p.CommandTimeout(5).MigrationsHistoryTable("__EFMigrationsHistory", "public")); break;
            case DatabaseProvider.SqlServer: options.UseSqlServer(settings.ConnectionString, p => p.CommandTimeout(5)); break;
            case DatabaseProvider.Sqlite: options.UseSqlite(settings.ConnectionString, p => p.CommandTimeout(5)); break;
            case DatabaseProvider.MySql: options.UseMySQL(settings.ConnectionString, p => p.CommandTimeout(5)); break;
        }
    }
}

/// <summary>
/// Selects the context pool for the configured database engine.
/// </summary>
public sealed class CollectionContextFactory(IServiceProvider services, DatabaseSettings settings)
{
    /// <summary>
    /// Borrows a context from the selected provider pool. The caller must dispose it after the
    /// operation so it returns to the pool.
    /// </summary>
    public async Task<CollectionDbContext> CreateAsync(CancellationToken cancellation = default) => settings.Provider switch
    {
        DatabaseProvider.PostgreSql => await services.GetRequiredService<IDbContextFactory<PostgresContext>>().CreateDbContextAsync(cancellation),
        DatabaseProvider.SqlServer => await services.GetRequiredService<IDbContextFactory<SqlServerContext>>().CreateDbContextAsync(cancellation),
        DatabaseProvider.Sqlite => await services.GetRequiredService<IDbContextFactory<SqliteContext>>().CreateDbContextAsync(cancellation),
        _ => await services.GetRequiredService<IDbContextFactory<MySqlContext>>().CreateDbContextAsync(cancellation)
    };
}

// EF tooling gets credentials from the environment, never from source code or CLI arguments.
/// <summary>
/// Supplies provider-specific contexts to EF migration tools using environment credentials or
/// non-production placeholders.
/// </summary>
public sealed class DesignContexts : IDesignTimeDbContextFactory<PostgresContext>, IDesignTimeDbContextFactory<SqlServerContext>,
    IDesignTimeDbContextFactory<SqliteContext>, IDesignTimeDbContextFactory<MySqlContext>
{
    /// <summary>
    /// Builds design-time provider options using the environment connection string when present,
    /// otherwise the supplied placeholder.
    /// </summary>
    private static DbContextOptions<T> Options<T>(DatabaseProvider provider, string placeholder) where T : DbContext
    {
        var options = new DbContextOptionsBuilder<T>();
        DatabaseRegistration.Configure(options, new(provider, Environment.GetEnvironmentVariable("ConnectionStrings__Runninghill") ?? placeholder), provider);
        return options.Options;
    }

    /// <summary>
    /// Creates the PostgreSQL context used by EF tools to generate and manage its migrations.
    /// </summary>
    PostgresContext IDesignTimeDbContextFactory<PostgresContext>.CreateDbContext(string[] args) => new(Options<PostgresContext>(DatabaseProvider.PostgreSql, "Host=localhost;Database=runninghill"));

    /// <summary>
    /// Creates the SQL Server context used by EF tools to generate and manage its migrations.
    /// </summary>
    SqlServerContext IDesignTimeDbContextFactory<SqlServerContext>.CreateDbContext(string[] args) => new(Options<SqlServerContext>(DatabaseProvider.SqlServer, "Server=localhost;Database=runninghill;Integrated Security=true"));

    /// <summary>
    /// Creates the SQLite context used by EF tools to generate and manage its migrations.
    /// </summary>
    SqliteContext IDesignTimeDbContextFactory<SqliteContext>.CreateDbContext(string[] args) => new(Options<SqliteContext>(DatabaseProvider.Sqlite, "Data Source=runninghill.db"));

    /// <summary>
    /// Creates the MySQL context used by EF tools to generate and manage its migrations.
    /// </summary>
    MySqlContext IDesignTimeDbContextFactory<MySqlContext>.CreateDbContext(string[] args) => new(Options<MySqlContext>(DatabaseProvider.MySql, "Server=localhost;Database=runninghill;User=runninghill"));
}
