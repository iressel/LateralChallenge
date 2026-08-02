using Microsoft.EntityFrameworkCore.Storage;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class TestEventProcessingExecutionStrategy : ExecutionStrategy
{
    public TestEventProcessingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 2, maxRetryDelay: TimeSpan.Zero)
    {
    }

    protected override bool ShouldRetryOn(Exception exception)
    {
        return exception is InjectedTransientEventProcessingException;
    }
}
