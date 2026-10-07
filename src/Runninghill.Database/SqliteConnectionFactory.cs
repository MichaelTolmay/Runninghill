using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Runninghill.Database;

/// <summary>Creates independent connections; callers own and dispose the returned connection.</summary>
public sealed class SqliteConnectionFactory(string connectionString) : IDatabaseConnectionFactory
{
    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
