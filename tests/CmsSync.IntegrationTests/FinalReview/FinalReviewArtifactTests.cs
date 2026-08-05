using System.Text.RegularExpressions;
using Xunit;

namespace CmsSync.IntegrationTests.FinalReview;

[Trait("Category", "FinalReview")]
public sealed class FinalReviewArtifactTests
{
    private const string ExpectedSqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";

    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    [Fact]
    public void SpecificationContainsAllFrNfrSecIdentifiersExactlyOnce()
    {
        var spec = ReadRepositoryFile("specs/cms-event-ingestion/spec.md");

        AssertIdentifierRange(spec, "FR", 1, 30, "(?m)^- \\*\\*(FR-\\d{3})");
        AssertIdentifierRange(spec, "NFR", 1, 8, "(?m)^- \\*\\*(NFR-\\d{3})");
        AssertIdentifierRange(spec, "SEC", 1, 4, "(?m)^- \\*\\*(SEC-\\d{3})");
    }

    [Fact]
    public void SpecificationContainsAllAcceptanceCriteriaExactlyOnce()
    {
        var spec = ReadRepositoryFile("specs/cms-event-ingestion/spec.md");

        AssertIdentifierRange(spec, "AC", 1, 57, "(?m)^- \\*\\*(AC-\\d{3})");
    }

    [Fact]
    public void SpecificationAcceptanceCriteriaReferenceEveryRequirementFamily()
    {
        var spec = ReadRepositoryFile("specs/cms-event-ingestion/spec.md");
        var acceptanceSection = ExtractSection(
            spec,
            "## 17. Acceptance criteria",
            "## 18. Explicit assumptions");

        var referencedFr = Regex.Matches(acceptanceSection, "FR-\\d{3}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var referencedNfr = Regex.Matches(acceptanceSection, "NFR-\\d{3}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var referencedSec = Regex.Matches(acceptanceSection, "SEC-\\d{3}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedIdentifiers("FR", 1, 30), referencedFr);
        Assert.Equal(ExpectedIdentifiers("NFR", 1, 8), referencedNfr);
        Assert.Equal(ExpectedIdentifiers("SEC", 1, 4), referencedSec);
    }

    [Fact]
    public void TasksTraceabilitySectionsCoverAllRequirementAndAcceptanceIdentifiers()
    {
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");
        var requirementTraceability = ExtractSection(
            tasks,
            "## 18. Requirement traceability",
            "## 19. Acceptance-criterion traceability");
        var acceptanceTraceability = ExtractSection(
            tasks,
            "## 19. Acceptance-criterion traceability",
            "## 20. Major-plan-phase traceability");

        var requirements = Regex.Matches(requirementTraceability, "\\|\\s*(FR-\\d{3}|NFR-\\d{3}|SEC-\\d{3})\\s*\\|")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var acceptanceCriteria = Regex.Matches(acceptanceTraceability, "AC-\\d{3}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ExpectedIdentifiers("FR", 1, 30)
                .Concat(ExpectedIdentifiers("NFR", 1, 8))
                .Concat(ExpectedIdentifiers("SEC", 1, 4))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            requirements);
        Assert.Equal(ExpectedIdentifiers("AC", 1, 57), acceptanceCriteria);
    }

    [Fact]
    public void CompletedTasksThroughT017ContainCompletionEvidenceAndT018IsUnchecked()
    {
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");

        for (var index = 1; index <= 17; index++)
        {
            var taskId = $"T{index:000}";
            var taskBlock = ExtractTaskBlock(tasks, taskId);

            Assert.Contains($"- [x] **{taskId}", taskBlock, StringComparison.Ordinal);
            Assert.Contains("Completion evidence (", taskBlock, StringComparison.Ordinal);
        }

        var t018Block = ExtractTaskBlock(tasks, "T018");
        Assert.Contains("- [ ] **T018", t018Block, StringComparison.Ordinal);
        Assert.DoesNotContain("Completion evidence (", t018Block, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractArtifactsAgreeOnRawArrayWireIdNormalizationAndTimestampSeparation()
    {
        var spec = ReadRepositoryFile("specs/cms-event-ingestion/spec.md");
        var readme = ReadRepositoryFile("README.md");
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");

        Assert.Contains("raw JSON array", spec, StringComparison.Ordinal);
        Assert.Contains("no wrapper object", spec, StringComparison.Ordinal);
        Assert.Contains("external entity property MUST be `id`", spec, StringComparison.Ordinal);
        Assert.Contains("MUST NOT expose `entityId`", spec, StringComparison.Ordinal);
        Assert.Contains("trimmed and matched case-insensitively", spec, StringComparison.Ordinal);
        Assert.Contains("CurrentVersionOccurredAtUtc", spec, StringComparison.Ordinal);
        Assert.Contains("EntityEventHighWatermarkUtc", spec, StringComparison.Ordinal);
        Assert.Contains("Delete ordering for an active entity MUST compare its Timestamp only with EntityEventHighWatermarkUtc", spec, StringComparison.Ordinal);

        Assert.Contains("Top-level JSON must be a raw array of 1 through 50 items.", readme, StringComparison.Ordinal);
        Assert.Contains("No `{ \"events\": [...] }` envelope.", readme, StringComparison.Ordinal);
        Assert.Contains("Webhook entity property is exactly `id`; `entityId` is not accepted.", readme, StringComparison.Ordinal);
        Assert.Contains("`CurrentVersionOccurredAtUtc` may move backward.", readme, StringComparison.Ordinal);
        Assert.Contains("`EntityEventHighWatermarkUtc` never moves backward.", readme, StringComparison.Ordinal);
        Assert.Contains("Delete ordering uses `EntityEventHighWatermarkUtc` only.", readme, StringComparison.Ordinal);

        Assert.Contains("wrapper/identifier/type-normalization drift", tasks, StringComparison.Ordinal);
        Assert.Contains("timestamp-column conflation", tasks, StringComparison.Ordinal);
        Assert.Contains("watermark regression", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationAndSnapshotDefineExactlyFourApplicationTables()
    {
        var migration = ReadRepositoryFile("src/CmsSync.Infrastructure/Persistence/Migrations/20260802142305_InitialCmsPersistence.cs");
        var snapshot = ReadRepositoryFile("src/CmsSync.Infrastructure/Persistence/Migrations/CmsWriteDbContextModelSnapshot.cs");

        var expectedTables = new[]
        {
            "CmsEntities",
            "CmsEntityRevisions",
            "CmsDeletionTombstones",
            "CmsEventProcessingLogs",
        };

        var migrationTables = Regex.Matches(migration, "CreateTable\\(\\s*name:\\s*\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var snapshotTables = Regex.Matches(snapshot, "\\.ToTable\\(\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedTables.Order(StringComparer.Ordinal), migrationTables);
        Assert.Equal(expectedTables.Order(StringComparer.Ordinal), snapshotTables);

        Assert.Contains("CurrentVersionOccurredAtUtc", migration, StringComparison.Ordinal);
        Assert.Contains("EntityEventHighWatermarkUtc", migration, StringComparison.Ordinal);
        Assert.Contains("CurrentVersionOccurredAtUtc", snapshot, StringComparison.Ordinal);
        Assert.Contains("EntityEventHighWatermarkUtc", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void DependenciesAndProvidersStayWithinContractBoundaries()
    {
        var packageProps = ReadRepositoryFile("Directory.Packages.props");
        var projectFiles = EnumerateRepositoryFiles(".csproj")
            .Select(ReadRepositoryFile)
            .ToArray();
        var productionCode = EnumerateRepositoryFiles("src/", ".cs")
            .Select(ReadRepositoryFile)
            .ToArray();

        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", packageProps, StringComparison.Ordinal);

        foreach (var text in projectFiles.Append(packageProps))
        {
            Assert.DoesNotContain("MediatR", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AutoMapper", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EntityFrameworkCore.InMemory", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LocalDB", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MSSQLLocalDB", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Npgsql", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MySql", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pomelo", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
        }

        var domainCode = EnumerateRepositoryFiles("src/CmsSync.Domain/", ".cs")
            .Select(ReadRepositoryFile)
            .ToArray();
        foreach (var text in domainCode)
        {
            Assert.DoesNotContain("using Microsoft.EntityFrameworkCore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("using Microsoft.AspNetCore", text, StringComparison.Ordinal);
        }

        var noMigrateOnStartup = string.Join(
            Environment.NewLine,
            productionCode.Where(text => text.Contains("Program", StringComparison.Ordinal) || text.Contains("Migrate", StringComparison.Ordinal)));
        Assert.DoesNotContain("Database.Migrate", noMigrateOnStartup, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", noMigrateOnStartup, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerAndCiArtifactsPreserveImmutablePinsAndPolicyRules()
    {
        var compose = ReadRepositoryFile("compose.yaml");
        var dockerfile = ReadRepositoryFile("Dockerfile");
        var envExample = ReadRepositoryFile(".env.example");
        var docs = ReadRepositoryFile("docs/container-development.md");
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var policyScript = ReadRepositoryFile("scripts/validate-repository-policy.ps1");

        Assert.Contains(ExpectedSqlServerImage, compose, StringComparison.Ordinal);
        Assert.Contains(ExpectedSqlServerImage, dockerfile, StringComparison.Ordinal);
        Assert.Contains(ExpectedSqlServerImage, envExample, StringComparison.Ordinal);

        foreach (var text in new[] { compose, dockerfile, workflow })
        {
            Assert.DoesNotContain("mcr.microsoft.com/mssql/server:latest", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("2022-latest", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("azure-sql-edge", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("platform: linux/amd64", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LocalDB", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Rosetta", docs, StringComparison.Ordinal);
        Assert.Contains("QEMU", docs, StringComparison.Ordinal);
        Assert.Contains("platform: linux/amd64", docs, StringComparison.Ordinal);
        Assert.Contains("Use a remote supported SQL Server instance or Azure SQL", docs, StringComparison.Ordinal);

        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("- main", workflow, StringComparison.Ordinal);
        Assert.Contains("feature/t016-*", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", workflow, StringComparison.Ordinal);
        Assert.Contains("ci-test-evidence", workflow, StringComparison.Ordinal);

        Assert.Contains("platform\\s*:\\s*linux/amd64", policyScript, StringComparison.Ordinal);
        Assert.Contains("This narrow deterministic scan does not replace GitHub secret scanning", policyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationTestsUseCategoriesAndDoNotContainSkippedCases()
    {
        var testFiles = EnumerateRepositoryFiles("tests/CmsSync.IntegrationTests/", ".cs")
            .ToArray();

        foreach (var relativePath in testFiles)
        {
            var testFile = ReadRepositoryFile(relativePath);
            var hasFactOrTheory = testFile.Contains("[Fact", StringComparison.Ordinal) ||
                testFile.Contains("[Theory", StringComparison.Ordinal);

            if (hasFactOrTheory)
            {
                Assert.Contains("[Trait(\"Category\"", testFile, StringComparison.Ordinal);
            }

            var hasSkippedTestAttribute = Regex.IsMatch(
                testFile,
                "\\[(Fact|Theory)\\s*\\([^\\)]*\\bSkip\\s*=",
                RegexOptions.CultureInvariant);
            Assert.False(hasSkippedTestAttribute, $"File contains skipped test attribute: {relativePath}");
        }
    }

    private static void AssertIdentifierRange(
        string source,
        string prefix,
        int start,
        int end,
        string capturePattern)
    {
        var identifiers = Regex.Matches(source, capturePattern)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedIdentifiers(prefix, start, end), identifiers);
    }

    private static string[] ExpectedIdentifiers(string prefix, int start, int end)
    {
        return Enumerable.Range(start, end - start + 1)
            .Select(index => $"{prefix}-{index:000}")
            .ToArray();
    }

    private static string ExtractTaskBlock(string tasks, string taskId)
    {
        var marker = $"- [";
        var taskHeader = $"**{taskId} ";
        var startIndex = tasks.IndexOf(taskHeader, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Task block was not found for {taskId}.");

        var previousMarkerIndex = tasks.LastIndexOf(marker, startIndex, StringComparison.Ordinal);
        Assert.True(previousMarkerIndex >= 0, $"Task line prefix was not found for {taskId}.");

        var nextTaskIndex = tasks.IndexOf("\n- [", startIndex, StringComparison.Ordinal);
        if (nextTaskIndex < 0)
        {
            nextTaskIndex = tasks.Length;
        }

        return tasks[previousMarkerIndex..nextTaskIndex];
    }

    private static string ExtractSection(string markdown, string heading, string nextHeading)
    {
        var headingIndex = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(headingIndex >= 0, $"Section heading was not found: {heading}");

        var nextHeadingIndex = markdown.IndexOf(nextHeading, headingIndex, StringComparison.Ordinal);
        Assert.True(nextHeadingIndex > headingIndex, $"Next heading was not found: {nextHeading}");

        return markdown[headingIndex..nextHeadingIndex];
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string requiredSuffix)
    {
        return Directory.EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Select(path => path.Replace('\\', '/'))
            .Where(path => path.EndsWith(requiredSuffix, StringComparison.Ordinal))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal))
            .Select(path => path[(RepositoryRoot.Length + 1)..]);
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string prefix, string requiredSuffix)
    {
        return EnumerateRepositoryFiles(requiredSuffix)
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal));
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
}
