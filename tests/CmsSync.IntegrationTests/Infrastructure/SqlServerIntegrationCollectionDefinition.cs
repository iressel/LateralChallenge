using Xunit;

namespace CmsSync.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationCollectionDefinition : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServerIntegration";
}
