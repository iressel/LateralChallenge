namespace CmsSync.Infrastructure.Persistence.Models;

public sealed class CmsDeletionTombstone
{
    public string EntityId { get; set; } = null!;

    public long LastDeletedGeneration { get; set; }

    public DateTime DeletedAtUtc { get; set; }

    public string? LastDeleteEventKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;
}
