using System.Text.RegularExpressions;
using Xunit;

namespace CmsSync.IntegrationTests.CI;

[Trait("Category", "CI")]
public sealed class CiWorkflowArtifactTests
{
    private const string CheckoutReference =
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1";
    private const string SetupDotnetReference =
        "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0";
    private const string UploadArtifactReference =
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1";

    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    [Fact]
    public void WorkflowExistsAndUsesTheRequiredNameAndTriggers()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");

        Assert.StartsWith("name: CI\n", workflow, StringComparison.Ordinal);
        Assert.Contains("pull_request:\n    branches:\n      - main", workflow, StringComparison.Ordinal);
        Assert.Contains("push:\n    branches:\n      - main\n      - \"feature/t016-*\"", workflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workflow_run", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repository_dispatch", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowUsesTheFixedRunnerAndAssertsDockerArchitectureEarly()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var checkoutIndex = workflow.IndexOf("uses: actions/checkout@", StringComparison.Ordinal);
        var architectureIndex = workflow.IndexOf("name: Assert x86-64 Docker host", StringComparison.Ordinal);
        var setupIndex = workflow.IndexOf("uses: actions/setup-dotnet@", StringComparison.Ordinal);

        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ubuntu-latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("self-hosted", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docker info --format '{{.Architecture}}'", workflow, StringComparison.Ordinal);
        Assert.Contains("amd64|x86_64", workflow, StringComparison.Ordinal);
        Assert.True(checkoutIndex < architectureIndex);
        Assert.True(architectureIndex < setupIndex);
        Assert.DoesNotContain("platform: linux/amd64", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowPermissionsAndCheckoutAreSafeForUntrustedPullRequests()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");

        Assert.Equal(1, Regex.Count(workflow, "(?m)^permissions:$"));
        Assert.Equal(1, Regex.Count(workflow, "(?m)^  contents: read$"));
        Assert.DoesNotMatch("(?im)^\\s*[A-Za-z-]+:\\s*write\\s*$", workflow);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ secrets.", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toJson(env", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toJson(secrets", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker inspect", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryActionIsOfficialImmutableAndReleaseAnnotated()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var actionLines = workflow
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("uses:", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();

        Assert.Equal(3, actionLines.Length);
        Assert.Contains($"uses: {CheckoutReference}", actionLines);
        Assert.Contains($"uses: {SetupDotnetReference}", actionLines);
        Assert.Contains($"uses: {UploadArtifactReference}", actionLines);
        Assert.All(
            actionLines,
            line => Assert.Matches(
                "^uses: actions/(checkout|setup-dotnet|upload-artifact)@[0-9a-f]{40} # v\\d+\\.\\d+\\.\\d+$",
                line));
    }

    [Fact]
    public void PolicyValidationPrecedesRestoreAndBuild()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var policyIndex = workflow.IndexOf("./scripts/validate-repository-policy.ps1", StringComparison.Ordinal);
        var restoreIndex = workflow.IndexOf("dotnet restore LateralChallenge.sln", StringComparison.Ordinal);
        var buildIndex = workflow.IndexOf("dotnet build LateralChallenge.sln", StringComparison.Ordinal);

        Assert.True(policyIndex >= 0);
        Assert.True(policyIndex < restoreIndex);
        Assert.True(restoreIndex < buildIndex);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", workflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseBuildAndFormattingAreBlockingGates()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");

        Assert.Contains("dotnet build LateralChallenge.sln", workflow, StringComparison.Ordinal);
        Assert.Contains("--configuration Release", workflow, StringComparison.Ordinal);
        Assert.Contains("--no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format LateralChallenge.sln", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-no-changes", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("|| true", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServerSmokeRunsBeforeSeparateUnitAndCompleteIntegrationSuites()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var sqlIndex = workflow.IndexOf("--filter \"Category=SqlServer\"", StringComparison.Ordinal);
        var unitIndex = workflow.IndexOf("CmsSync.UnitTests/CmsSync.UnitTests.csproj", StringComparison.Ordinal);
        var integrationIndex = workflow.LastIndexOf(
            "CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj",
            StringComparison.Ordinal);

        Assert.True(sqlIndex >= 0);
        Assert.True(sqlIndex < unitIndex);
        Assert.True(unitIndex < integrationIndex);
        Assert.Contains("sql-server-smoke.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("unit-tests.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("integration-tests.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/sql-server", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/unit", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/integration", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(workflow, "--collect \\\"XPlat Code Coverage\\\""));
    }

    [Fact]
    public void ComposeSmokeAndFinalCleanupReuseRepositoryScripts()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var integrationIndex = workflow.IndexOf("name: Run complete integration tests", StringComparison.Ordinal);
        var composeIndex = workflow.IndexOf("./scripts/validate-container-setup.ps1", StringComparison.Ordinal);
        var cleanupIndex = workflow.IndexOf("./scripts/verify-container-cleanup.ps1", StringComparison.Ordinal);

        Assert.True(integrationIndex < composeIndex);
        Assert.True(composeIndex < cleanupIndex);
        Assert.Contains("name: Verify repository and container cleanup\n        id: cleanup\n        if: always()", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactUploadAlwaysPublishesDistinctTrxAndCoberturaEvidence()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var uploadIndex = workflow.IndexOf("uses: actions/upload-artifact@", StringComparison.Ordinal);
        var uploadBlock = workflow[workflow.LastIndexOf("- name:", uploadIndex, StringComparison.Ordinal)..];
        uploadBlock = uploadBlock[..uploadBlock.IndexOf("\n      - name:", StringComparison.Ordinal)];

        Assert.Contains("if: always()", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("name: ci-test-evidence", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/**/*.trx", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("artifacts/test-results/**/coverage.cobertura.xml", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("retention-days: 14", uploadBlock, StringComparison.Ordinal);
        Assert.Contains("include-hidden-files: false", uploadBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("bin/", uploadBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obj/", uploadBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".env", uploadBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowBoundsExecutionCancelsObsoleteRunsAndWritesASafeSummary()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");

        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 30", workflow, StringComparison.Ordinal);
        Assert.Contains("DOTNET_NOLOGO: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("DOTNET_CLI_TELEMETRY_OPTOUT: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("DOTNET_SKIP_FIRST_TIME_EXPERIENCE: \"true\"", workflow, StringComparison.Ordinal);
        Assert.Contains("## CI quality gates", workflow, StringComparison.Ordinal);
        Assert.Contains("Artifact: ci-test-evidence", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyScriptUsesTrackedFilesAndDefinesRequiredSafeFindings()
    {
        var policyScript = ReadRepositoryFile("scripts/validate-repository-policy.ps1");
        var requiredRuleIdentifiers = new[]
        {
            "SEC001_TRACKED_ENV",
            "SEC002_TRACKED_SECRET_FILE",
            "SEC003_TRACKED_TEST_EVIDENCE",
            "SEC005_USABLE_AUTHORIZATION",
            "SEC006_COMMITTED_BASIC_CREDENTIAL",
            "SEC007_USABLE_CONNECTION_PASSWORD",
            "SEC008_USABLE_CONFIG_PASSWORD",
            "SEC009_USABLE_DOCKER_PASSWORD",
            "IMG002_EXPECTED_PIN_MISSING",
            "IMG003_PROHIBITED_CONTAINER_TARGET",
            "IMG004_DIFFERENT_OR_UNPINNED_SQL_IMAGE",
            "IMG005_MUTABLE_CONTAINER_DEFAULT",
            "WF001_PULL_REQUEST_TARGET",
            "WF002_SELF_HOSTED",
            "WF003_MUTABLE_RUNNER",
            "WF004_CONTINUE_ON_ERROR",
            "WF005_IGNORED_EXIT_CODE",
            "WF006_SECRET_INTERPOLATION",
            "WF007_ENVIRONMENT_DUMP",
            "WF009_BROAD_PERMISSION",
            "WF011_THIRD_PARTY_ACTION",
            "WF012_ACTION_NOT_PINNED",
            "WF013_ACTION_RELEASE_COMMENT_MISSING",
            "WF014_ARTIFACT_NOT_ALWAYS",
            "WF015_ARTIFACT_MISSING_EVIDENCE_ALLOWED",
        };

        Assert.Contains("git -C $repositoryRoot ls-files", policyScript, StringComparison.Ordinal);
        Assert.All(requiredRuleIdentifiers, rule => Assert.Contains(rule, policyScript, StringComparison.Ordinal));
        Assert.Contains("does not replace GitHub secret scanning", policyScript, StringComparison.Ordinal);
        Assert.Contains("LineNumber", policyScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $line", policyScript, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowAndPolicyKeepSqlServerImmutableAndExcludeLocalDb()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var policyScript = ReadRepositoryFile("scripts/validate-repository-policy.ps1");
        var sqlServerConstants = ReadRepositoryFile(
            "tests/CmsSync.IntegrationTests/Infrastructure/SqlServerTestConstants.cs");

        Assert.DoesNotContain("mssql/server:latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2022-latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalDB", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SqlServerTestConstants.cs", policyScript, StringComparison.Ordinal);
        Assert.Contains("2022-CU26-ubuntu-22.04@sha256:", sqlServerConstants, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowDoesNotReferenceLaterTaskArtifacts()
    {
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");
        var policyScript = ReadRepositoryFile("scripts/validate-repository-policy.ps1");

        Assert.DoesNotContain("README.md", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("README.md", policyScript, StringComparison.Ordinal);
        Assert.DoesNotContain("T017", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("T017", policyScript, StringComparison.Ordinal);
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
