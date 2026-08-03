namespace CmsSync.IntegrationTests.Observability;

internal sealed class AsyncDeadlockGate
{
    private readonly TaskCompletionSource _bothArrived = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivalCount;

    public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrivalCount) == 2)
        {
            _bothArrived.TrySetResult();
        }

        await _bothArrived.Task.WaitAsync(cancellationToken);
    }
}
