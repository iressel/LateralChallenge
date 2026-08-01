namespace CmsSync.Infrastructure.Persistence.Models;

public sealed class CmsEntityRevision
{
    public string EntityId { get; set; } = null!;

    public long Generation { get; set; }

    public long Version { get; set; }

    public string FirstObservedPayload { get; set; } = null!;

    public byte[] PayloadHash { get; set; } = null!;

    public DateTime FirstObservedAtUtc { get; set; }
}
