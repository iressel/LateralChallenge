namespace CmsSync.Domain.Processing;

public sealed record DeleteActiveEntityOperation : CmsEntityStateOperation
{
    internal DeleteActiveEntityOperation(string entityId)
    {
        EntityId = entityId;
    }

    public string EntityId { get; }
}
