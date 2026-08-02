using CmsSync.Domain.Processing;

namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventBatchItemResult
{
    internal CmsEventBatchItemResult(EventTransactionResult transactionResult)
    {
        Sequence = transactionResult.Sequence;
        EventId = transactionResult.EventId;
        EntityId = transactionResult.EntityId;
        Outcome = transactionResult.Outcome;
        Code = transactionResult.Code;
        Generation = transactionResult.Generation;
        ResultingVersion = transactionResult.ResultingVersion;
    }

    public int Sequence { get; }

    public string? EventId { get; }

    public string? EntityId { get; }

    public ProcessingOutcome Outcome { get; }

    public string Code { get; }

    public long? Generation { get; }

    public long? ResultingVersion { get; }
}
