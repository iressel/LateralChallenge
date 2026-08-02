namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventBatchService
{
    private readonly IEventTransactionExecutor _transactionExecutor;

    public CmsEventBatchService(IEventTransactionExecutor transactionExecutor)
    {
        _transactionExecutor = transactionExecutor
            ?? throw new ArgumentNullException(nameof(transactionExecutor));
    }

    public async Task<CmsEventBatchResult> ProcessAsync(
        CmsEventBatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new CmsEventBatchItemResult[request.Items.Count];

        for (var index = 0; index < request.Items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = request.Items[index];
            var transactionRequest = new EventTransactionRequest(
                request.BatchId,
                item,
                request.CorrelationId,
                request.AuthenticatedCmsSubject);
            var transactionResult = await _transactionExecutor.ExecuteAsync(
                transactionRequest,
                cancellationToken);

            if (transactionResult.Sequence != item.Sequence)
            {
                throw new InvalidOperationException(
                    "The transaction result sequence does not match the submitted batch position.");
            }

            results[index] = new CmsEventBatchItemResult(transactionResult);
        }

        return new CmsEventBatchResult(request.BatchId, results);
    }
}
