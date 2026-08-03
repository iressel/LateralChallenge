using Microsoft.Data.SqlClient;

namespace CmsSync.Infrastructure.Observability;

internal static class SqlServerFailureClassifier
{
    private const int DeadlockErrorNumber = 1205;

    private static readonly HashSet<int> RecognizedTransientSqlErrors =
    [
        -2,
        20,
        64,
        233,
        DeadlockErrorNumber,
        4060,
        10053,
        10054,
        10060,
        10928,
        10929,
        40197,
        40501,
        40613,
        49918,
        49919,
        49920,
    ];

    public static bool IsTransient(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Errors
            .Cast<SqlError>()
            .Any(error => RecognizedTransientSqlErrors.Contains(error.Number));
    }

    public static bool IsDeadlock(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Errors
            .Cast<SqlError>()
            .Any(error => error.Number == DeadlockErrorNumber);
    }
}
