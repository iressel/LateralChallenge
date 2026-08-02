namespace CmsSync.Domain.Processing;

public static class ProcessingCodes
{
    public static ProcessingCode EntityCreated { get; } = new("ENTITY_CREATED");

    public static ProcessingCode EntityRecreated { get; } = new("ENTITY_RECREATED");

    public static ProcessingCode VersionAdvanced { get; } = new("VERSION_ADVANCED");

    public static ProcessingCode SameVersionApplied { get; } = new("SAME_VERSION_APPLIED");

    public static ProcessingCode VersionStale { get; } = new("VERSION_STALE");

    public static ProcessingCode EventTimestampStale { get; } = new("EVENT_TIMESTAMP_STALE");

    public static ProcessingCode StateEquivalent { get; } = new("STATE_EQUIVALENT");

    public static ProcessingCode PayloadConflict { get; } = new("PAYLOAD_CONFLICT");

    public static ProcessingCode PublicationStatusConflict { get; } = new("PUBLICATION_STATUS_CONFLICT");

    public static ProcessingCode TombstoneBlocked { get; } = new("TOMBSTONE_BLOCKED");

    public static ProcessingCode TombstoneCreated { get; } = new("TOMBSTONE_CREATED");

    public static ProcessingCode TombstoneStale { get; } = new("TOMBSTONE_STALE");

    public static ProcessingCode TombstoneEquivalent { get; } = new("TOMBSTONE_EQUIVALENT");

    public static ProcessingCode TombstoneAdvanced { get; } = new("TOMBSTONE_ADVANCED");

    public static ProcessingCode DeleteStale { get; } = new("DELETE_STALE");

    public static ProcessingCode DeleteConflict { get; } = new("DELETE_CONFLICT");

    public static ProcessingCode EntityDeleted { get; } = new("ENTITY_DELETED");

    public static ProcessingCode GenerationExhausted { get; } = new("GENERATION_EXHAUSTED");
}
