using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class TerminalBeforeCommitInterceptor : DbTransactionInterceptor
{
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        throw new InjectedTerminalEventProcessingException();
    }
}
