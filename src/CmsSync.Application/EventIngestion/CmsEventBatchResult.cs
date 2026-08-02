namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventBatchResult
{
    internal CmsEventBatchResult(Guid batchId, CmsEventBatchItemResult[] results)
    {
        BatchId = batchId;
        Results = Array.AsReadOnly((CmsEventBatchItemResult[])results.Clone());
        Summary = new CmsEventBatchSummary(Results);
    }

    public Guid BatchId { get; }

    public IReadOnlyList<CmsEventBatchItemResult> Results { get; }

    public CmsEventBatchSummary Summary { get; }
}
