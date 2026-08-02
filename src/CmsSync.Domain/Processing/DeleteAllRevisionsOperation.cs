namespace CmsSync.Domain.Processing;

public sealed record DeleteAllRevisionsOperation : CmsEntityStateOperation
{
    internal DeleteAllRevisionsOperation(string entityId)
    {
        EntityId = entityId;
    }

    public string EntityId { get; }
}
