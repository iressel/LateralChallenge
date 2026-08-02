using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class AmbiguousCommitInterceptor : DbTransactionInterceptor
{
    private int _remainingFailures = 1;
    private int _committedTransactions;

    public int CommittedTransactions => Volatile.Read(ref _committedTransactions);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _committedTransactions);

        if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
        {
            throw new InjectedTransientEventProcessingException();
        }

        return Task.CompletedTask;
    }
}
