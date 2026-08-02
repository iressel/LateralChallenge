using System.Text;
using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class CmsEventBatchServiceTests
{
    private static readonly int[] FourSequences = [0, 1, 2, 3];
    private static readonly int[] FirstTwoSequences = [0, 1];

    [Fact]
    public async Task ItemsAreInvokedAndReturnedInExactSequenceOrder()
    {
        var executor = CreateExecutor((request, _) => Task.FromResult(CreateResult(request.Item.Sequence)));
        var service = new CmsEventBatchService(executor);

        var result = await service.ProcessAsync(CreateRequest(4), TestContext.Current.CancellationToken);

        Assert.Equal(FourSequences, executor.Requests.Select(request => request.Item.Sequence));
        Assert.Equal(FourSequences, result.Results.Select(item => item.Sequence));
    }

    [Fact]
    public async Task NextItemDoesNotStartUntilTheCurrentItemCompletesAndExecutionIsNeverParallel()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = CreateExecutor(async (request, cancellationToken) =>
        {
            if (request.Item.Sequence == 0)
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            return CreateResult(request.Item.Sequence);
        });
        var service = new CmsEventBatchService(executor);

        var processing = service.ProcessAsync(CreateRequest(3), TestContext.Current.CancellationToken);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Single(executor.Requests);
        releaseFirst.SetResult();
        await processing;

        Assert.Equal(1, executor.MaximumConcurrentCalls);
        Assert.Equal(3, executor.Requests.Count);
    }

    [Fact]
    public async Task SummaryCountsEverySupportedOutcomeExactly()
    {
        var outcomes = Enum.GetValues<ProcessingOutcome>();
        var executor = CreateExecutor((request, _) => Task.FromResult(
            CreateResult(request.Item.Sequence, outcomes[request.Item.Sequence])));
        var service = new CmsEventBatchService(executor);

        var result = await service.ProcessAsync(
            CreateRequest(outcomes.Length),
            TestContext.Current.CancellationToken);

        Assert.Equal(6, result.Summary.Total);
        Assert.Equal(1, result.Summary.Applied);
        Assert.Equal(1, result.Summary.Duplicate);
        Assert.Equal(1, result.Summary.Equivalent);
        Assert.Equal(1, result.Summary.Stale);
        Assert.Equal(1, result.Summary.Invalid);
        Assert.Equal(1, result.Summary.Conflict);
    }

    [Fact]
    public async Task MixedOutcomesProduceOneCompletedBatchWithSafeMetadata()
    {
        var outcomes = new[]
        {
            ProcessingOutcome.Applied,
            ProcessingOutcome.Invalid,
            ProcessingOutcome.Conflict,
        };
        var executor = CreateExecutor((request, _) => Task.FromResult(
            new EventTransactionResult(
                request.Item.Sequence,
                $"event-{request.Item.Sequence}",
                $"entity-{request.Item.Sequence}",
                outcomes[request.Item.Sequence],
                $"CODE_{request.Item.Sequence}",
                generation: 1,
                resultingVersion: 1)));
        var service = new CmsEventBatchService(executor);

        var result = await service.ProcessAsync(CreateRequest(3), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Results.Count);
        Assert.Equal(outcomes, result.Results.Select(item => item.Outcome));
        Assert.All(result.Results, item => Assert.DoesNotContain("payload", item.Code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecutorFailureStopsLaterItemsAndDoesNotReinvokePriorItems()
    {
        var executor = CreateExecutor((request, _) =>
        {
            if (request.Item.Sequence == 1)
            {
                throw new InvalidOperationException("safe test failure");
            }

            return Task.FromResult(CreateResult(request.Item.Sequence));
        });
        var service = new CmsEventBatchService(executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(CreateRequest(4), TestContext.Current.CancellationToken));

        Assert.Equal(FirstTwoSequences, executor.Requests.Select(request => request.Item.Sequence));
    }

    [Fact]
    public async Task CancellationAfterACompletedItemStopsBeforeTheNextInvocation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = CreateExecutor((request, _) =>
        {
            cancellationSource.Cancel();
            return Task.FromResult(CreateResult(request.Item.Sequence));
        });
        var service = new CmsEventBatchService(executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProcessAsync(CreateRequest(3), cancellationSource.Token));

        Assert.Single(executor.Requests);
        Assert.Equal(0, executor.Requests[0].Item.Sequence);
    }

    [Fact]
    public async Task CancellationTokenIsPropagatedUnchanged()
    {
        using var cancellationSource = new CancellationTokenSource();
        var executor = CreateExecutor((request, _) => Task.FromResult(CreateResult(request.Item.Sequence)));
        var service = new CmsEventBatchService(executor);

        await service.ProcessAsync(CreateRequest(2), cancellationSource.Token);

        Assert.All(executor.CancellationTokens, token => Assert.Equal(cancellationSource.Token, token));
    }

    [Fact]
    public async Task MismatchedTransactionSequenceFailsSafelyAndStopsTheBatch()
    {
        var executor = CreateExecutor((_, _) => Task.FromResult(CreateResult(sequence: 9)));
        var service = new CmsEventBatchService(executor);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(CreateRequest(2), TestContext.Current.CancellationToken));

        Assert.Equal(
            "The transaction result sequence does not match the submitted batch position.",
            exception.Message);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public void EmptyOrInvalidBatchMetadataFailsBeforeExecutorInvocation()
    {
        var items = ParseItems(1);

        Assert.Throws<ArgumentException>(() => new CmsEventBatchRequest(
            Guid.Empty,
            items,
            "correlation",
            "subject"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CmsEventBatchRequest(
            Guid.NewGuid(),
            Array.Empty<ParsedCmsEventItem>(),
            "correlation",
            "subject"));
        Assert.Throws<ArgumentException>(() => new CmsEventBatchRequest(
            Guid.NewGuid(),
            items,
            string.Empty,
            "subject"));
        Assert.Throws<ArgumentException>(() => new CmsEventBatchRequest(
            Guid.NewGuid(),
            items,
            "correlation",
            new string('s', CmsEventIngestionLimits.MaximumIdentifierLength + 1)));
    }

    [Fact]
    public void NonContiguousItemSequencesAreRejected()
    {
        var item = Assert.Single(ParseItems(1));

        Assert.Throws<ArgumentException>(() => new CmsEventBatchRequest(
            Guid.NewGuid(),
            new[] { item, item },
            "correlation",
            "subject"));
    }

    private static RecordingEventTransactionExecutor CreateExecutor(
        Func<EventTransactionRequest, CancellationToken, Task<EventTransactionResult>> handler)
    {
        return new RecordingEventTransactionExecutor(handler);
    }

    private static EventTransactionResult CreateResult(
        int sequence,
        ProcessingOutcome outcome = ProcessingOutcome.Applied)
    {
        return new EventTransactionResult(sequence, null, $"entity-{sequence}", outcome, "TEST_CODE");
    }

    private static CmsEventBatchRequest CreateRequest(int itemCount)
    {
        return new CmsEventBatchRequest(
            Guid.NewGuid(),
            ParseItems(itemCount),
            "unit-correlation",
            "unit-subject");
    }

    private static IReadOnlyList<ParsedCmsEventItem> ParseItems(int itemCount)
    {
        var json = $"[{string.Join(',', Enumerable.Repeat("{}", itemCount))}]";
        var parseResult = new CmsEventArrayParser().Parse(Encoding.UTF8.GetBytes(json));

        Assert.True(parseResult.IsSuccess, parseResult.Failure?.Code);
        return parseResult.Items;
    }
}
