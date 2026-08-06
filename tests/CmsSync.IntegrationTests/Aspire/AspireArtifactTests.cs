using System.Text.RegularExpressions;
using Xunit;

namespace CmsSync.IntegrationTests.Aspire;

[Trait("Category", "Aspire")]
public sealed class AspireArtifactTests
{
    private const string ExpectedAspireVersion = "13.4.0";
    private const string ExpectedMessagePackVersion = "3.1.8";
    private const string ExpectedSqlImageTag = "2022-CU26-ubuntu-22.04";
    private const string ExpectedSqlImageSha256 = "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";
    private const string ExpectedSqlDataVolumeName = "cms-sync-aspire-sql-data";

    private static readonly string RepositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        [".git", "bin", "obj", "TestResults", "artifacts"],
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly string[] ExpectedSecretParameterNames =
    [
        "mssql-sa-password",
        "migration-sql-password",
        "write-sql-password",
        "read-sql-password",
        "cms-username",
        "cms-password",
        "consumer-username",
        "consumer-password",
        "administrator-username",
        "administrator-password",
    ];

    [Fact]
    public void AppHostExistsAndUsesPinnedAspireVersionsWithoutAuditDisable()
    {
        const string appHostPath = "apphost.cs";
        Assert.True(PathExistsInRepository(appHostPath), $"Missing file: {appHostPath}");

        var appHost = ReadRepositoryFile(appHostPath);

        var sdkVersion = ReadDirectiveVersion(
            appHost,
            @"(?m)^#:sdk\s+Aspire\.AppHost\.Sdk@([^\r\n]+)\s*$",
            "Aspire AppHost SDK directive");
        Assert.Equal(ExpectedAspireVersion, sdkVersion);
        Assert.Equal(
            1,
            Regex.Count(appHost, @"(?m)^#:sdk\s+Aspire\.AppHost\.Sdk@", RegexOptions.CultureInvariant));
        AssertStableDirectiveVersion(sdkVersion, "Aspire.AppHost.Sdk");

        var packageDirectiveMatches = Regex.Matches(
            appHost,
            @"(?m)^#:package\s+([A-Za-z0-9.\-]+)@([^\r\n]+)\s*$",
            RegexOptions.CultureInvariant);

        var packageDirectives = packageDirectiveMatches
            .Select(match => (
                Name: match.Groups[1].Value.Trim(),
                Version: match.Groups[2].Value.Trim()))
            .ToArray();

        var sqlHostingDirectives = packageDirectives
            .Where(directive => directive.Name.Equals("Aspire.Hosting.SqlServer", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(sqlHostingDirectives);
        Assert.Equal(ExpectedAspireVersion, sqlHostingDirectives[0].Version);

        foreach (var packageDirective in packageDirectives)
        {
            AssertStableDirectiveVersion(
                packageDirective.Version,
                $"package {packageDirective.Name}");
        }

        var messagePackDirectives = packageDirectives
            .Where(directive => directive.Name.Equals("MessagePack", StringComparison.Ordinal))
            .ToArray();
        Assert.True(messagePackDirectives.Length <= 1, "Expected at most one MessagePack package directive.");

        if (messagePackDirectives.Length == 1)
        {
            Assert.Equal(ExpectedMessagePackVersion, messagePackDirectives[0].Version);

            AssertExactSet(
                packageDirectives.Select(directive => directive.Name),
                ["Aspire.Hosting.SqlServer", "MessagePack"],
                "AppHost package directives");

            Assert.Contains("Security pin", appHost, StringComparison.Ordinal);
            Assert.Contains("NuGet Audit enabled", appHost, StringComparison.Ordinal);
            Assert.Contains("MessagePack 2.5.192", appHost, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("#:package MessagePack@", appHost, StringComparison.Ordinal);
            AssertExactSet(
                packageDirectives.Select(directive => directive.Name),
                ["Aspire.Hosting.SqlServer"],
                "AppHost package directives");
        }

        Assert.DoesNotContain("#:property NuGetAudit=false", appHost, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostUsesReferenceExpressionsWithoutPlaintextPasswordExtraction()
    {
        var appHost = ReadRepositoryFile("apphost.cs");

        Assert.Equal(2, CountOccurrences(appHost, "ReferenceExpression.Create("));
        Assert.Contains("var writeConnectionString = ReferenceExpression.Create(", appHost, StringComparison.Ordinal);
        Assert.Contains("var readConnectionString = ReferenceExpression.Create(", appHost, StringComparison.Ordinal);
        Assert.Contains("EndpointProperty.IPV4Host", appHost, StringComparison.Ordinal);
        Assert.Contains("EndpointProperty.Port", appHost, StringComparison.Ordinal);
        Assert.Contains(
            ".WithEnvironment(\"ConnectionStrings__WriteDatabase\", writeConnectionString)",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            ".WithEnvironment(\"ConnectionStrings__ReadDatabase\", readConnectionString)",
            appHost,
            StringComparison.Ordinal);

        Assert.DoesNotContain("writeSqlPasswordValue", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("readSqlPasswordValue", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredParameterValue", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.Configuration[\"Parameters:", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Password={writeSqlPasswordValue}", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Password={readSqlPasswordValue}", appHost, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostDeclaresExpectedResourcesDependenciesAndPinnedSqlArtifacts()
    {
        var appHost = ReadRepositoryFile("apphost.cs");

        Assert.Equal(1, CountOccurrences(appHost, "AddSqlServer(\"sql\""));
        Assert.Equal(1, CountOccurrences(appHost, "AddContainer(\"db-init\""));
        Assert.Equal(1, CountOccurrences(appHost, "AddDockerfile(\"migration\", \".\", \"Dockerfile\", \"migration\")"));
        Assert.Equal(1, CountOccurrences(appHost, "AddProject(\"api\", \"src/CmsSync.Api/CmsSync.Api.csproj\")"));

        Assert.Contains(".WaitFor(sql)", appHost, StringComparison.Ordinal);
        Assert.Contains(".WaitForCompletion(dbInit)", appHost, StringComparison.Ordinal);
        Assert.Contains(".WaitForCompletion(migration)", appHost, StringComparison.Ordinal);

        Assert.Contains("const string SqlServerImageTag = \"2022-CU26-ubuntu-22.04\";", appHost, StringComparison.Ordinal);
        Assert.Contains(
            "const string SqlServerImageSha256 = \"ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89\";",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains($"{ExpectedSqlImageTag}@sha256:{ExpectedSqlImageSha256}", appHost, StringComparison.Ordinal);
        Assert.Contains(".WithImage(SqlServerImageRepository, SqlServerImageTag)", appHost, StringComparison.Ordinal);
        Assert.Contains(".WithImageSHA256(SqlServerImageSha256)", appHost, StringComparison.Ordinal);

        Assert.Contains("const string DefaultSqlDataVolumeName = \"cms-sync-aspire-sql-data\";", appHost, StringComparison.Ordinal);
        Assert.Contains(".WithDataVolume(sqlDataVolumeName)", appHost, StringComparison.Ordinal);
        Assert.Contains(".WithLifetime(ContainerLifetime.Persistent)", appHost, StringComparison.Ordinal);

        Assert.Contains(
            ".WithBindMount(\"./scripts/container\", \"/opt/cms-sync\", isReadOnly: true)",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(".WithHttpHealthCheck(\"/health/ready\"", appHost, StringComparison.Ordinal);
    }

    [Fact]
    public void SolutionAndProjectsRemainWithinRequiredAspireArchitectureBoundaries()
    {
        var solution = ReadRepositoryFile("LateralChallenge.sln");
        var projectMatches = Regex.Matches(
            solution,
            "(?m)^Project\\(\\\"[^\\\"]+\\\"\\) = \\\"[^\\\"]+\\\", \\\"([^\\\"]+\\.csproj)\\\", \\\"[^\\\"]+\\\"",
            RegexOptions.CultureInvariant);
        var projectEntries = projectMatches
            .Select(match => NormalizePathSeparators(match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(6, projectEntries.Length);
        Assert.DoesNotContain(solution, "apphost.cs", StringComparison.OrdinalIgnoreCase);

        var csprojFiles = EnumerateRepositoryFiles(".csproj").ToArray();

        Assert.DoesNotContain(
            csprojFiles,
            path => path.EndsWith("CmsSync.AppHost.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            csprojFiles,
            path => path.Contains("ServiceDefaults", StringComparison.OrdinalIgnoreCase));

        var productionCsprojFiles = csprojFiles
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(4, productionCsprojFiles.Length);

        foreach (var productionCsprojFile in productionCsprojFiles)
        {
            var projectContent = ReadRepositoryFile(productionCsprojFile);
            Assert.DoesNotContain("Aspire.", projectContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppHostConnectionStringsAndSecretParametersStayWithinCredentialBoundaries()
    {
        var appHost = ReadRepositoryFile("apphost.cs");

        var secretParameterMatches = Regex.Matches(
            appHost,
            "builder\\.AddParameter\\(\\\"([^\\\"]+)\\\",\\s*secret:\\s*true\\)",
            RegexOptions.CultureInvariant);
        var secretParameterNames = secretParameterMatches
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(10, secretParameterNames.Length);
        AssertExactSet(
            secretParameterNames,
            ExpectedSecretParameterNames,
            "secret parameter names");

        var writeConnectionBlock = ExtractBlock(
            appHost,
            "var writeConnectionString = ReferenceExpression.Create(",
            "var readConnectionString = ReferenceExpression.Create(");
        var readConnectionBlock = ExtractBlock(
            appHost,
            "var readConnectionString = ReferenceExpression.Create(",
            "builder.AddProject(\"api\", \"src/CmsSync.Api/CmsSync.Api.csproj\")");

        Assert.Contains("User ID=CmsSyncWriter;", writeConnectionBlock, StringComparison.Ordinal);
        Assert.Contains("Password={writeSqlPassword};", writeConnectionBlock, StringComparison.Ordinal);

        Assert.Contains("User ID=CmsSyncReader;", readConnectionBlock, StringComparison.Ordinal);
        Assert.Contains("Password={readSqlPassword};", readConnectionBlock, StringComparison.Ordinal);
        Assert.Contains("ApplicationIntent=ReadOnly", readConnectionBlock, StringComparison.Ordinal);

        Assert.DoesNotContain("User ID=sa", writeConnectionBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User ID=sa", readConnectionBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CmsSyncMigration", writeConnectionBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CmsSyncMigration", readConnectionBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("mssqlSaPassword", writeConnectionBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("mssqlSaPassword", readConnectionBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationSqlPassword", writeConnectionBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationSqlPassword", readConnectionBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureScriptRequiresExplicitOrPromptedUsernamesAndProtectsSecrets()
    {
        var configureScript = ReadRepositoryFile("scripts/configure-aspire-local.ps1");

        Assert.DoesNotContain("cmssvc-local1", configureScript, StringComparison.Ordinal);
        Assert.DoesNotContain("consumer-local1", configureScript, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-local1", configureScript, StringComparison.Ordinal);

        Assert.Contains("[string] $CmsUsername = $null", configureScript, StringComparison.Ordinal);
        Assert.Contains("[string] $ConsumerUsername = $null", configureScript, StringComparison.Ordinal);
        Assert.Contains("[string] $AdministratorUsername = $null", configureScript, StringComparison.Ordinal);
        Assert.Contains("Resolve-RequiredUsername", configureScript, StringComparison.Ordinal);

        Assert.Contains(
            "Read-Host -Prompt \"Enter value for '$Name' (GUID D format)\" -AsSecureString",
            configureScript,
            StringComparison.Ordinal);
        Assert.Contains("ZeroFreeBSTR", configureScript, StringComparison.Ordinal);
        Assert.Contains("[Array]::Clear($plaintextCharacters, 0, $plaintextCharacters.Length)", configureScript, StringComparison.Ordinal);

        Assert.Contains("if (!$RotateSecrets)", configureScript, StringComparison.Ordinal);
        Assert.Contains("Get-ExistingParameterSecret -Name $Name", configureScript, StringComparison.Ordinal);
        Assert.Contains("Actor passwords must be distinct.", configureScript, StringComparison.Ordinal);

        var configureAspireResolutionBlock = ExtractBlock(
            configureScript,
            "$aspireCommand = Get-Command",
            "function Get-AspireVersion {");

        Assert.Contains("-Name \"aspire\"", configureAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("-CommandType Application", configureAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("-ErrorAction SilentlyContinue", configureAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("Select-Object -First 1", configureAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("throw \"The Aspire CLI is required but was not found in PATH.\"", configureAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("$aspireExecutable = $aspireCommand.Source", configureAspireResolutionBlock, StringComparison.Ordinal);

        Assert.DoesNotContain("& aspire", configureScript, StringComparison.Ordinal);

        var configureResolveIndex = configureScript.IndexOf(
            "$aspireExecutable = $aspireCommand.Source",
            StringComparison.Ordinal);
        var configureVersionCallIndex = configureScript.IndexOf(
            "$aspireVersion = Get-AspireVersion",
            StringComparison.Ordinal);
        var configureFirstAspireInvocationIndex = configureScript.IndexOf(
            "& $aspireExecutable",
            StringComparison.Ordinal);

        Assert.True(configureResolveIndex >= 0, "Configure script does not resolve Aspire executable.");
        Assert.True(configureVersionCallIndex > configureResolveIndex, "Configure script calls Get-AspireVersion before Aspire executable resolution.");
        Assert.True(configureFirstAspireInvocationIndex > configureResolveIndex, "Configure script invokes Aspire before executable resolution.");

        Assert.DoesNotContain("aspire secret path", configureScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Secrets file path:", configureScript, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            "Write-(Output|Host|Information|Verbose)\\s+.*\\$(mssqlSaPassword|migrationSqlPassword|writeSqlPassword|readSqlPassword|cmsPassword|consumerPassword|administratorPassword)",
            configureScript);
    }

    [Fact]
    public void StopAndValidateScriptsPreserveSafePersistentLifecycleBehavior()
    {
        const string stopScriptPath = "scripts/stop-aspire-local.ps1";
        const string validateScriptPath = "scripts/validate-aspire-setup.ps1";

        Assert.True(PathExistsInRepository(stopScriptPath), $"Missing file: {stopScriptPath}");
        Assert.True(PathExistsInRepository(validateScriptPath), $"Missing file: {validateScriptPath}");

        var stopScript = ReadRepositoryFile(stopScriptPath);
        var validateScript = ReadRepositoryFile(validateScriptPath);

        Assert.Contains("[switch] $RemoveData", stopScript, StringComparison.Ordinal);
        Assert.Contains("[string] $SqlDataVolumeName = \"cms-sync-aspire-sql-data\"", stopScript, StringComparison.Ordinal);
        Assert.Contains("/var/opt/mssql", stopScript, StringComparison.Ordinal);
        Assert.Contains("\"stop\", \"--apphost\"", stopScript, StringComparison.Ordinal);
        Assert.Contains("docker", stopScript, StringComparison.Ordinal);
        Assert.Contains("\"volume\", \"rm\", $SqlDataVolumeName", stopScript, StringComparison.Ordinal);
        Assert.Contains("verify SQL container removal", stopScript, StringComparison.Ordinal);
        Assert.Contains("if ($RemoveData)", stopScript, StringComparison.Ordinal);
        Assert.Contains("$requiredHostPorts = @(14333, 8080)", stopScript, StringComparison.Ordinal);

        Assert.DoesNotContain("docker system prune", stopScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--all", stopScript, StringComparison.Ordinal);

        Assert.Contains("$stopScriptPath = Join-Path $PSScriptRoot \"stop-aspire-local.ps1\"", validateScript, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(
                validateScript,
                "& $stopScriptPath -AppHostPath $appHostPath -SqlDataVolumeName $validationVolumeName -RemoveData"));

        Assert.Contains("\"--isolated\"", validateScript, StringComparison.Ordinal);
        Assert.Contains("Parameters__mssql-sa-password", validateScript, StringComparison.Ordinal);
        Assert.Contains("Parameters__migration-sql-password", validateScript, StringComparison.Ordinal);
        Assert.Contains("Parameters__write-sql-password", validateScript, StringComparison.Ordinal);
        Assert.Contains("Parameters__read-sql-password", validateScript, StringComparison.Ordinal);
        Assert.Contains("Aspire__SqlDataVolumeName", validateScript, StringComparison.Ordinal);

        Assert.DoesNotContain("docker volume rm $validationVolumeName", validateScript, StringComparison.Ordinal);
        Assert.DoesNotContain(ExpectedSqlDataVolumeName, validateScript, StringComparison.Ordinal);

        var validateAspireResolutionBlock = ExtractBlock(
            validateScript,
            "$aspireCommand = Get-Command",
            "function Get-AspireVersion {");

        Assert.Contains("-Name \"aspire\"", validateAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("-CommandType Application", validateAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("-ErrorAction SilentlyContinue", validateAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("Select-Object -First 1", validateAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("throw \"The Aspire CLI is required but was not found in PATH.\"", validateAspireResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("$aspireExecutable = $aspireCommand.Source", validateAspireResolutionBlock, StringComparison.Ordinal);

        Assert.Contains("& $aspireExecutable @Arguments", validateScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& aspire", validateScript, StringComparison.Ordinal);

        var validateResolveIndex = validateScript.IndexOf(
            "$aspireExecutable = $aspireCommand.Source",
            StringComparison.Ordinal);
        var validateFirstAspireInvocationIndex = validateScript.IndexOf(
            "& $aspireExecutable",
            StringComparison.Ordinal);

        Assert.True(validateResolveIndex >= 0, "Validate script does not resolve Aspire executable.");
        Assert.True(validateFirstAspireInvocationIndex > validateResolveIndex, "Validate script invokes Aspire before executable resolution.");

        var stopCommandResolutionBlock = ExtractBlock(
            stopScript,
            "$aspireCommand = Get-Command",
            "function Invoke-CheckedCapture {");

        Assert.Contains("-Name \"aspire\"", stopCommandResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("-Name \"docker\"", stopCommandResolutionBlock, StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Count(stopCommandResolutionBlock, "-CommandType\\s+Application", RegexOptions.CultureInvariant));
        Assert.Equal(
            2,
            Regex.Count(stopCommandResolutionBlock, "-ErrorAction\\s+SilentlyContinue", RegexOptions.CultureInvariant));
        Assert.Equal(
            2,
            Regex.Count(stopCommandResolutionBlock, "Select-Object\\s+-First\\s+1", RegexOptions.CultureInvariant));
        Assert.Contains("throw \"The Aspire CLI is required but was not found in PATH.\"", stopCommandResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("throw \"Docker is required but was not found in PATH.\"", stopCommandResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("$aspireExecutable = $aspireCommand.Source", stopCommandResolutionBlock, StringComparison.Ordinal);
        Assert.Contains("$dockerExecutable = $dockerCommand.Source", stopCommandResolutionBlock, StringComparison.Ordinal);

        Assert.Contains("-ExecutablePath $aspireExecutable", stopScript, StringComparison.Ordinal);
        Assert.Contains("-ExecutablePath $dockerExecutable", stopScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-Command \"aspire\"", stopScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-Command \"docker\"", stopScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& aspire", stopScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& docker", stopScript, StringComparison.Ordinal);

        var stopValidationCompleteIndex = stopScript.IndexOf(
            "$dockerExecutable = $dockerCommand.Source",
            StringComparison.Ordinal);
        var stopFirstAspireOrDockerOperationIndex = stopScript.IndexOf(
            "$appHostResourceNames = @(Try-GetAppHostResourceNames",
            StringComparison.Ordinal);

        Assert.True(stopValidationCompleteIndex >= 0, "Stop script does not resolve required command executables.");
        Assert.True(
            stopFirstAspireOrDockerOperationIndex > stopValidationCompleteIndex,
            "Stop script performs Aspire or Docker cleanup operations before command validation completes.");
    }

    [Fact]
    public void DocumentationDescribesNormalRunFlowAndSafeStopResetSemantics()
    {
        var readme = ReadRepositoryFile("README.md");
        var containerDoc = ReadRepositoryFile("docs/container-development.md");
        var aspireReadmeSection = ExtractBlock(
            readme,
            "### Optional local Aspire orchestration path",
            "## 6. Authentication and authorization contract");

        Assert.Equal(
            1,
            Regex.Count(
                readme,
                "### Optional local Aspire orchestration path",
                RegexOptions.CultureInvariant));
        Assert.DoesNotContain(
            "Optional local Aspire orchestration path (additional path; Compose remains supported):",
            readme,
            StringComparison.Ordinal);

        Assert.Contains("dotnet --version", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("docker version", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("aspire --version", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("aspire doctor", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Set-ExecutionPolicy -Scope Process Bypass", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("powershell.exe -NoProfile -ExecutionPolicy Bypass", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Process-scoped `Bypass` affects only the current terminal session.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("aspire certs trust", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\configure-aspire-local.ps1 -RotateSecrets", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("/login?t=...", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Do not run Compose and Aspire at the same time on ports `8080` and `14333`.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Expected lifecycle states:", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`sql`: Running and healthy.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`db-init`: Exited successfully.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`migration`: Exited successfully.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`api`: Running and healthy.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("http://localhost:8080/swagger", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Expected operation groups in Swagger UI:", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`CmsEntities`", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("`CmsEvents`", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("No operation group beginning with `#/components/tags/` should appear.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("aspire certs clean", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("dotnet dev-certs https --clean", aspireReadmeSection, StringComparison.Ordinal);

        var stopResetSection = ExtractBlock(
            aspireReadmeSection,
            "#### 8. Stop or reset the environment",
            "#### 9. Troubleshooting");

        Assert.Contains(".\\scripts\\stop-aspire-local.ps1", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\stop-aspire-local.ps1 -RemoveData", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains(
            "PowerShell 7 users may equivalently prefix these commands with `pwsh`.",
            aspireReadmeSection,
            StringComparison.Ordinal);
        Assert.Contains(
            "PowerShell 7 (`pwsh`) is recommended; Windows PowerShell is supported.",
            aspireReadmeSection,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                "(?m)^\\s*pwsh\\s+\\./scripts/stop-aspire-local\\.ps1(?:\\s+-RemoveData)?\\s*$",
                RegexOptions.CultureInvariant),
            stopResetSection);
        Assert.Contains("can remain running until `stop-aspire-local.ps1` removes the SQL container", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("Aspire remains optional and Compose remains independently supported.", aspireReadmeSection, StringComparison.Ordinal);
        Assert.Contains("No production deployment behavior changed and no seventh solution project was added.", aspireReadmeSection, StringComparison.Ordinal);

        Assert.DoesNotMatch(
            new Regex(@"/login\?t=(?!\.\.\.)[^\s)]+", RegexOptions.CultureInvariant),
            aspireReadmeSection);
        Assert.DoesNotMatch(
            new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant),
            aspireReadmeSection);
        Assert.DoesNotContain("C:\\", aspireReadmeSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", aspireReadmeSection, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "aspire start --apphost ./apphost.cs --isolated --non-interactive",
            aspireReadmeSection,
            StringComparison.Ordinal);

        Assert.Contains("aspire run --apphost ./apphost.cs", containerDoc, StringComparison.Ordinal);
        Assert.Contains("pwsh ./scripts/stop-aspire-local.ps1", containerDoc, StringComparison.Ordinal);
        Assert.Contains("pwsh ./scripts/stop-aspire-local.ps1 -RemoveData", containerDoc, StringComparison.Ordinal);
        Assert.Contains("aspire start --apphost ./apphost.cs --format Json", containerDoc, StringComparison.Ordinal);

        var deterministicValidationIndex = containerDoc.IndexOf("Deterministic validation:", StringComparison.Ordinal);
        Assert.True(deterministicValidationIndex > 0, "Deterministic validation section was not found in container-development.md.");

        var normalFlowSection = containerDoc[..deterministicValidationIndex];
        Assert.DoesNotContain("--isolated", normalFlowSection, StringComparison.Ordinal);
        Assert.Contains("aspire start --isolated --non-interactive", containerDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void ChallengeTasksRemainCheckedThroughT018WithNoT019()
    {
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");

        for (var index = 1; index <= 18; index++)
        {
            var taskId = $"T{index:000}";
            var taskBlock = ExtractTaskBlock(tasks, taskId);

            Assert.Contains($"- [x] **{taskId}", taskBlock, StringComparison.Ordinal);
            Assert.Contains("Completion evidence", taskBlock, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"- \[ \] \*\*T\d{3}\b", RegexOptions.CultureInvariant), tasks);
        Assert.DoesNotContain("T019", tasks, StringComparison.Ordinal);
    }

    private static string ReadDirectiveVersion(string content, string pattern, string directiveName)
    {
        var matches = Regex.Matches(content, pattern, RegexOptions.CultureInvariant);
        Assert.True(matches.Count == 1, $"Expected exactly one {directiveName}.");

        return matches[0].Groups[1].Value.Trim();
    }

    private static int CountOccurrences(string content, string fragment)
    {
        var count = 0;
        var index = 0;

        while (index < content.Length)
        {
            var next = content.IndexOf(fragment, index, StringComparison.Ordinal);
            if (next < 0)
            {
                break;
            }

            count++;
            index = next + fragment.Length;
        }

        return count;
    }

    private static void AssertStableDirectiveVersion(string version, string directiveName)
    {
        Assert.DoesNotContain("*", version, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant),
            $"Directive '{directiveName}' must use an exact stable version with no wildcard or prerelease suffix.");
    }

    private static void AssertExactSet(
        IEnumerable<string> actualValues,
        IEnumerable<string> expectedValues,
        string label)
    {
        var actual = actualValues
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = expectedValues
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Unexpected {label}. Expected '{string.Join(", ", expected)}' but found '{string.Join(", ", actual)}'.");
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, actual.Length);
    }

    private static string ExtractBlock(string content, string startMarker, string endMarker)
    {
        var startIndex = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {startMarker}");

        var endIndex = content.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found: {endMarker}");

        return content[startIndex..endIndex];
    }

    private static string ExtractTaskBlock(string tasks, string taskId)
    {
        var taskHeader = $"**{taskId} ";
        var startIndex = tasks.IndexOf(taskHeader, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Task block was not found for {taskId}.");

        var taskLineStartIndex = tasks.LastIndexOf("- [", startIndex, StringComparison.Ordinal);
        Assert.True(taskLineStartIndex >= 0, $"Task line start was not found for {taskId}.");

        var nextTaskIndex = tasks.IndexOf("\n- [", startIndex, StringComparison.Ordinal);
        if (nextTaskIndex < 0)
        {
            nextTaskIndex = tasks.Length;
        }

        return tasks[taskLineStartIndex..nextTaskIndex];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var fullPath = GetContainedRepositoryPath(relativePath);
        return File.ReadAllText(fullPath);
    }

    private static bool PathExistsInRepository(string relativePath)
    {
        var fullPath = GetContainedRepositoryPath(relativePath);
        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string requiredSuffix)
    {
        return EnumerateRepositoryFilesRecursive(RepositoryRoot, RepositoryRoot)
            .Where(path => path.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase));
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
            if (ExcludedDirectoryNames.Contains(childDirectory.Name))
            {
                continue;
            }

            foreach (var relativePath in EnumerateRepositoryFilesRecursive(repositoryRoot, childDirectory.FullName))
            {
                yield return relativePath;
            }
        }
    }

    private static string GetContainedRepositoryPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Repository path cannot be empty.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException($"Rooted repository-relative path is not allowed: {relativePath}");
        }

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(RepositoryRoot, normalizedRelativePath));

        if (!IsPathContainedInRepository(RepositoryRoot, fullPath))
        {
            throw new InvalidOperationException($"Path is outside repository boundaries: {relativePath}");
        }

        return fullPath;
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

    private static string NormalizePathSeparators(string path)
    {
        return path.Replace('\\', '/');
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
