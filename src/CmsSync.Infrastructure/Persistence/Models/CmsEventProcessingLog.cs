namespace CmsSync.Infrastructure.Persistence.Models;

public sealed class CmsEventProcessingLog
{
    public long ProcessingLogId { get; set; }

    public Guid BatchId { get; set; }

    public int Sequence { get; set; }

    public string? IdempotencyKey { get; set; }

    public bool OwnsIdempotencyKey { get; set; }

    public long? ReplayOfProcessingLogId { get; set; }

    public string? ExternalEventId { get; set; }

    public byte[]? EventContentHash { get; set; }

    public byte[]? PayloadHash { get; set; }

    public string? EventType { get; set; }

    public string? EntityId { get; set; }

    public long? Version { get; set; }

    public DateTime? EventOccurredAtUtc { get; set; }

    public string Outcome { get; set; } = null!;

    public string Code { get; set; } = null!;

    public long? Generation { get; set; }

    public long? ResultingVersion { get; set; }

    public DateTime ProcessedAtUtc { get; set; }

    public string CorrelationId { get; set; } = null!;

    public string AuthenticatedCmsSubject { get; set; } = null!;
}
