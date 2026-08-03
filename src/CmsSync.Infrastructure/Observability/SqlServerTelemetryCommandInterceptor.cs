using System.Data.Common;
using CmsSync.Application.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CmsSync.Infrastructure.Observability;

public sealed class SqlServerTelemetryCommandInterceptor : DbCommandInterceptor
{
    private readonly string _operation;

    public SqlServerTelemetryCommandInterceptor(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _operation = operation;
    }

    public override void CommandFailed(
        DbCommand command,
        CommandErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        RecordFailure(eventData.Exception);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        RecordFailure(eventData.Exception);
        return Task.CompletedTask;
    }

    private void RecordFailure(Exception exception)
    {
        if (exception is SqlException sqlException)
        {
            var isTransient = SqlServerFailureClassifier.IsTransient(sqlException);
            CmsOperationalMetrics.RecordSqlFailure(
                _operation,
                isTransient ? "transient" : "permanent",
                isTransient,
                SqlServerFailureClassifier.IsDeadlock(sqlException));
            return;
        }

        CmsOperationalMetrics.RecordSqlFailure(
            _operation,
            "provider",
            isTransient: false,
            isDeadlock: false);
    }
}
