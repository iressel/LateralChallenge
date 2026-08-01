using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;

namespace CmsSync.Application.EventIngestion;

public sealed record EventValidationFailure(string Code, string Message);

public sealed class ValidatedCmsEventData
{
    internal ValidatedCmsEventData(
        int sequence,
        string? eventId,
        CmsEventType eventType,
        string entityId,
        EntityVersion? version,
        UtcTimestamp occurredAtUtc,
        string? rawPayload,
        PayloadHash? payloadHash,
        EventIdentity identity,
        ValidatedCmsEvent domainEvent)
    {
        Sequence = sequence;
        EventId = eventId;
        EventType = eventType;
        EntityId = entityId;
        Version = version;
        OccurredAtUtc = occurredAtUtc;
        RawPayload = rawPayload;
        PayloadHash = payloadHash;
        IdempotencyKey = identity.IdempotencyKey;
        EventContentHash = identity.ContentHash;
        DomainEvent = domainEvent;
    }

    public int Sequence { get; }

    public string? EventId { get; }

    public CmsEventType EventType { get; }

    public string CanonicalEventType => CmsEventTypeNames.GetCanonicalName(EventType);

    public string EntityId { get; }

    public EntityVersion? Version { get; }

    public UtcTimestamp OccurredAtUtc { get; }

    public string? RawPayload { get; }

    public PayloadHash? PayloadHash { get; }

    public string IdempotencyKey { get; }

    public EventContentHash EventContentHash { get; }

    public ValidatedCmsEvent DomainEvent { get; }

    public override string ToString() =>
        $"Sequence = {Sequence}, Type = {CanonicalEventType}, EntityId = {EntityId}, HasPayload = {RawPayload is not null}";
}

public sealed class EventValidationResult
{
    private EventValidationResult(
        int sequence,
        ValidatedCmsEventData? validatedEvent,
        EventValidationFailure? failure)
    {
        Sequence = sequence;
        ValidatedEvent = validatedEvent;
        Failure = failure;
    }

    public int Sequence { get; }

    public bool IsValid => ValidatedEvent is not null;

    public ValidatedCmsEventData? ValidatedEvent { get; }

    public EventValidationFailure? Failure { get; }

    internal static EventValidationResult Valid(ValidatedCmsEventData validatedEvent) =>
        new(validatedEvent.Sequence, validatedEvent, null);

    internal static EventValidationResult Invalid(int sequence, string code, string message) =>
        new(sequence, null, new EventValidationFailure(code, message));

    public override string ToString() => IsValid
        ? $"Sequence = {Sequence}, Valid"
        : $"Sequence = {Sequence}, Invalid, Code = {Failure!.Code}";
}
