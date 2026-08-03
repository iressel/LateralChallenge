using CmsSync.Application.EventIngestion;

namespace CmsSync.IntegrationTests.Webhook;

internal sealed class ThrowingEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly Func<Exception> _exceptionFactory;

    public ThrowingEventTransactionExecutor(Func<Exception> exceptionFactory)
    {
        _exceptionFactory = exceptionFactory;
    }

    public Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw _exceptionFactory();
    }
}
