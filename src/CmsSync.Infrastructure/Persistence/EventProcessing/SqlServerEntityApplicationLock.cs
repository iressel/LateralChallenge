using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CmsSync.Application.EventIngestion;

namespace CmsSync.Infrastructure.Persistence.EventProcessing;

public sealed class SqlServerEntityApplicationLock
{
    private const string ResourceNamespace = "CmsSync:Entity:";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly int _lockTimeoutMilliseconds;

    public SqlServerEntityApplicationLock(TimeSpan? lockTimeout = null)
    {
        var selectedTimeout = lockTimeout ?? TimeSpan.FromSeconds(1);

        if (selectedTimeout < TimeSpan.Zero || selectedTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lockTimeout),
                selectedTimeout,
                "The application-lock timeout must fit a non-negative SQL Server integer millisecond value.");
        }

        _lockTimeoutMilliseconds = checked((int)selectedTimeout.TotalMilliseconds);
    }

    public static string CreateResource(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        try
        {
            var entityBytes = StrictUtf8.GetBytes(entityId);
            return ResourceNamespace + Convert.ToHexString(SHA256.HashData(entityBytes));
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException(
                "The entity identifier cannot be encoded for SQL Server serialization.");
        }
    }

    public async Task AcquireAsync(
        DbConnection connection,
        DbTransaction transaction,
        string entityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText =
            "DECLARE @result int; " +
            "EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', " +
            "@LockTimeout = @lockTimeout; " +
            "SELECT @result;";

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Size = ResourceNamespace.Length + (SHA256.HashSizeInBytes * 2);
        resourceParameter.Value = CreateResource(entityId);
        command.Parameters.Add(resourceParameter);

        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@lockTimeout";
        timeoutParameter.DbType = DbType.Int32;
        timeoutParameter.Value = _lockTimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        var returnCode = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        HandleReturnCode(returnCode, cancellationToken);
    }

    private static void HandleReturnCode(int returnCode, CancellationToken cancellationToken)
    {
        if (returnCode >= 0)
        {
            return;
        }

        switch (returnCode)
        {
            case -1:
            case -3:
                throw new EventProcessingDependencyUnavailableException();
            case -2:
                throw new OperationCanceledException(
                    "SQL Server canceled the entity application-lock request.",
                    cancellationToken);
            case -999:
                throw new InvalidOperationException("SQL Server rejected the entity application-lock invocation.");
            default:
                throw new InvalidOperationException("SQL Server returned an unexpected entity application-lock result.");
        }
    }
}
