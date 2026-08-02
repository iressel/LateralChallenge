using CmsSync.Domain.Processing;

namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventBatchSummary
{
    internal CmsEventBatchSummary(IReadOnlyList<CmsEventBatchItemResult> results)
    {
        foreach (var result in results)
        {
            switch (result.Outcome)
            {
                case ProcessingOutcome.Applied:
                    Applied++;
                    break;
                case ProcessingOutcome.Duplicate:
                    Duplicate++;
                    break;
                case ProcessingOutcome.Equivalent:
                    Equivalent++;
                    break;
                case ProcessingOutcome.Stale:
                    Stale++;
                    break;
                case ProcessingOutcome.Invalid:
                    Invalid++;
                    break;
                case ProcessingOutcome.Conflict:
                    Conflict++;
                    break;
                default:
                    throw new InvalidOperationException("The batch contains an unsupported processing outcome.");
            }
        }

        Total = results.Count;
    }

    public int Total { get; }

    public int Applied { get; private set; }

    public int Duplicate { get; private set; }

    public int Equivalent { get; private set; }

    public int Stale { get; private set; }

    public int Invalid { get; private set; }

    public int Conflict { get; private set; }
}
