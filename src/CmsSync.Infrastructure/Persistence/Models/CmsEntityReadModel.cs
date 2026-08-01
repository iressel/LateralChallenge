namespace CmsSync.Infrastructure.Persistence.Models;

public sealed class CmsEntityReadModel
{
    public string EntityId { get; set; } = null!;

    public long Generation { get; set; }

    public long LatestVersion { get; set; }

    public string Payload { get; set; } = null!;

    public string CmsPublicationStatus { get; set; } = null!;

    public DateTime CurrentVersionOccurredAtUtc { get; set; }

    public DateTime EntityEventHighWatermarkUtc { get; set; }

    public bool AdministrativeDisabled { get; set; }

    public DateTime? AdministrativeStateChangedAtUtc { get; set; }

    public string? AdministrativeStateChangedBy { get; set; }
}
