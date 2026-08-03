using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CmsSync.Application.EventIngestion;
using CmsSync.Application.Observability;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;
using CmsSync.Domain.Processing;
using CmsSync.Infrastructure.Observability;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace CmsSync.Infrastructure.Persistence;

public sealed class SqlServerEventTransactionExecutor : IEventTransactionExecutor
{
    private const int MaximumTransactionAttempts = 3;

    private static readonly Func<ILogger, Guid, string, string, string, string, IDisposable?> EventLogScope =
        LoggerMessage.DefineScope<Guid, string, string, string, string>(
            "BatchId {BatchId} EventType {EventType} EntityIdHash {EntityIdHash} " +
            "CorrelationId {CorrelationId} TraceId {TraceId}");
    private static readonly Action<ILogger, int, string, string, double, Exception?> EventCompletedLog =
        LoggerMessage.Define<int, string, string, double>(
            LogLevel.Information,
            new EventId(1401, nameof(EventCompletedLog)),
            "CMS event processing completed. Sequence {Sequence} Outcome {Outcome} Code {Code} " +
            "ElapsedMilliseconds {ElapsedMilliseconds}");
    private static readonly Action<ILogger, int, string, double, Exception?> EventFailedLog =
        LoggerMessage.Define<int, string, double>(
            LogLevel.Error,
            new EventId(1402, nameof(EventFailedLog)),
            "CMS event processing failed. Sequence {Sequence} ResultClass {ResultClass} " +
            "ElapsedMilliseconds {ElapsedMilliseconds}");

    private readonly DbContextOptions<CmsWriteDbContext> _contextOptions;
    private readonly EventValidator _validator;
    private readonly SqlServerEntityApplicationLock _applicationLock;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlServerEventTransactionExecutor>? _logger;

    public SqlServerEventTransactionExecutor(
        DbContextOptions<CmsWriteDbContext> contextOptions,
        EventValidator validator,
        SqlServerEntityApplicationLock applicationLock,
        TimeProvider timeProvider,
        ILogger<SqlServerEventTransactionExecutor>? logger = null)
    {
        _contextOptions = contextOptions;
        _validator = validator;
        _applicationLock = applicationLock;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var candidate = new EventProcessingCandidate(_validator.Validate(request.Item));
        var startedTimestamp = Stopwatch.GetTimestamp();

        try
        {
            for (var attempt = 1; attempt <= MaximumTransactionAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await ExecuteWithStrategyAsync(request, candidate, cancellationToken);
                    RecordCompleted(request, candidate, result, startedTimestamp);
                    return result;
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
                    throw new EventProcessingDependencyUnavailableException();
                }
                catch (EventProcessingDependencyUnavailableException) when (attempt < MaximumTransactionAttempts)
                {
                }
                catch (EventProcessingDependencyUnavailableException)
                {
                    throw;
                }
                catch (RetryLimitExceededException)
                {
                    throw new EventProcessingDependencyUnavailableException();
                }
                catch (SqlException exception) when (
                    SqlServerFailureClassifier.IsTransient(exception) && attempt < MaximumTransactionAttempts)
                {
                }
                catch (SqlException exception) when (SqlServerFailureClassifier.IsTransient(exception))
                {
                    throw new EventProcessingDependencyUnavailableException();
                }
                catch (DbUpdateException exception) when (
                    IsExpectedRetryableUniqueIndexRace(exception) && attempt < MaximumTransactionAttempts)
                {
                }
                catch (DbUpdateException exception) when (IsExpectedRetryableUniqueIndexRace(exception))
                {
                    throw new EventProcessingDependencyUnavailableException();
                }
            }

            throw new EventProcessingDependencyUnavailableException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EventProcessingDependencyUnavailableException)
        {
            RecordFailed(request, candidate, startedTimestamp, "dependency_unavailable");
            throw;
        }
        catch (Exception)
        {
            RecordFailed(request, candidate, startedTimestamp, "unexpected_failure");
            throw;
        }
    }

    private async Task<EventTransactionResult> ExecuteWithStrategyAsync(
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
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
                    request,
                    candidate,
                    retryCancellationToken);
                await transaction.CommitAsync(retryCancellationToken);
                return result;
            },
            cancellationToken);
    }

    private async Task<EventTransactionResult> ExecuteTransactionAttemptAsync(
        CmsWriteDbContext context,
        IDbContextTransaction transaction,
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        CancellationToken cancellationToken)
    {
        var completedPosition = await context.CmsEventProcessingLogs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                log => log.BatchId == request.BatchId && log.Sequence == candidate.Sequence,
                cancellationToken);

        if (completedPosition is not null)
        {
            ValidateBatchPositionCompatibility(completedPosition, request, candidate);
            return CreateResult(completedPosition);
        }

        var identityOwner = await FindIdentityOwnerAsync(context, candidate, cancellationToken);

        if (identityOwner is not null)
        {
            return await PersistReplayAsync(
                context,
                request,
                candidate,
                identityOwner,
                cancellationToken);
        }

        if (!candidate.IsValid)
        {
            return await PersistInvalidAsync(context, request, candidate, cancellationToken);
        }

        var entityId = candidate.EntityId
            ?? throw new InvalidOperationException("A validated event has no entity identifier.");
        await _applicationLock.AcquireAsync(
            context.Database.GetDbConnection(),
            transaction.GetDbTransaction(),
            entityId,
            cancellationToken);

        identityOwner = await FindIdentityOwnerAsync(context, candidate, cancellationToken);

        if (identityOwner is not null)
        {
            return await PersistReplayAsync(
                context,
                request,
                candidate,
                identityOwner,
                cancellationToken);
        }

        var (activeModel, activeSnapshot) = await LoadActiveEntityAsync(
            context,
            entityId,
            cancellationToken);
        var (tombstoneModel, tombstoneSnapshot) = await LoadTombstoneAsync(
            context,
            entityId,
            cancellationToken);
        var revisionSnapshot = await LoadSameVersionRevisionAsync(
            context,
            candidate,
            activeSnapshot,
            cancellationToken);
        var domainEvent = candidate.ValidatedEvent?.DomainEvent
            ?? throw new InvalidOperationException("A validated event has no domain event.");
        var decision = CmsEntityStateMachine.Decide(
            domainEvent,
            activeSnapshot,
            tombstoneSnapshot,
            revisionSnapshot);
        var processedAtUtc = GetUtcNow();

        await ApplyOperationsAsync(
            context,
            candidate,
            decision,
            activeModel,
            tombstoneModel,
            processedAtUtc,
            cancellationToken);

        var (generation, resultingVersion) = ResolveDecisionMetadata(
            decision,
            activeSnapshot,
            tombstoneSnapshot);
        var processingLog = CreateProcessingLog(
            request,
            candidate,
            decision.Outcome,
            decision.Code.Value,
            generation,
            resultingVersion,
            ownsIdentity: true,
            replayOfProcessingLogId: null,
            processedAtUtc);
        context.CmsEventProcessingLogs.Add(processingLog);
        await context.SaveChangesAsync(cancellationToken);

        return CreateResult(processingLog);
    }

    private static async Task<CmsEventProcessingLog?> FindIdentityOwnerAsync(
        CmsWriteDbContext context,
        EventProcessingCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.IdempotencyKey is null)
        {
            return null;
        }

        return await context.CmsEventProcessingLogs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                log => log.OwnsIdempotencyKey && log.IdempotencyKey == candidate.IdempotencyKey,
                cancellationToken);
    }

    private async Task<EventTransactionResult> PersistReplayAsync(
        CmsWriteDbContext context,
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        CmsEventProcessingLog identityOwner,
        CancellationToken cancellationToken)
    {
        var candidateContentHash = candidate.EventContentHash?.ToArray();

        if (candidateContentHash is null ||
            candidateContentHash.Length != EventContentHash.Length ||
            identityOwner.EventContentHash is null ||
            identityOwner.EventContentHash.Length != EventContentHash.Length)
        {
            throw new InvalidOperationException(
                "The idempotency owner has incomplete normalized event content metadata.");
        }

        if (candidate.ExternalEventId is not null &&
            !string.Equals(
                identityOwner.ExternalEventId,
                candidate.ExternalEventId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The external idempotency owner has inconsistent event identity metadata.");
        }

        var contentMatches = FixedHashEquals(candidateContentHash, identityOwner.EventContentHash);
        ProcessingOutcome outcome;
        string code;
        long? generation;
        long? resultingVersion;

        if (!contentMatches)
        {
            if (candidate.ExternalEventId is null)
            {
                throw new InvalidOperationException(
                    "A derived idempotency owner has inconsistent normalized event content.");
            }

            outcome = ProcessingOutcome.Conflict;
            code = EventProcessingCodes.EventIdContentConflict;
            generation = null;
            resultingVersion = null;
        }
        else
        {
            var originalOutcome = ParseOutcome(identityOwner.Outcome);

            if (originalOutcome is ProcessingOutcome.Invalid or ProcessingOutcome.Conflict)
            {
                outcome = originalOutcome;
                code = identityOwner.Code;
            }
            else
            {
                outcome = ProcessingOutcome.Duplicate;
                code = EventProcessingCodes.ExactDuplicate;
            }

            generation = identityOwner.Generation;
            resultingVersion = identityOwner.ResultingVersion;
        }

        var replayLog = CreateProcessingLog(
            request,
            candidate,
            outcome,
            code,
            generation,
            resultingVersion,
            ownsIdentity: false,
            identityOwner.ProcessingLogId,
            GetUtcNow());
        context.CmsEventProcessingLogs.Add(replayLog);
        await context.SaveChangesAsync(cancellationToken);

        return CreateResult(replayLog);
    }

    private async Task<EventTransactionResult> PersistInvalidAsync(
        CmsWriteDbContext context,
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        CancellationToken cancellationToken)
    {
        var failure = candidate.Failure
            ?? throw new InvalidOperationException("An invalid event has no validation failure.");
        var processingLog = CreateProcessingLog(
            request,
            candidate,
            ProcessingOutcome.Invalid,
            failure.Code,
            generation: null,
            resultingVersion: null,
            ownsIdentity: candidate.IdempotencyKey is not null,
            replayOfProcessingLogId: null,
            GetUtcNow());
        context.CmsEventProcessingLogs.Add(processingLog);
        await context.SaveChangesAsync(cancellationToken);

        return CreateResult(processingLog);
    }

    private static async Task<(CmsEntity? Model, ActiveCmsEntitySnapshot? Snapshot)> LoadActiveEntityAsync(
        CmsWriteDbContext context,
        string entityId,
        CancellationToken cancellationToken)
    {
        var model = await context.CmsEntities.SingleOrDefaultAsync(
            entity => entity.EntityId == entityId,
            cancellationToken);

        if (model is null)
        {
            return (null, null);
        }

        var status = model.CmsPublicationStatus switch
        {
            "Published" => CmsPublicationStatus.Published,
            "Unpublished" => CmsPublicationStatus.Unpublished,
            _ => throw new InvalidOperationException("The persisted CMS publication status is not supported."),
        };
        var snapshot = new ActiveCmsEntitySnapshot(
            model.EntityId,
            new EntityGeneration(model.Generation),
            new EntityVersion(model.LatestVersion),
            model.Payload,
            new PayloadHash(model.PayloadHash),
            status,
            ToUtcTimestamp(model.CurrentVersionOccurredAtUtc),
            ToUtcTimestamp(model.EntityEventHighWatermarkUtc),
            model.AdministrativeDisabled);

        return (model, snapshot);
    }

    private static async Task<(CmsDeletionTombstone? Model, CmsDeletionTombstoneSnapshot? Snapshot)> LoadTombstoneAsync(
        CmsWriteDbContext context,
        string entityId,
        CancellationToken cancellationToken)
    {
        var model = await context.CmsDeletionTombstones.SingleOrDefaultAsync(
            tombstone => tombstone.EntityId == entityId,
            cancellationToken);

        if (model is null)
        {
            return (null, null);
        }

        var snapshot = new CmsDeletionTombstoneSnapshot(
            model.EntityId,
            new EntityGeneration(model.LastDeletedGeneration),
            ToUtcTimestamp(model.DeletedAtUtc));

        return (model, snapshot);
    }

    private static async Task<CmsEntityRevisionSnapshot?> LoadSameVersionRevisionAsync(
        CmsWriteDbContext context,
        EventProcessingCandidate candidate,
        ActiveCmsEntitySnapshot? activeSnapshot,
        CancellationToken cancellationToken)
    {
        if (activeSnapshot is null ||
            candidate.Version is null ||
            candidate.Version.Value != activeSnapshot.LatestVersion)
        {
            return null;
        }

        var revision = await context.CmsEntityRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                storedRevision =>
                    storedRevision.EntityId == activeSnapshot.EntityId &&
                    storedRevision.Generation == activeSnapshot.Generation.Value &&
                    storedRevision.Version == candidate.Version.Value.Value,
                cancellationToken);

        if (revision is null)
        {
            return null;
        }

        return new CmsEntityRevisionSnapshot(
            revision.EntityId,
            new EntityGeneration(revision.Generation),
            new EntityVersion(revision.Version),
            revision.FirstObservedPayload,
            new PayloadHash(revision.PayloadHash),
            ToUtcTimestamp(revision.FirstObservedAtUtc));
    }

    private static async Task ApplyOperationsAsync(
        CmsWriteDbContext context,
        EventProcessingCandidate candidate,
        ProcessingDecision decision,
        CmsEntity? activeModel,
        CmsDeletionTombstone? tombstoneModel,
        DateTime processedAtUtc,
        CancellationToken cancellationToken)
    {
        foreach (var operation in decision.Operations)
        {
            switch (operation)
            {
                case UpsertActiveEntityOperation upsertEntity:
                    UpsertActiveEntity(context, activeModel, upsertEntity.Entity, processedAtUtc);
                    break;
                case InsertRevisionOperation insertRevision:
                    InsertRevision(context, insertRevision.Revision);
                    break;
                case DeleteAllRevisionsOperation deleteRevisions:
                    await context.CmsEntityRevisions
                        .Where(revision => revision.EntityId == deleteRevisions.EntityId)
                        .ExecuteDeleteAsync(cancellationToken);
                    break;
                case DeleteActiveEntityOperation deleteEntity:
                    DeleteActiveEntity(context, activeModel, deleteEntity.EntityId);
                    break;
                case UpsertDeletionTombstoneOperation upsertTombstone:
                    UpsertTombstone(
                        context,
                        tombstoneModel,
                        upsertTombstone.Tombstone,
                        candidate.IdempotencyKey,
                        processedAtUtc);
                    break;
                default:
                    throw new InvalidOperationException("The state machine returned an unsupported persistence operation.");
            }
        }
    }

    private static void UpsertActiveEntity(
        CmsWriteDbContext context,
        CmsEntity? model,
        ActiveCmsEntitySnapshot snapshot,
        DateTime processedAtUtc)
    {
        if (model is null)
        {
            context.CmsEntities.Add(new CmsEntity
            {
                EntityId = snapshot.EntityId,
                Generation = snapshot.Generation.Value,
                LatestVersion = snapshot.LatestVersion.Value,
                Payload = snapshot.Payload,
                PayloadHash = snapshot.PayloadHash.ToArray(),
                CmsPublicationStatus = snapshot.PublicationStatus.ToString(),
                CurrentVersionOccurredAtUtc = snapshot.CurrentVersionOccurredAtUtc.Value.UtcDateTime,
                EntityEventHighWatermarkUtc = snapshot.EntityEventHighWatermarkUtc.Value.UtcDateTime,
                AdministrativeDisabled = snapshot.AdministrativeDisabled,
                CreatedAtUtc = processedAtUtc,
                UpdatedAtUtc = processedAtUtc,
            });
            return;
        }

        if (!string.Equals(model.EntityId, snapshot.EntityId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The active entity operation does not match the loaded entity.");
        }

        model.Generation = snapshot.Generation.Value;
        model.LatestVersion = snapshot.LatestVersion.Value;
        model.Payload = snapshot.Payload;
        model.PayloadHash = snapshot.PayloadHash.ToArray();
        model.CmsPublicationStatus = snapshot.PublicationStatus.ToString();
        model.CurrentVersionOccurredAtUtc = snapshot.CurrentVersionOccurredAtUtc.Value.UtcDateTime;
        model.EntityEventHighWatermarkUtc = snapshot.EntityEventHighWatermarkUtc.Value.UtcDateTime;
        model.AdministrativeDisabled = snapshot.AdministrativeDisabled;
        model.UpdatedAtUtc = processedAtUtc;
    }

    private static void InsertRevision(CmsWriteDbContext context, CmsEntityRevisionSnapshot snapshot)
    {
        context.CmsEntityRevisions.Add(new CmsEntityRevision
        {
            EntityId = snapshot.EntityId,
            Generation = snapshot.Generation.Value,
            Version = snapshot.Version.Value,
            FirstObservedPayload = snapshot.FirstObservedPayload,
            PayloadHash = snapshot.PayloadHash.ToArray(),
            FirstObservedAtUtc = snapshot.FirstObservedAtUtc.Value.UtcDateTime,
        });
    }

    private static void DeleteActiveEntity(
        CmsWriteDbContext context,
        CmsEntity? activeModel,
        string entityId)
    {
        if (activeModel is null || !string.Equals(activeModel.EntityId, entityId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The active entity delete operation has no matching loaded entity.");
        }

        context.CmsEntities.Remove(activeModel);
    }

    private static void UpsertTombstone(
        CmsWriteDbContext context,
        CmsDeletionTombstone? model,
        CmsDeletionTombstoneSnapshot snapshot,
        string? lastDeleteEventKey,
        DateTime processedAtUtc)
    {
        if (model is null)
        {
            context.CmsDeletionTombstones.Add(new CmsDeletionTombstone
            {
                EntityId = snapshot.EntityId,
                LastDeletedGeneration = snapshot.LastDeletedGeneration.Value,
                DeletedAtUtc = snapshot.DeletedAtUtc.Value.UtcDateTime,
                LastDeleteEventKey = lastDeleteEventKey,
                CreatedAtUtc = processedAtUtc,
                UpdatedAtUtc = processedAtUtc,
            });
            return;
        }

        if (!string.Equals(model.EntityId, snapshot.EntityId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The tombstone operation does not match the loaded tombstone.");
        }

        model.LastDeletedGeneration = snapshot.LastDeletedGeneration.Value;
        model.DeletedAtUtc = snapshot.DeletedAtUtc.Value.UtcDateTime;
        model.LastDeleteEventKey = lastDeleteEventKey;
        model.UpdatedAtUtc = processedAtUtc;
    }

    private static (long? Generation, long? ResultingVersion) ResolveDecisionMetadata(
        ProcessingDecision decision,
        ActiveCmsEntitySnapshot? activeSnapshot,
        CmsDeletionTombstoneSnapshot? tombstoneSnapshot)
    {
        var entityOperation = decision.Operations.OfType<UpsertActiveEntityOperation>().SingleOrDefault();

        if (entityOperation is not null)
        {
            return (
                entityOperation.Entity.Generation.Value,
                entityOperation.Entity.LatestVersion.Value);
        }

        var tombstoneOperation = decision.Operations
            .OfType<UpsertDeletionTombstoneOperation>()
            .SingleOrDefault();

        if (tombstoneOperation is not null)
        {
            return (tombstoneOperation.Tombstone.LastDeletedGeneration.Value, null);
        }

        if (activeSnapshot is not null)
        {
            return (activeSnapshot.Generation.Value, activeSnapshot.LatestVersion.Value);
        }

        return (tombstoneSnapshot?.LastDeletedGeneration.Value, null);
    }

    private static CmsEventProcessingLog CreateProcessingLog(
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        ProcessingOutcome outcome,
        string code,
        long? generation,
        long? resultingVersion,
        bool ownsIdentity,
        long? replayOfProcessingLogId,
        DateTime processedAtUtc)
    {
        return new CmsEventProcessingLog
        {
            BatchId = request.BatchId,
            Sequence = candidate.Sequence,
            IdempotencyKey = candidate.IdempotencyKey,
            OwnsIdempotencyKey = ownsIdentity,
            ReplayOfProcessingLogId = replayOfProcessingLogId,
            ExternalEventId = candidate.ExternalEventId,
            EventContentHash = candidate.EventContentHash?.ToArray(),
            PayloadHash = candidate.PayloadHash?.ToArray(),
            EventType = candidate.EventType,
            EntityId = candidate.EntityId,
            Version = candidate.Version?.Value,
            EventOccurredAtUtc = candidate.OccurredAtUtc?.Value.UtcDateTime,
            Outcome = outcome.ToString(),
            Code = code,
            Generation = generation,
            ResultingVersion = resultingVersion,
            ProcessedAtUtc = processedAtUtc,
            CorrelationId = request.CorrelationId,
            AuthenticatedCmsSubject = request.AuthenticatedCmsSubject,
        };
    }

    private static EventTransactionResult CreateResult(CmsEventProcessingLog processingLog)
    {
        return new EventTransactionResult(
            processingLog.Sequence,
            processingLog.ExternalEventId,
            processingLog.EntityId,
            ParseOutcome(processingLog.Outcome),
            processingLog.Code,
            processingLog.Generation,
            processingLog.ResultingVersion);
    }

    private static ProcessingOutcome ParseOutcome(string value)
    {
        if (!Enum.TryParse<ProcessingOutcome>(value, ignoreCase: false, out var outcome) ||
            !Enum.IsDefined(outcome))
        {
            throw new InvalidOperationException("The persisted processing outcome is not supported.");
        }

        return outcome;
    }

    private static void ValidateBatchPositionCompatibility(
        CmsEventProcessingLog completedPosition,
        EventTransactionRequest request,
        EventProcessingCandidate candidate)
    {
        var compatible = completedPosition.BatchId == request.BatchId &&
            completedPosition.Sequence == candidate.Sequence &&
            string.Equals(completedPosition.CorrelationId, request.CorrelationId, StringComparison.Ordinal) &&
            string.Equals(
                completedPosition.AuthenticatedCmsSubject,
                request.AuthenticatedCmsSubject,
                StringComparison.Ordinal) &&
            string.Equals(completedPosition.IdempotencyKey, candidate.IdempotencyKey, StringComparison.Ordinal) &&
            string.Equals(completedPosition.ExternalEventId, candidate.ExternalEventId, StringComparison.Ordinal) &&
            string.Equals(completedPosition.EventType, candidate.EventType, StringComparison.Ordinal) &&
            string.Equals(completedPosition.EntityId, candidate.EntityId, StringComparison.Ordinal) &&
            completedPosition.Version == candidate.Version?.Value &&
            completedPosition.EventOccurredAtUtc == candidate.OccurredAtUtc?.Value.UtcDateTime &&
            FixedHashEquals(completedPosition.EventContentHash, candidate.EventContentHash?.ToArray()) &&
            FixedHashEquals(completedPosition.PayloadHash, candidate.PayloadHash?.ToArray());

        if (!candidate.IsValid)
        {
            compatible = compatible &&
                string.Equals(completedPosition.Outcome, ProcessingOutcome.Invalid.ToString(), StringComparison.Ordinal) &&
                string.Equals(completedPosition.Code, candidate.Failure?.Code, StringComparison.Ordinal);
        }

        if (!compatible)
        {
            throw new InvalidOperationException(
                "The completed batch position is inconsistent with the current event request.");
        }
    }

    private static bool FixedHashEquals(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Length == EventContentHash.Length &&
            right.Length == EventContentHash.Length &&
            CryptographicOperations.FixedTimeEquals(left, right);
    }

    private DateTime GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private static UtcTimestamp ToUtcTimestamp(DateTime value)
    {
        return new UtcTimestamp(
            new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero));
    }

    private static bool IsExpectedRetryableUniqueIndexRace(DbUpdateException exception)
    {
        if (exception.InnerException is not SqlException sqlException ||
            sqlException.Number is not 2601 and not 2627)
        {
            return false;
        }

        return sqlException.Message.Contains(
                   PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner,
                   StringComparison.Ordinal) ||
               sqlException.Message.Contains(
                   PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence,
                   StringComparison.Ordinal);
    }

    private void RecordCompleted(
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        EventTransactionResult result,
        long startedTimestamp)
    {
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        CmsOperationalMetrics.RecordEvent(result.Outcome, result.Code, elapsedMilliseconds);

        if (_logger is null)
        {
            return;
        }

        using var scope = EventLogScope(
            _logger,
            request.BatchId,
            candidate.EventType ?? "unknown",
            CreateEntityIdentifierHash(candidate.EntityId),
            request.CorrelationId,
            ReadTraceIdentifier());
        EventCompletedLog(
            _logger,
            result.Sequence,
            result.Outcome.ToString().ToLowerInvariant(),
            result.Code,
            elapsedMilliseconds,
            null);
    }

    private void RecordFailed(
        EventTransactionRequest request,
        EventProcessingCandidate candidate,
        long startedTimestamp,
        string resultClass)
    {
        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;
        CmsOperationalMetrics.RecordEventFailure(elapsedMilliseconds, resultClass);

        if (_logger is null)
        {
            return;
        }

        using var scope = EventLogScope(
            _logger,
            request.BatchId,
            candidate.EventType ?? "unknown",
            CreateEntityIdentifierHash(candidate.EntityId),
            request.CorrelationId,
            ReadTraceIdentifier());
        EventFailedLog(
            _logger,
            candidate.Sequence,
            resultClass,
            elapsedMilliseconds,
            null);
    }

    private static string CreateEntityIdentifierHash(string? entityId)
    {
        if (entityId is null)
        {
            return "none";
        }

        var identifierBytes = Encoding.UTF8.GetBytes(entityId);

        try
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(identifierBytes, hash);
            return Convert.ToHexString(hash[..8]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identifierBytes);
        }
    }

    private static string ReadTraceIdentifier()
    {
        return Activity.Current?.TraceId.ToString() ?? "none";
    }
}
