using CmsSync.Application.EventIngestion;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class CancelAfterSequenceEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly IEventTransactionExecutor _inner;
    private readonly CancellationTokenSource _cancellationSource;
    private readonly int _cancellationSequence;
    private readonly List<int> _invokedSequences = [];

    public CancelAfterSequenceEventTransactionExecutor(
        IEventTransactionExecutor inner,
        CancellationTokenSource cancellationSource,
        int cancellationSequence)
    {
        _inner = inner;
        _cancellationSource = cancellationSource;
        _cancellationSequence = cancellationSequence;
    }

    public IReadOnlyList<int> InvokedSequences => _invokedSequences;

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        _invokedSequences.Add(request.Item.Sequence);
        var result = await _inner.ExecuteAsync(request, cancellationToken);

        if (request.Item.Sequence == _cancellationSequence)
        {
            await _cancellationSource.CancelAsync();
        }

        return result;
    }
}
