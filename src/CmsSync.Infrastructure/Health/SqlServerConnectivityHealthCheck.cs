using CmsSync.Application.Observability;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CmsSync.Infrastructure.Health;

public sealed class SqlServerConnectivityHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly string _connectionString;
    private readonly string _operation;

    public SqlServerConnectivityHealthCheck(
        string connectionString,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = (int)ProbeTimeout.TotalSeconds,
        };
        _connectionString = connectionStringBuilder.ConnectionString;
        _operation = operation;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ProbeTimeout);

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(timeoutSource.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = (int)ProbeTimeout.TotalSeconds;
            _ = await command.ExecuteScalarAsync(timeoutSource.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            RecordFailure("timeout");
            return HealthCheckResult.Unhealthy("Required database connectivity is unavailable.");
        }
        catch (SqlException)
        {
            RecordFailure("dependency_unavailable");
            return HealthCheckResult.Unhealthy("Required database connectivity is unavailable.");
        }
        catch (InvalidOperationException)
        {
            RecordFailure("configuration_invalid");
            return HealthCheckResult.Unhealthy("Required database connectivity is unavailable.");
        }
    }

    private void RecordFailure(string resultClass)
    {
        CmsOperationalMetrics.RecordSqlFailure(
            _operation,
            resultClass,
            isTransient: false,
            isDeadlock: false);
    }
}
