using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Processing;

public sealed record InsertRevisionOperation : CmsEntityStateOperation
{
    internal InsertRevisionOperation(CmsEntityRevisionSnapshot revision)
    {
        Revision = revision;
    }

    public CmsEntityRevisionSnapshot Revision { get; }
}
