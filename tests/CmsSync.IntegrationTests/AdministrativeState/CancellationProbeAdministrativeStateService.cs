using CmsSync.Application.AdministrativeState;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal sealed class CancellationProbeAdministrativeStateService : IAdministrativeStateService
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _cancellationObserved;

    public Task Started => _started.Task;

    public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) == 1;

    public async Task<AdministrativeStateResult?> SetAsync(
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken)
    {
        _started.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _cancellationObserved, 1);
            throw;
        }

        throw new InvalidOperationException("The cancellation probe completed unexpectedly.");
    }
}
