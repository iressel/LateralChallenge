using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Processing;

public sealed record UpsertDeletionTombstoneOperation : CmsEntityStateOperation
{
    internal UpsertDeletionTombstoneOperation(CmsDeletionTombstoneSnapshot tombstone)
    {
        Tombstone = tombstone;
    }

    public CmsDeletionTombstoneSnapshot Tombstone { get; }
}
