using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CmsSync.IntegrationTests.FinalReview;

[Trait("Category", "FinalReview")]
public sealed class FinalReviewArtifactTests
{
    private const string ExpectedSqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";

    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    private static readonly string[] ExpectedApplicationTables =
    [
        "CmsEntities",
        "CmsEntityRevisions",
        "CmsDeletionTombstones",
        "CmsEventProcessingLogs",
    ];

    private static readonly string[] RepositoryFileExtensions =
    [
        ".cs",
        ".csproj",
        ".json",
        ".md",
        ".props",
        ".ps1",
        ".sh",
        ".sln",
        ".sql",
        ".yaml",
        ".yml",
    ];

    private static readonly string[] ExpectedIncludedSourceFiles =
    [
        "Included.cs",
        "src/Nested.cs",
    ];

    private static readonly string[] ExpectedNestedSourceFiles =
    [
        "src/Nested.cs",
    ];

    private static readonly HashSet<string> ExcludedRepositoryDirectoryNames = new(
        [".git", "bin", "obj"],
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly (string ApiName, string Pattern)[] ProhibitedAutoMigrationInvocationPatterns =
    [
        ("Database.Migrate(", @"\bDatabase\s*\.\s*Migrate\s*\("),
        ("Database.MigrateAsync(", @"\bDatabase\s*\.\s*MigrateAsync\s*\("),
        (".Migrate(", @"\.\s*Migrate\s*\("),
        (".MigrateAsync(", @"\.\s*MigrateAsync\s*\("),
        ("EnsureCreated(", @"\bEnsureCreated\s*\("),
        ("EnsureCreatedAsync(", @"\bEnsureCreatedAsync\s*\("),
    ];

    private static readonly Regex RequirementIdentifierPattern = new(
        @"\b(FR-\d{3}|NFR-\d{3}|SEC-\d{3})\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderEvidencePattern = new(
        @"\b(TBD|N/?A|PENDING|TODO)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BacktickTokenPattern = new(
        "`([^`]+)`",
        RegexOptions.CultureInvariant);

    private static readonly Regex TaskReferenceOnlyPattern = new(
        @"^(?:`?T\d{3}`?(?:\s*,\s*)?)+$",
        RegexOptions.CultureInvariant);

    private static readonly Regex VerificationTestEvidencePattern = new(
        "`[^`]+\\.cs`\\s*::\\s*`[A-Za-z0-9_]+\\.[A-Za-z0-9_]+`",
        RegexOptions.CultureInvariant);

    private static readonly Regex VerificationScriptEvidencePattern = new(
        "`[^`]+\\.ps1`\\s*::\\s*`[A-Za-z0-9_]+`",
        RegexOptions.CultureInvariant);

    private static readonly Regex VerificationCiGatePattern = new(
        "`\\.github/workflows/ci\\.yml`\\s*::\\s*`[A-Za-z0-9_./-]+`",
        RegexOptions.CultureInvariant);

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

        var requirementRows = ParseMarkdownTable(
            requirementTraceability,
            expectedColumns: 3,
            tableName: "Section 18 requirement traceability");
        var requirementIdentifiers = requirementRows
            .Select(row => row[0])
            .ToArray();

        AssertExactUniqueSet(
            requirementIdentifiers,
            ExpectedIdentifiers("FR", 1, 30)
                .Concat(ExpectedIdentifiers("NFR", 1, 8))
                .Concat(ExpectedIdentifiers("SEC", 1, 4))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            "Section 18 requirement identifiers");

        var acceptanceRows = ParseMarkdownTable(
            acceptanceTraceability,
            expectedColumns: 4,
            tableName: "Section 19 acceptance-criterion traceability");

        Assert.Equal(57, acceptanceRows.Count);

        var acceptanceCriteria = acceptanceRows
            .Select(row => row[0])
            .ToArray();

        AssertExactUniqueSet(
            acceptanceCriteria,
            ExpectedIdentifiers("AC", 1, 57),
            "Section 19 acceptance criteria");

        foreach (var row in acceptanceRows)
        {
            var criterion = row[0];
            var requirementEvidence = row[1];
            var implementationEvidence = row[2];
            var verificationEvidence = row[3];

            var requirementIdentifiersInRow = RequirementIdentifierPattern.Matches(requirementEvidence)
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.NotEmpty(requirementIdentifiersInRow);
            Assert.True(
                requirementIdentifiersInRow.Length == requirementIdentifiersInRow.Distinct(StringComparer.Ordinal).Count(),
                $"{criterion} repeats a requirement identifier in the same row.");

            Assert.False(
                string.IsNullOrWhiteSpace(implementationEvidence),
                $"{criterion} has empty implementation evidence.");
            Assert.False(
                string.IsNullOrWhiteSpace(verificationEvidence),
                $"{criterion} has empty verification evidence.");

            Assert.False(
                IsTaskReferenceOnly(implementationEvidence),
                $"{criterion} implementation evidence relies only on task references.");
            Assert.False(
                IsTaskReferenceOnly(verificationEvidence),
                $"{criterion} verification evidence relies only on task references.");

            Assert.False(
                ContainsPlaceholderTerms(implementationEvidence),
                $"{criterion} implementation evidence contains placeholder text.");
            Assert.False(
                ContainsPlaceholderTerms(verificationEvidence),
                $"{criterion} verification evidence contains placeholder text.");

            var implementationPaths = ExtractRepositoryRelativePaths(implementationEvidence).ToArray();
            var verificationPaths = ExtractRepositoryRelativePaths(verificationEvidence).ToArray();
            var referencedPaths = implementationPaths
                .Concat(verificationPaths)
                .ToArray();

            Assert.NotEmpty(referencedPaths);
            Assert.NotEmpty(verificationPaths);

            foreach (var referencedPath in referencedPaths)
            {
                Assert.True(
                    PathExistsInRepository(referencedPath),
                    $"{criterion} references a missing repository path: {referencedPath}");
            }

            Assert.True(
                HasVerificationEvidence(verificationEvidence),
                $"{criterion} verification evidence must identify a test method, script check, or CI gate.");
        }
    }

    [Fact]
    public void ExactUniqueSetAssertionRejectsDuplicateRawValuesEvenWhenDistinctProjectionWouldMatch()
    {
        var rawValues = new[]
        {
            "CmsEntities",
            "CmsEntities",
            "CmsEntityRevisions",
            "CmsDeletionTombstones",
            "CmsEventProcessingLogs",
        };

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertExactUniqueSet(rawValues, ExpectedApplicationTables, "duplicate-regression"));
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
        Assert.DoesNotContain("- [x] **T018", t018Block, StringComparison.Ordinal);
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

        var migrationTables = Regex.Matches(migration, "CreateTable\\(\\s*name:\\s*\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        var snapshotTables = Regex.Matches(snapshot, "\\.ToTable\\(\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        AssertExactUniqueSet(
            migrationTables,
            ExpectedApplicationTables,
            "migration CreateTable declarations");
        AssertExactUniqueSet(
            snapshotTables,
            ExpectedApplicationTables,
            "model-snapshot ToTable declarations");

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
        var productionCodePaths = EnumerateRepositoryFiles("src/", ".cs")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var productionCodeByPath = productionCodePaths.ToDictionary(
            path => path,
            ReadRepositoryFile,
            StringComparer.Ordinal);

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

        foreach (var text in productionCodeByPath
                     .Where(pair => pair.Key.StartsWith("src/CmsSync.Domain/", StringComparison.Ordinal))
                     .Select(pair => pair.Value))
        {
            Assert.DoesNotContain("using Microsoft.EntityFrameworkCore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("using Microsoft.AspNetCore", text, StringComparison.Ordinal);
        }

        var autoMigrationViolations = new List<string>();

        foreach (var (relativePath, sourceText) in productionCodeByPath)
        {
            var prohibitedApis = FindProhibitedAutoMigrationInvocations(relativePath, sourceText);

            foreach (var apiName in prohibitedApis)
            {
                autoMigrationViolations.Add($"{relativePath} :: {apiName}");
            }
        }

        Assert.True(
            autoMigrationViolations.Count == 0,
            string.Join(Environment.NewLine, autoMigrationViolations));

        foreach (var startupFile in new[] { "src/CmsSync.Api/Program.cs", "src/CmsSync.Infrastructure/DependencyInjection.cs" })
        {
            var startupViolations = FindProhibitedAutoMigrationInvocations(
                startupFile,
                productionCodeByPath[startupFile]);

            Assert.True(
                startupViolations.Length == 0,
                string.Join(", ", startupViolations.Select(apiName => $"{startupFile} :: {apiName}")));
        }
    }

    [Fact]
    public void AutoMigrationInvocationScannerRejectsEnsureCreatedInArbitrarySourceAndIgnoresGeneratedMigrationDeclarations()
    {
        const string ensureCreatedSource =
            "namespace Example; public sealed class Worker { public void Run() { db.Database.EnsureCreated(); } }";
        const string ensureCreatedAsyncSource =
            "namespace Example; public sealed class Worker { public async Task RunAsync() { await db.Database.EnsureCreatedAsync(); } }";
        const string safeSource =
            "namespace Example; public sealed class Worker { public void Run() { _ = DateTime.UtcNow; } }";
        const string generatedMigrationDeclaration =
            "using Microsoft.EntityFrameworkCore.Migrations; public partial class InitialCmsPersistence : Migration { protected override void Up(MigrationBuilder migrationBuilder) { migrationBuilder.CreateTable(name: \"CmsEntities\", columns: table => new { }); } }";

        var ensureCreatedMatches = FindProhibitedAutoMigrationInvocations(
            "src/CmsSync.Application/EventIngestion/ArbitraryWorker.cs",
            ensureCreatedSource);
        var ensureCreatedAsyncMatches = FindProhibitedAutoMigrationInvocations(
            "src/CmsSync.Application/EventIngestion/ArbitraryWorker.cs",
            ensureCreatedAsyncSource);
        var safeMatches = FindProhibitedAutoMigrationInvocations(
            "src/CmsSync.Application/EventIngestion/ArbitraryWorker.cs",
            safeSource);
        var migrationDeclarationMatches = FindProhibitedAutoMigrationInvocations(
            "src/CmsSync.Infrastructure/Persistence/Migrations/20260802142305_InitialCmsPersistence.cs",
            generatedMigrationDeclaration);

        Assert.Contains("EnsureCreated(", ensureCreatedMatches);
        Assert.Contains("EnsureCreatedAsync(", ensureCreatedAsyncMatches);
        Assert.Empty(safeMatches);
        Assert.Empty(migrationDeclarationMatches);
    }

    [Fact]
    public void RepositoryFileEnumerationSkipsExcludedDirectoriesBeforeDescentAndRemainsDeterministic()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"FinalReviewArtifactTests-{Guid.NewGuid():N}");
        var cleanupExecuted = false;

        try
        {
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "src"));
            Directory.CreateDirectory(Path.Combine(temporaryRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "src", "bin"));
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "src", "obj"));

            File.WriteAllText(Path.Combine(temporaryRoot, "Included.cs"), "class Included {}\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "src", "Nested.cs"), "class Nested {}\n");
            File.WriteAllText(Path.Combine(temporaryRoot, ".git", "Hidden.cs"), "class Hidden {}\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "src", "bin", "Generated.cs"), "class Generated {}\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "src", "obj", "Generated.cs"), "class Generated {}\n");

            var firstRun = EnumerateRepositoryFilesFromRoot(temporaryRoot, ".cs").ToArray();
            var secondRun = EnumerateRepositoryFilesFromRoot(temporaryRoot, ".cs").ToArray();
            var prefixedRun = EnumerateRepositoryFilesFromRoot(temporaryRoot, "src/", ".cs").ToArray();

            Assert.Equal(ExpectedIncludedSourceFiles, firstRun);
            Assert.Equal(firstRun, secondRun);
            Assert.Equal(ExpectedNestedSourceFiles, prefixedRun);
            Assert.DoesNotContain(firstRun, path => path.StartsWith(".git/", StringComparison.Ordinal));
            Assert.DoesNotContain(firstRun, path => path.Contains("/bin/", StringComparison.Ordinal));
            Assert.DoesNotContain(firstRun, path => path.Contains("/obj/", StringComparison.Ordinal));
        }
        finally
        {
            cleanupExecuted = true;

            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }

        Assert.True(cleanupExecuted);
    }

    [Fact]
    public void RepositoryContainmentAndTokenValidationRejectTraversalAndSharedPrefixSiblings()
    {
        var parentDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FinalReviewContainmentTests-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(parentDirectory, "LateralChallenge");
        var siblingDirectory = Path.Combine(parentDirectory, "LateralChallenge-sibling");
        var cleanupExecuted = false;

        try
        {
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "docs", "nested"));
            Directory.CreateDirectory(siblingDirectory);

            File.WriteAllText(Path.Combine(repositoryRoot, "README.md"), "readme\n");
            File.WriteAllText(Path.Combine(repositoryRoot, "docs", "nested", "Guide.md"), "guide\n");
            File.WriteAllText(Path.Combine(repositoryRoot, "docs", "documentation..archive.md"), "archive\n");
            File.WriteAllText(Path.Combine(siblingDirectory, "Sibling.md"), "outside\n");

            Assert.True(IsPathContainedInRepository(repositoryRoot, repositoryRoot));
            Assert.True(IsPathContainedInRepository(repositoryRoot, Path.Combine(repositoryRoot, "README.md")));
            Assert.True(IsPathContainedInRepository(repositoryRoot, Path.Combine(repositoryRoot, "docs", "nested", "Guide.md")));
            Assert.False(IsPathContainedInRepository(repositoryRoot, parentDirectory));
            Assert.False(IsPathContainedInRepository(repositoryRoot, Path.Combine(siblingDirectory, "Sibling.md")));

            Assert.True(PathExistsInRepository(repositoryRoot, "README.md"));
            Assert.True(PathExistsInRepository(repositoryRoot, "docs/nested/Guide.md"));
            Assert.False(PathExistsInRepository(repositoryRoot, "docs/../../LateralChallenge-sibling/Sibling.md"));
            Assert.False(PathExistsInRepository(repositoryRoot, Path.Combine(siblingDirectory, "Sibling.md")));

            Assert.False(LooksLikeRepositoryPathToken("docs/../../LateralChallenge-sibling/Sibling.md"));
            Assert.False(LooksLikeRepositoryPathToken("docs\\..\\..\\LateralChallenge-sibling\\Sibling.md"));
            Assert.True(LooksLikeRepositoryPathToken("docs/documentation..archive.md"));
            Assert.True(PathExistsInRepository(repositoryRoot, "docs/documentation..archive.md"));

            if (OperatingSystem.IsWindows())
            {
                var alternateCaseRepositoryRoot = InvertLetterCasing(repositoryRoot);
                var alternateCaseChildPath = Path.Combine(alternateCaseRepositoryRoot, "docs", "nested", "Guide.md");

                Assert.True(IsPathContainedInRepository(repositoryRoot, alternateCaseRepositoryRoot));
                Assert.True(IsPathContainedInRepository(repositoryRoot, alternateCaseChildPath));
            }
        }
        finally
        {
            cleanupExecuted = true;

            if (Directory.Exists(parentDirectory))
            {
                Directory.Delete(parentDirectory, recursive: true);
            }
        }

        Assert.True(cleanupExecuted);
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

    private static void AssertExactUniqueSet(
        string[] rawValues,
        string[] expectedValues,
        string collectionName)
    {
        var duplicates = rawValues
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            $"{collectionName} contains duplicates: {string.Join(", ", duplicates)}");

        Assert.Equal(expectedValues.Length, rawValues.Length);

        var actualSet = new HashSet<string>(rawValues, StringComparer.Ordinal);
        var expectedSet = new HashSet<string>(expectedValues, StringComparer.Ordinal);

        var missing = expectedSet
            .Where(expected => !actualSet.Contains(expected))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unexpected = actualSet
            .Where(actual => !expectedSet.Contains(actual))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{collectionName} is missing: {string.Join(", ", missing)}");
        Assert.True(
            unexpected.Length == 0,
            $"{collectionName} has unexpected values: {string.Join(", ", unexpected)}");
    }

    private static List<string[]> ParseMarkdownTable(
        string markdownSection,
        int expectedColumns,
        string tableName)
    {
        var tableLines = markdownSection
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.TrimStart().StartsWith('|'))
            .ToArray();

        Assert.True(
            tableLines.Length >= 2,
            $"{tableName} must include a header and a separator row.");

        _ = ParseMarkdownRow(tableLines[0], expectedColumns, tableName, 1);
        var separatorRow = ParseMarkdownRow(tableLines[1], expectedColumns, tableName, 2);

        Assert.All(
            separatorRow,
            cell => Assert.True(
                IsMarkdownSeparatorCell(cell),
                $"{tableName} has an invalid separator cell: {cell}"));

        var rows = new List<string[]>(Math.Max(0, tableLines.Length - 2));

        for (var index = 2; index < tableLines.Length; index++)
        {
            var row = ParseMarkdownRow(tableLines[index], expectedColumns, tableName, index + 1);

            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string[] ParseMarkdownRow(
        string line,
        int expectedColumns,
        string tableName,
        int lineNumber)
    {
        var trimmedLine = line.Trim();

        Assert.True(
            trimmedLine.StartsWith('|') &&
            trimmedLine.EndsWith('|'),
            $"{tableName} line {lineNumber} is not a valid Markdown row.");

        var cells = trimmedLine[1..^1]
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();

        Assert.Equal(expectedColumns, cells.Length);
        return cells;
    }

    private static bool IsMarkdownSeparatorCell(string cell)
    {
        var separator = cell.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (separator.Length == 0)
        {
            return false;
        }

        return separator.All(character => character is '-' or ':');
    }

    private static bool IsTaskReferenceOnly(string evidenceCell)
    {
        return TaskReferenceOnlyPattern.IsMatch(evidenceCell.Trim());
    }

    private static bool ContainsPlaceholderTerms(string evidenceCell)
    {
        return PlaceholderEvidencePattern.IsMatch(evidenceCell);
    }

    private static bool HasVerificationEvidence(string verificationCell)
    {
        return VerificationTestEvidencePattern.IsMatch(verificationCell) ||
               VerificationScriptEvidencePattern.IsMatch(verificationCell) ||
               VerificationCiGatePattern.IsMatch(verificationCell);
    }

    private static IEnumerable<string> ExtractRepositoryRelativePaths(string evidenceCell)
    {
        foreach (Match match in BacktickTokenPattern.Matches(evidenceCell))
        {
            var token = match.Groups[1].Value.Trim();

            if (!LooksLikeRepositoryPathToken(token))
            {
                continue;
            }

            yield return token.Replace('\\', '/');
        }
    }

    private static bool LooksLikeRepositoryPathToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Contains("://", StringComparison.Ordinal) ||
            Path.IsPathRooted(token) ||
            ContainsParentTraversalSegment(token))
        {
            return false;
        }

        var normalizedToken = NormalizePathSeparators(token);

        if (normalizedToken.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        return RepositoryFileExtensions.Any(extension =>
            token.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathExistsInRepository(string relativePath)
    {
        return PathExistsInRepository(RepositoryRoot, relativePath);
    }

    private static bool PathExistsInRepository(string repositoryRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalizedRelativePath));

        if (!IsPathContainedInRepository(repositoryRoot, fullPath))
        {
            return false;
        }

        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private static string[] FindProhibitedAutoMigrationInvocations(
        string relativePath,
        string sourceText)
    {
        if (IsGeneratedEfMigrationFile(relativePath))
        {
            return [];
        }

        var sourceWithoutComments = StripComments(sourceText);
        var detectedApis = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (apiName, pattern) in ProhibitedAutoMigrationInvocationPatterns)
        {
            if (Regex.IsMatch(sourceWithoutComments, pattern, RegexOptions.CultureInvariant))
            {
                detectedApis.Add(apiName);
            }
        }

        return detectedApis
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string StripComments(string sourceText)
    {
        var withoutBlockComments = Regex.Replace(
            sourceText,
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var lines = withoutBlockComments.Split('\n');
        var builder = new StringBuilder(withoutBlockComments.Length);

        foreach (var line in lines)
        {
            var commentStart = line.IndexOf("//", StringComparison.Ordinal);
            var safeLine = commentStart >= 0 ? line[..commentStart] : line;
            builder.AppendLine(safeLine);
        }

        return builder.ToString();
    }

    private static bool IsGeneratedEfMigrationFile(string relativePath)
    {
        const string migrationPrefix = "src/CmsSync.Infrastructure/Persistence/Migrations/";

        if (!relativePath.StartsWith(migrationPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = Path.GetFileName(relativePath);

        if (fileName.EndsWith(".Designer.cs", StringComparison.Ordinal) ||
            string.Equals(
                fileName,
                "CmsWriteDbContextModelSnapshot.cs",
                StringComparison.Ordinal))
        {
            return true;
        }

        return Regex.IsMatch(
            fileName,
            @"^\d{14}_.+\.cs$",
            RegexOptions.CultureInvariant);
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

    private static bool ContainsParentTraversalSegment(string token)
    {
        var normalizedToken = NormalizePathSeparators(token);
        var segments = normalizedToken.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool IsPathContainedInRepository(string repositoryRoot, string candidateFullPath)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, candidateFullPath);

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        if (string.Equals(relativePath, "..", StringComparison.Ordinal))
        {
            return false;
        }

        return !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string InvertLetterCasing(string value)
    {
        var characters = value.ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];

            if (char.IsLetter(character))
            {
                characters[index] = char.IsUpper(character)
                    ? char.ToLowerInvariant(character)
                    : char.ToUpperInvariant(character);
            }
        }

        return new string(characters);
    }

    private static string NormalizePathSeparators(string path)
    {
        return path.Replace('\\', '/');
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
        return EnumerateRepositoryFilesFromRoot(RepositoryRoot, requiredSuffix);
    }

    private static IEnumerable<string> EnumerateRepositoryFilesFromRoot(
        string repositoryRoot,
        string requiredSuffix)
    {
        return EnumerateRepositoryFilesRecursive(repositoryRoot, repositoryRoot)
            .Where(path => path.EndsWith(requiredSuffix, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateRepositoryFilesRecursive(
        string repositoryRoot,
        string currentDirectory)
    {
        var files = Directory.EnumerateFiles(currentDirectory)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, filePath);
            yield return NormalizePathSeparators(relativePath);
        }

        var childDirectories = Directory.EnumerateDirectories(currentDirectory)
            .Select(path => new DirectoryInfo(path))
            .OrderBy(directory => directory.Name, StringComparer.Ordinal)
            .ThenBy(directory => directory.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var childDirectory in childDirectories)
        {
            if (ExcludedRepositoryDirectoryNames.Contains(childDirectory.Name))
            {
                continue;
            }

            foreach (var relativePath in EnumerateRepositoryFilesRecursive(repositoryRoot, childDirectory.FullName))
            {
                yield return relativePath;
            }
        }
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string prefix, string requiredSuffix)
    {
        return EnumerateRepositoryFilesFromRoot(RepositoryRoot, prefix, requiredSuffix);
    }

    private static IEnumerable<string> EnumerateRepositoryFilesFromRoot(
        string repositoryRoot,
        string prefix,
        string requiredSuffix)
    {
        return EnumerateRepositoryFilesFromRoot(repositoryRoot, requiredSuffix)
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
