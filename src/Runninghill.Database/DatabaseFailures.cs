using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace Runninghill.Database;

internal static class DatabaseFailures
{
    // Constraint codes, not message parsing: messages vary with language and server version.
    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                or SqlException { Number: 2601 or 2627 }
                or SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                or MySqlException { Number: 1062 }) return true;
        return false;
    }
}
