namespace CmsSync.Application.EventIngestion;

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

    internal static EventValidationResult Valid(ValidatedCmsEventData validatedEvent)
    {
        return new EventValidationResult(validatedEvent.Sequence, validatedEvent, null);
    }

    internal static EventValidationResult Invalid(int sequence, string code, string message)
    {
        return new EventValidationResult(sequence, null, new EventValidationFailure(code, message));
    }

    public override string ToString()
    {
        return IsValid
            ? $"Sequence = {Sequence}, Valid"
            : $"Sequence = {Sequence}, Invalid, Code = {Failure!.Code}";
    }
}
