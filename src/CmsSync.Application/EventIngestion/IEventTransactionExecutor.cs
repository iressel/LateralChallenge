namespace CmsSync.Application.EventIngestion;

public interface IEventTransactionExecutor
{
    Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken);
}
