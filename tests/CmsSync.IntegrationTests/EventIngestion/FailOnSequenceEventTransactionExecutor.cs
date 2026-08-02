using CmsSync.Application.EventIngestion;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class FailOnSequenceEventTransactionExecutor : IEventTransactionExecutor
{
    private readonly IEventTransactionExecutor _inner;
    private readonly int _failingSequence;
    private readonly List<int> _invokedSequences = [];

    public FailOnSequenceEventTransactionExecutor(
        IEventTransactionExecutor inner,
        int failingSequence)
    {
        _inner = inner;
        _failingSequence = failingSequence;
    }

    public IReadOnlyList<int> InvokedSequences => _invokedSequences;

    public async Task<EventTransactionResult> ExecuteAsync(
        EventTransactionRequest request,
        CancellationToken cancellationToken)
    {
        _invokedSequences.Add(request.Item.Sequence);

        if (request.Item.Sequence == _failingSequence)
        {
            throw new InjectedTerminalEventProcessingException();
        }

        return await _inner.ExecuteAsync(request, cancellationToken);
    }
}
