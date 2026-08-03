using System.Data;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal sealed class AdministrativeConcurrencyFailureInterceptor : SaveChangesInterceptor
{
    private readonly int _failuresToInject;
    private int _saveAttempts;
    private int _injectedFailures;

    public AdministrativeConcurrencyFailureInterceptor(int failuresToInject)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failuresToInject, 1);

        _failuresToInject = failuresToInject;
    }

    public int InjectedFailures => Volatile.Read(ref _injectedFailures);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not DbContext context)
        {
            return result;
        }

        var administrativeEntry = context.ChangeTracker
            .Entries<CmsEntity>()
            .SingleOrDefault(entry =>
                entry.State == EntityState.Modified &&
                entry.Property(entity => entity.AdministrativeDisabled).IsModified);

        if (administrativeEntry is null ||
            Interlocked.Increment(ref _saveAttempts) > _failuresToInject)
        {
            return result;
        }

        var currentTransaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The administrative update has no current transaction.");
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText =
            "UPDATE [CmsEntities] SET [AdministrativeDisabled] = [AdministrativeDisabled] " +
            "WHERE [EntityId] = @entityId";

        var entityIdParameter = command.CreateParameter();
        entityIdParameter.ParameterName = "@entityId";
        entityIdParameter.DbType = DbType.String;
        entityIdParameter.Size = 200;
        entityIdParameter.Value = administrativeEntry.Entity.EntityId;
        command.Parameters.Add(entityIdParameter);

        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException("The concurrency test could not update the target entity.");
        }

        Interlocked.Increment(ref _injectedFailures);
        return result;
    }
}
