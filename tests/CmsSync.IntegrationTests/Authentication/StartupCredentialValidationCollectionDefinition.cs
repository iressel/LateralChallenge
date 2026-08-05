using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StartupCredentialValidationCollectionDefinition
{
    public const string Name = "StartupCredentialValidation";
}
