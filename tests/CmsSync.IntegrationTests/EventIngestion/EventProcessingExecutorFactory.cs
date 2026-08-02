using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CmsSync.IntegrationTests.EventIngestion;

internal static class EventProcessingExecutorFactory
{
    public static SqlServerEventTransactionExecutor Create(
        string connectionString,
        IEnumerable<IInterceptor>? interceptors = null,
        SqlServerEntityApplicationLock? applicationLock = null,
        bool useTestExecutionStrategy = false)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CmsWriteDbContext>()
            .UseSqlServer(connectionString);

        if (interceptors is not null)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        if (useTestExecutionStrategy)
        {
            optionsBuilder.ReplaceService<IExecutionStrategyFactory, TestEventProcessingExecutionStrategyFactory>();
        }

        return new SqlServerEventTransactionExecutor(
            optionsBuilder.Options,
            new EventValidator(),
            applicationLock ?? new SqlServerEntityApplicationLock(),
            TimeProvider.System);
    }
}
