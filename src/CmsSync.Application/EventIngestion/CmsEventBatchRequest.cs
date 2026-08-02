namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventBatchRequest
{
    public CmsEventBatchRequest(
        Guid batchId,
        IReadOnlyList<ParsedCmsEventItem> items,
        string correlationId,
        string authenticatedCmsSubject)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("The batch identifier must not be empty.", nameof(batchId));
        }

        ArgumentNullException.ThrowIfNull(items);
        ValidateMetadata(correlationId, nameof(correlationId));
        ValidateMetadata(authenticatedCmsSubject, nameof(authenticatedCmsSubject));

        if (items.Count is 0 or > CmsEventIngestionLimits.AbsoluteMaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(items),
                items.Count,
                $"The batch must contain between 1 and {CmsEventIngestionLimits.AbsoluteMaximumBatchSize} items.");
        }

        var copiedItems = new ParsedCmsEventItem[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index]
                ?? throw new ArgumentException("The batch cannot contain null items.", nameof(items));

            if (item.Sequence != index)
            {
                throw new ArgumentException(
                    "Batch item sequences must be contiguous and match their original positions.",
                    nameof(items));
            }

            copiedItems[index] = item;
        }

        BatchId = batchId;
        Items = Array.AsReadOnly(copiedItems);
        CorrelationId = correlationId;
        AuthenticatedCmsSubject = authenticatedCmsSubject;
    }

    public Guid BatchId { get; }

    public IReadOnlyList<ParsedCmsEventItem> Items { get; }

    public string CorrelationId { get; }

    public string AuthenticatedCmsSubject { get; }

    internal static void ValidateMetadata(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > CmsEventIngestionLimits.MaximumIdentifierLength ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The value must contain 1 through {CmsEventIngestionLimits.MaximumIdentifierLength} non-control characters.",
                parameterName);
        }
    }
}
