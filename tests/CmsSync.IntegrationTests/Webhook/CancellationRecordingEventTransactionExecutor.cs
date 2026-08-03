using CmsSync.Application.EventIngestion;

namespace CmsSync.IntegrationTests.Webhook;

internal sealed class CancellationRecordingEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly IEventTransactionExecutor _inner;
    private int _observedCancelableToken;

    public CancellationRecordingEventTransactionExecutor(IEventTransactionExecutor inner)
    {
        _inner = inner;
    }

    public bool ObservedCancelableToken => Volatile.Read(ref _observedCancelableToken) == 1;

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            Interlocked.Exchange(ref _observedCancelableToken, 1);
        }

        return await _inner.ExecuteAsync(request, cancellationToken);
    }
}
