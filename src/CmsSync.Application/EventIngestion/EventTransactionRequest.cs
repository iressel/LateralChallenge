namespace CmsSync.Application.EventIngestion;

public sealed class EventTransactionRequest
{
    public EventTransactionRequest(
        Guid batchId,
        ParsedCmsEventItem item,
        string correlationId,
        string authenticatedCmsSubject)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("The batch identifier must not be empty.", nameof(batchId));
        }

        ArgumentNullException.ThrowIfNull(item);
        CmsEventBatchRequest.ValidateMetadata(correlationId, nameof(correlationId));
        CmsEventBatchRequest.ValidateMetadata(
            authenticatedCmsSubject,
            nameof(authenticatedCmsSubject));

        BatchId = batchId;
        Item = item;
        CorrelationId = correlationId;
        AuthenticatedCmsSubject = authenticatedCmsSubject;
    }

    public Guid BatchId { get; }

    public ParsedCmsEventItem Item { get; }

    public string CorrelationId { get; }

    public string AuthenticatedCmsSubject { get; }
}
