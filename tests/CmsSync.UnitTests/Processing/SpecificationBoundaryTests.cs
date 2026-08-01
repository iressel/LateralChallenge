using CmsSync.Application.EventIngestion;
using CmsSync.Domain.Processing;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class SpecificationBoundaryTests
{
    [Fact]
    public void AC055PureDomainAndEventIngestionAssembliesDoNotReferenceEfCoreOrAspNetCore()
    {
        var assemblies = new[]
        {
            typeof(CmsEntityStateMachine).Assembly,
            typeof(EventValidator).Assembly,
        };
        var forbiddenReferences = assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }
}
