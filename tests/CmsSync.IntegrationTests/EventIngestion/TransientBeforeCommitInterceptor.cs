using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class TransientBeforeCommitInterceptor : DbTransactionInterceptor
{
    private int _remainingFailures = 1;
    private int _startedTransactions;
    private int _committedTransactions;

    public int StartedTransactions => Volatile.Read(ref _startedTransactions);

    public int CommittedTransactions => Volatile.Read(ref _committedTransactions);

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _startedTransactions);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
        {
            throw new InjectedTransientEventProcessingException();
        }

        return ValueTask.FromResult(result);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _committedTransactions);
        return Task.CompletedTask;
    }
}
