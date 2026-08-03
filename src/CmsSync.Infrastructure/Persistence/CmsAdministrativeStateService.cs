using System.Data;
using CmsSync.Application.AdministrativeState;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CmsSync.Infrastructure.Persistence;

public sealed class CmsAdministrativeStateService : IAdministrativeStateService
{
    private const int MaximumTransactionAttempts = 3;

    private readonly DbContextOptions<CmsWriteDbContext> _contextOptions;
    private readonly SqlServerEntityApplicationLock _applicationLock;
    private readonly TimeProvider _timeProvider;

    public CmsAdministrativeStateService(
        DbContextOptions<CmsWriteDbContext> contextOptions,
        SqlServerEntityApplicationLock applicationLock,
        TimeProvider timeProvider)
    {
        _contextOptions = contextOptions;
        _applicationLock = applicationLock;
        _timeProvider = timeProvider;
    }

    public async Task<AdministrativeStateResult?> SetAsync(
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(administratorSubject);
        cancellationToken.ThrowIfCancellationRequested();

        for (var attempt = 1; attempt <= MaximumTransactionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await ExecuteWithStrategyAsync(
                    entityId,
                    disabled,
                    administratorSubject,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaximumTransactionAttempts)
            {
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new AdministrativeStateDependencyUnavailableException();
            }
            catch (EventProcessingDependencyUnavailableException) when (attempt < MaximumTransactionAttempts)
            {
            }
            catch (EventProcessingDependencyUnavailableException)
            {
                throw new AdministrativeStateDependencyUnavailableException();
            }
            catch (RetryLimitExceededException)
            {
                throw new AdministrativeStateDependencyUnavailableException();
            }
        }

        throw new AdministrativeStateDependencyUnavailableException();
    }

    private async Task<AdministrativeStateResult?> ExecuteWithStrategyAsync(
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = new CmsWriteDbContext(_contextOptions);
        var executionStrategy = strategyContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(
            async retryCancellationToken =>
            {
                retryCancellationToken.ThrowIfCancellationRequested();
                await using var context = new CmsWriteDbContext(_contextOptions);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    retryCancellationToken);
                var result = await ExecuteTransactionAttemptAsync(
                    context,
                    transaction,
                    entityId,
                    disabled,
                    administratorSubject,
                    retryCancellationToken);
                await transaction.CommitAsync(retryCancellationToken);
                return result;
            },
            cancellationToken);
    }

    private async Task<AdministrativeStateResult?> ExecuteTransactionAttemptAsync(
        CmsWriteDbContext context,
        IDbContextTransaction transaction,
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken)
    {
        await _applicationLock.AcquireAsync(
            context.Database.GetDbConnection(),
            transaction.GetDbTransaction(),
            entityId,
            cancellationToken);

        var entity = await context.CmsEntities.SingleOrDefaultAsync(
            candidate => candidate.EntityId == entityId,
            cancellationToken);

        if (entity is null)
        {
            return null;
        }

        ValidateAdministrativeAudit(entity.AdministrativeStateChangedAtUtc, entity.AdministrativeStateChangedBy);

        if (entity.AdministrativeDisabled == disabled)
        {
            return CreateResult(entity);
        }

        entity.AdministrativeDisabled = disabled;
        entity.AdministrativeStateChangedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        entity.AdministrativeStateChangedBy = administratorSubject;

        await context.SaveChangesAsync(cancellationToken);
        return CreateResult(entity);
    }

    private static void ValidateAdministrativeAudit(DateTime? changedAtUtc, string? changedBy)
    {
        if ((changedAtUtc is null) != (changedBy is null))
        {
            throw new InvalidOperationException("The persisted administrative audit metadata is inconsistent.");
        }
    }

    private static AdministrativeStateResult CreateResult(CmsEntity entity)
    {
        DateTime? changedAtUtc = entity.AdministrativeStateChangedAtUtc is null
            ? null
            : DateTime.SpecifyKind(entity.AdministrativeStateChangedAtUtc.Value, DateTimeKind.Utc);

        return new AdministrativeStateResult(
            entity.EntityId,
            entity.AdministrativeDisabled,
            changedAtUtc,
            entity.AdministrativeStateChangedBy);
    }
}
