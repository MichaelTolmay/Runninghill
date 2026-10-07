using System.Data.Common;

namespace Runninghill.Database;

public interface IDatabaseConnectionFactory
{
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
