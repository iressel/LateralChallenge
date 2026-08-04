using System.Xml.Linq;
using Xunit;

namespace CmsSync.UnitTests.Architecture;

public sealed class ProjectDependencyTests
{
    private static readonly Dictionary<string, string[]> ExpectedReferences = new(StringComparer.Ordinal)
    {
        ["src/CmsSync.Domain/CmsSync.Domain.csproj"] = [],
        ["src/CmsSync.Application/CmsSync.Application.csproj"] =
        [
            "src/CmsSync.Domain/CmsSync.Domain.csproj",
        ],
        ["src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj"] =
        [
            "src/CmsSync.Application/CmsSync.Application.csproj",
            "src/CmsSync.Domain/CmsSync.Domain.csproj",
        ],
        ["src/CmsSync.Api/CmsSync.Api.csproj"] =
        [
            "src/CmsSync.Application/CmsSync.Application.csproj",
            "src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj",
        ],
        ["tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj"] =
        [
            "src/CmsSync.Application/CmsSync.Application.csproj",
            "src/CmsSync.Domain/CmsSync.Domain.csproj",
        ],
        ["tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj"] =
        [
            "src/CmsSync.Api/CmsSync.Api.csproj",
            "src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj",
        ],
    };

    [Fact]
    public void ProjectReferencesMatchTheApprovedDependencyGraph()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

        foreach (var (projectPath, expectedReferences) in ExpectedReferences)
        {
            var absoluteProjectPath = Path.Combine(
                repositoryRoot,
                projectPath.Replace('/', Path.DirectorySeparatorChar));
            var actualReferences = ReadProjectReferences(repositoryRoot, absoluteProjectPath);

            Assert.Equal(expectedReferences, actualReferences);
        }
    }

    [Fact]
    public void ProjectReferenceSeparatorsResolveToSameCanonicalRepositoryPath()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectDirectory = Path.Combine(repositoryRoot, "src", "CmsSync.Application");

        var windowsStyleReference = ResolveProjectReference(
            repositoryRoot,
            projectDirectory,
            @"..\CmsSync.Domain\CmsSync.Domain.csproj");
        var unixStyleReference = ResolveProjectReference(
            repositoryRoot,
            projectDirectory,
            "../CmsSync.Domain/CmsSync.Domain.csproj");

        Assert.Equal("src/CmsSync.Domain/CmsSync.Domain.csproj", windowsStyleReference);
        Assert.Equal(windowsStyleReference, unixStyleReference);
    }

    private static string[] ReadProjectReferences(string repositoryRoot, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");
        var document = XDocument.Load(projectPath, LoadOptions.None);
        var references = new List<string>();

        foreach (var projectReference in document.Descendants("ProjectReference"))
        {
            var include = (string?)projectReference.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                throw new InvalidDataException($"ProjectReference has no Include value in {projectPath}.");
            }

            references.Add(ResolveProjectReference(repositoryRoot, projectDirectory, include));
        }

        return [.. references.Order(StringComparer.Ordinal)];
    }

    private static string ResolveProjectReference(
        string repositoryRoot,
        string projectDirectory,
        string include)
    {
        var normalizedInclude = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var absoluteReference = Path.GetFullPath(Path.Combine(projectDirectory, normalizedInclude));

        return Path.GetRelativePath(repositoryRoot, absoluteReference)
            .Replace('\\', '/');
    }

    private static string FindRepositoryRoot(string startingPath)
    {
        for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LateralChallenge.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LateralChallenge repository root.");
    }
}
