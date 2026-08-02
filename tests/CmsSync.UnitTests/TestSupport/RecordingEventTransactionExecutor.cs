using CmsSync.Application.EventIngestion;

namespace CmsSync.UnitTests.TestSupport;

internal sealed class RecordingEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly Func<EventTransactionRequest, CancellationToken, Task<EventTransactionResult>> _handler;
    private int _activeCalls;
    private int _maximumConcurrentCalls;

    public RecordingEventTransactionExecutor(
        Func<EventTransactionRequest, CancellationToken, Task<EventTransactionResult>> handler)
    {
        _handler = handler;
    }

    public List<EventTransactionRequest> Requests { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public int MaximumConcurrentCalls => _maximumConcurrentCalls;

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        CancellationTokens.Add(cancellationToken);
        var activeCalls = Interlocked.Increment(ref _activeCalls);
        UpdateMaximumConcurrentCalls(activeCalls);

        try
        {
            return await _handler(request, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    private void UpdateMaximumConcurrentCalls(int activeCalls)
    {
        while (true)
        {
            var currentMaximum = Volatile.Read(ref _maximumConcurrentCalls);

            if (activeCalls <= currentMaximum ||
                Interlocked.CompareExchange(
                    ref _maximumConcurrentCalls,
                    activeCalls,
                    currentMaximum) == currentMaximum)
            {
                return;
            }
        }
    }
}
