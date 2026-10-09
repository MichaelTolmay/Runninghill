using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Runninghill.Database;

/// <summary>
/// Identifies one of the four supported database engines.
/// </summary>
public enum DatabaseProvider { PostgreSql, SqlServer, Sqlite, MySql }

/// <summary>
/// Holds the selected database engine and its validated connection string.
/// </summary>
public sealed record DatabaseSettings(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>
    /// Resolves a provider name and validates its connection string without exposing credentials.
    /// SQLite must use a persistent file.
    /// </summary>
    public static DatabaseSettings Parse(string? provider, string? connectionString)
    {
        var selected = (provider ?? "Postgres").Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" => DatabaseProvider.PostgreSql,
            "mssql" or "sqlserver" => DatabaseProvider.SqlServer,
            "sqlite" => DatabaseProvider.Sqlite,
            "mysql" => DatabaseProvider.MySql,
            _ => throw new InvalidOperationException("Database:Provider must be Postgres, MSSQL, SQLite, or MySQL.")
        };
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Runninghill is required; no temporary database fallback is allowed.");
        try
        {
            var name = selected switch
            {
                DatabaseProvider.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString).Database,
                DatabaseProvider.SqlServer => new SqlConnectionStringBuilder(connectionString).InitialCatalog,
                DatabaseProvider.MySql => new MySqlConnectionStringBuilder(connectionString).Database,
                _ => new SqliteConnectionStringBuilder(connectionString).DataSource
            };
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException();
            if (selected == DatabaseProvider.Sqlite)
            {
                var sqlite = new SqliteConnectionStringBuilder(connectionString);
                if (sqlite.DataSource == ":memory:" || sqlite.Mode == SqliteOpenMode.Memory)
                    throw new ArgumentException();
            }
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            // Never echo a connection string: it commonly contains a password.
            throw new InvalidOperationException($"ConnectionStrings:Runninghill is not valid for {selected}. Specify a database name (or a persistent SQLite file) and check the setting names.");
        }
        return new(selected, connectionString);
    }
}
