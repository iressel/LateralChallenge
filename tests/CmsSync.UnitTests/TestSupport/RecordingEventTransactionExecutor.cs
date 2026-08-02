using System.Collections.Concurrent;
using CmsSync.Application.EventIngestion;

namespace CmsSync.UnitTests.TestSupport;

internal sealed class RecordingEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly Lock _recordingLock = new();
    private readonly ConcurrentQueue<EventTransactionRequest> _requests = new();
    private readonly ConcurrentQueue<CancellationToken> _cancellationTokens = new();
    private readonly Func<EventTransactionRequest, CancellationToken, Task<EventTransactionResult>> _handler;
    private int _activeCalls;
    private int _maximumConcurrentCalls;

    public RecordingEventTransactionExecutor(
        Func<EventTransactionRequest, CancellationToken, Task<EventTransactionResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
    }

    public IReadOnlyList<EventTransactionRequest> Requests
    {
        get
        {
            lock (_recordingLock)
            {
                return _requests.ToArray();
            }
        }
    }

    public IReadOnlyList<CancellationToken> CancellationTokens
    {
        get
        {
            lock (_recordingLock)
            {
                return _cancellationTokens.ToArray();
            }
        }
    }

    public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        lock (_recordingLock)
        {
            _requests.Enqueue(request);
            _cancellationTokens.Enqueue(cancellationToken);
        }

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
