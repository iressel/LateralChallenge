using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Processing;

public sealed record UpsertActiveEntityOperation : CmsEntityStateOperation
{
    internal UpsertActiveEntityOperation(ActiveCmsEntitySnapshot entity)
    {
        Entity = entity;
    }

    public ActiveCmsEntitySnapshot Entity { get; }
}
