using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.Documentation;

[Trait("Category", "Documentation")]
public sealed class DocumentationArtifactTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    [Fact]
    public void ReadmeExistsAndContainsTheExpectedNineteenNumberedSections()
    {
        var readmePath = Path.Combine(RepositoryRoot, "README.md");
        Assert.True(File.Exists(readmePath), "README.md was not found in the repository root.");

        var readme = File.ReadAllText(readmePath);
        var sectionNumbers = Regex.Matches(readme, "(?m)^##\\s+(\\d+)\\.")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(Enumerable.Range(1, 19).ToArray(), sectionNumbers);
    }

    [Fact]
    public void ReadmeMarkdownLinksResolveToExistingRepositoryFiles()
    {
        var readme = ReadRepositoryFile("README.md");
        var linkTargets = Regex.Matches(readme, "\\[[^\\]]+\\]\\(([^)]+)\\)")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(linkTargets);

        foreach (var rawTarget in linkTargets)
        {
            var target = rawTarget.Split('#')[0].Trim();

            if (target.Length == 0 ||
                target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedTarget = Uri.UnescapeDataString(target.Replace('/', Path.DirectorySeparatorChar));
            var fullPath = Path.GetFullPath(Path.Combine(RepositoryRoot, normalizedTarget));

            Assert.StartsWith(RepositoryRoot, fullPath, StringComparison.Ordinal);
            Assert.True(File.Exists(fullPath), $"README link target was not found: {rawTarget}");
        }
    }

    [Fact]
    public void ReadmeContainsRequiredSetupValidationCommandsAndPinnedImage()
    {
        var readme = ReadRepositoryFile("README.md");

        Assert.Contains(
            "dotnet restore LateralChallenge.sln --source https://api.nuget.org/v3/index.json",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet build LateralChallenge.sln --configuration Release --no-restore",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet format LateralChallenge.sln --verify-no-changes --no-restore",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --no-build --no-restore",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter \"Category=SqlServer\"",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("docker compose config --quiet", readme, StringComparison.Ordinal);
        Assert.Contains("docker compose up --build --wait", readme, StringComparison.Ordinal);
        Assert.Contains("pwsh ./scripts/validate-container-setup.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("pwsh ./scripts/verify-container-cleanup.ps1", readme, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet ef migrations script --idempotent --project src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj --startup-project src/CmsSync.Api/CmsSync.Api.csproj --context CmsWriteDbContext",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(SqlServerTestConstants.Image, readme, StringComparison.Ordinal);

        Assert.DoesNotContain("mssql/server:latest", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2022-latest", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeWebhookExampleIsRawArrayWithWireIdAndDocumentedTypeVariants()
    {
        var readme = ReadRepositoryFile("README.md");
        var json = ExtractFencedBlock(
            readme,
            "### Example: raw webhook array request",
            "json");

        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(3, document.RootElement.GetArrayLength());

        var events = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(events, item => string.Equals(item.GetProperty("type").GetString(), "Publish", StringComparison.Ordinal));
        Assert.Contains(
            events,
            item => string.Equals(item.GetProperty("type").GetString()?.Trim(), "unPublish", StringComparison.Ordinal)
                && string.Equals(item.GetProperty("type").GetString(), "unPublish", StringComparison.Ordinal) is false);
        Assert.Contains(events, item => string.Equals(item.GetProperty("type").GetString(), "DELETE", StringComparison.Ordinal));

        foreach (var item in events)
        {
            Assert.True(item.TryGetProperty("id", out _));
            Assert.False(item.TryGetProperty("entityId", out _));
            Assert.True(item.TryGetProperty("timestamp", out _));
        }

        Assert.Contains(events, item => item.TryGetProperty("eventId", out _));
        Assert.Contains(events, item => item.TryGetProperty("eventId", out _) is false);

        var delete = events.Single(item => string.Equals(item.GetProperty("type").GetString(), "DELETE", StringComparison.Ordinal));
        Assert.False(delete.TryGetProperty("version", out _));
        Assert.False(delete.TryGetProperty("payload", out _));

        var versioned = events.Where(item => !string.Equals(item.GetProperty("type").GetString(), "DELETE", StringComparison.Ordinal));
        Assert.All(versioned, item =>
        {
            Assert.True(item.TryGetProperty("version", out _));
            Assert.True(item.TryGetProperty("payload", out var payload));
            Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        });
    }

    [Fact]
    public void ReadmeDocumentsOutcomesRetryRulesAndTimestampHighWatermarkBehavior()
    {
        var readme = ReadRepositoryFile("README.md");
        var expectedOutcomes = new[]
        {
            "`applied`",
            "`duplicate`",
            "`equivalent`",
            "`stale`",
            "`invalid`",
            "`conflict`",
        };

        foreach (var outcome in expectedOutcomes)
        {
            Assert.Contains(outcome, readme, StringComparison.Ordinal);
        }

        Assert.Contains("retry the entire original request", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not retry unchanged deterministic `invalid` or `conflict` items", readme, StringComparison.Ordinal);

        Assert.Contains("CurrentVersionOccurredAtUtc", readme, StringComparison.Ordinal);
        Assert.Contains("EntityEventHighWatermarkUtc", readme, StringComparison.Ordinal);
        Assert.Contains("Start at Version 5 with both timestamps at 10:00", readme, StringComparison.Ordinal);
        Assert.Contains("Accept Version 6 at 09:00", readme, StringComparison.Ordinal);
        Assert.Contains("Delete at 09:30 is `stale`", readme, StringComparison.Ordinal);
        Assert.Contains("Delete at 10:00 under a new identity is `conflict`", readme, StringComparison.Ordinal);
        Assert.Contains("Delete after 10:00 is `applied`", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeDocumentsSecuritySchemesPoliciesRolesAndPlatformBoundaries()
    {
        var readme = ReadRepositoryFile("README.md");

        Assert.Contains("`CmsBasic`", readme, StringComparison.Ordinal);
        Assert.Contains("`ConsumerBasic`", readme, StringComparison.Ordinal);
        Assert.Contains("`CmsEvents`", readme, StringComparison.Ordinal);
        Assert.Contains("`ConsumerAccess`", readme, StringComparison.Ordinal);
        Assert.Contains("`AdministratorAccess`", readme, StringComparison.Ordinal);
        Assert.Contains("`CmsService`", readme, StringComparison.Ordinal);
        Assert.Contains("`NormalConsumer`", readme, StringComparison.Ordinal);
        Assert.Contains("`Administrator`", readme, StringComparison.Ordinal);

        Assert.Contains("`Cache-Control: no-store`", readme, StringComparison.Ordinal);
        Assert.Contains("HTTPS", readme, StringComparison.Ordinal);

        Assert.Contains("Apple Silicon", readme, StringComparison.Ordinal);
        Assert.Contains("remote supported SQL Server or Azure SQL", readme, StringComparison.Ordinal);
        Assert.Contains("Do not add `platform: linux/amd64`", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksFileKeepsT018Unchecked()
    {
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");

        Assert.Contains("- [ ] **T018", tasks, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
    }

    private static string FindRepositoryRoot(string startingPath)
    {
        for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LateralChallenge.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LateralChallenge repository root.");
    }

    private static string ExtractFencedBlock(string markdown, string sectionHeading, string language)
    {
        var headingIndex = markdown.IndexOf(sectionHeading, StringComparison.Ordinal);
        Assert.True(headingIndex >= 0, $"Section heading was not found: {sectionHeading}");

        var openingFence = "```" + language;
        var fenceStart = markdown.IndexOf(openingFence, headingIndex, StringComparison.Ordinal);
        Assert.True(fenceStart >= 0, $"Fenced block with language '{language}' was not found after: {sectionHeading}");

        var contentStart = fenceStart + openingFence.Length;
        if (contentStart < markdown.Length && markdown[contentStart] == '\r')
        {
            contentStart++;
        }

        if (contentStart < markdown.Length && markdown[contentStart] == '\n')
        {
            contentStart++;
        }

        var fenceEnd = markdown.IndexOf("```", contentStart, StringComparison.Ordinal);
        Assert.True(fenceEnd >= 0, "Closing fenced block delimiter was not found.");

        return markdown[contentStart..fenceEnd].Trim();
    }
}
