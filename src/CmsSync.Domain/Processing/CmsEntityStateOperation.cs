using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Processing;

public abstract record CmsEntityStateOperation
{
    private protected CmsEntityStateOperation()
    {
    }
}

public sealed record UpsertActiveEntityOperation : CmsEntityStateOperation
{
    internal UpsertActiveEntityOperation(ActiveCmsEntitySnapshot entity)
    {
        Entity = entity;
    }

    public ActiveCmsEntitySnapshot Entity { get; }
}

public sealed record InsertRevisionOperation : CmsEntityStateOperation
{
    internal InsertRevisionOperation(CmsEntityRevisionSnapshot revision)
    {
        Revision = revision;
    }

    public CmsEntityRevisionSnapshot Revision { get; }
}

public sealed record DeleteActiveEntityOperation : CmsEntityStateOperation
{
    internal DeleteActiveEntityOperation(string entityId)
    {
        EntityId = entityId;
    }

    public string EntityId { get; }
}

public sealed record DeleteAllRevisionsOperation : CmsEntityStateOperation
{
    internal DeleteAllRevisionsOperation(string entityId)
    {
        EntityId = entityId;
    }

    public string EntityId { get; }
}

public sealed record UpsertDeletionTombstoneOperation : CmsEntityStateOperation
{
    internal UpsertDeletionTombstoneOperation(CmsDeletionTombstoneSnapshot tombstone)
    {
        Tombstone = tombstone;
    }

    public CmsDeletionTombstoneSnapshot Tombstone { get; }
}
