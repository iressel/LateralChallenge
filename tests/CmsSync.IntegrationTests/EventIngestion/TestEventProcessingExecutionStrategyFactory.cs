using Microsoft.EntityFrameworkCore.Storage;

namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class TestEventProcessingExecutionStrategyFactory : IExecutionStrategyFactory
{
    private readonly ExecutionStrategyDependencies _dependencies;

    public TestEventProcessingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public IExecutionStrategy Create()
    {
        return new TestEventProcessingExecutionStrategy(_dependencies);
    }
}
