using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Infrastructure.Persistence.EventProcessing;

internal sealed class EventProcessingCandidate
{
    public EventProcessingCandidate(EventValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        Sequence = validationResult.Sequence;

        if (validationResult.IsValid)
        {
            var validatedEvent = validationResult.ValidatedEvent
                ?? throw new InvalidOperationException("A valid event result has no validated event data.");

            ValidatedEvent = validatedEvent;
            ExternalEventId = validatedEvent.EventId;
            EventType = validatedEvent.CanonicalEventType;
            EntityId = validatedEvent.EntityId;
            Version = validatedEvent.Version;
            OccurredAtUtc = validatedEvent.OccurredAtUtc;
            PayloadHash = validatedEvent.PayloadHash;
            IdempotencyKey = validatedEvent.IdempotencyKey;
            EventContentHash = validatedEvent.EventContentHash;
            return;
        }

        Failure = validationResult.Failure
            ?? throw new InvalidOperationException("An invalid event result has no validation failure.");

        if (validationResult.InvalidIdentityData is null)
        {
            return;
        }

        var invalidIdentity = validationResult.InvalidIdentityData;
        ExternalEventId = invalidIdentity.EventId;
        EventType = invalidIdentity.CanonicalEventType;
        EntityId = invalidIdentity.EntityId;
        Version = invalidIdentity.Version;
        OccurredAtUtc = invalidIdentity.OccurredAtUtc;
        PayloadHash = invalidIdentity.PayloadHash;
        IdempotencyKey = invalidIdentity.IdempotencyKey;
        EventContentHash = invalidIdentity.EventContentHash;
    }

    public int Sequence { get; }

    public bool IsValid => ValidatedEvent is not null;

    public ValidatedCmsEventData? ValidatedEvent { get; }

    public EventValidationFailure? Failure { get; }

    public string? ExternalEventId { get; }

    public string? EventType { get; }

    public string? EntityId { get; }

    public EntityVersion? Version { get; }

    public UtcTimestamp? OccurredAtUtc { get; }

    public PayloadHash? PayloadHash { get; }

    public string? IdempotencyKey { get; }

    public EventContentHash? EventContentHash { get; }
}
