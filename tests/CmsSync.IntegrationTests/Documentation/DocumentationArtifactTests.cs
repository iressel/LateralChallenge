using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            Assert.True(
                IsPathWithinRepository(RepositoryRoot, fullPath),
                $"README link target must resolve inside the repository: {rawTarget}");
            Assert.True(File.Exists(fullPath), $"README link target was not found: {rawTarget}");
        }
    }

    [Fact]
    public void RepositoryContainmentHelperAcceptsRootAndChildrenAndRejectsTraversal()
    {
        Assert.True(IsPathWithinRepository(RepositoryRoot, RepositoryRoot));

        var childPath = Path.Combine(RepositoryRoot, "README.md");
        Assert.True(IsPathWithinRepository(RepositoryRoot, childPath));

        var trimmedRepositoryRoot = RepositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var repositoryParent = Directory.GetParent(trimmedRepositoryRoot);
        Assert.NotNull(repositoryParent);

        var repositoryName = Path.GetFileName(trimmedRepositoryRoot);
        var siblingPath = Path.Combine(repositoryParent!.FullName, repositoryName + "-sibling", "README.md");
        Assert.False(IsPathWithinRepository(RepositoryRoot, siblingPath));

        var parentPath = Path.Combine(repositoryParent.FullName, "README.md");
        Assert.False(IsPathWithinRepository(RepositoryRoot, parentPath));

        if (OperatingSystem.IsWindows())
        {
            var alternateCasingRoot = FlipPathLetterCasing(trimmedRepositoryRoot);
            Assert.False(string.Equals(trimmedRepositoryRoot, alternateCasingRoot, StringComparison.Ordinal));

            var alternateCasingChild = Path.Combine(alternateCasingRoot, "README.md");
            Assert.True(IsPathWithinRepository(RepositoryRoot, alternateCasingChild));
        }
    }

    [Fact]
    public void ReadmeDocumentsConfigurationAndSqlPrincipalSeparation()
    {
        var readme = ReadRepositoryFile("README.md");

        Assert.Contains(
            "`ConnectionStrings__WriteDatabase`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`ConnectionStrings__ReadDatabase`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "CMS username length must be 10 through 20 characters.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "CMS, Consumer, and Administrator usernames are distinct.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "CMS, Consumer, and Administrator passwords are distinct GUID `D` format values.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "Basic Authentication requires HTTPS outside Development.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("Real credentials never belong in source control.", readme, StringComparison.Ordinal);

        Assert.Contains("`sa` is used only for local database initialization checks and setup.", readme, StringComparison.Ordinal);
        Assert.Contains("`CmsSyncMigration` is the migration principal", readme, StringComparison.Ordinal);
        Assert.Contains("`CmsSyncWriter` is the API write-context principal.", readme, StringComparison.Ordinal);
        Assert.Contains("`CmsSyncReader` is SELECT-only.", readme, StringComparison.Ordinal);
        Assert.Contains(
            "Normal API startup does not call `Database.Migrate`, `EnsureCreated`, or equivalent auto-migration behavior.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "Production migrations require a separately authorized migration principal.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "The API write identity must not receive migration permissions.",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeWebhookRequestAndResponseExamplesUseExactContracts()
    {
        var readme = ReadRepositoryFile("README.md");
        var requestJson = ExtractFencedBlock(
            readme,
            "### Example: raw webhook array request",
            "json");

        using var requestDocument = JsonDocument.Parse(requestJson);
        Assert.Equal(JsonValueKind.Array, requestDocument.RootElement.ValueKind);
        Assert.Equal(3, requestDocument.RootElement.GetArrayLength());

        var events = requestDocument.RootElement.EnumerateArray().ToArray();
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

        var responseJson = ExtractFencedBlock(
            readme,
            "### Example: webhook 200 OK batch response",
            "json");
        using var responseDocument = JsonDocument.Parse(responseJson);
        var response = responseDocument.RootElement;

        AssertPropertyNames(response, "batchId", "results", "summary");
        Assert.True(Guid.TryParse(response.GetProperty("batchId").GetString(), out _));

        var results = response.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal(0, results[0].GetProperty("sequence").GetInt32());
        Assert.Equal(1, results[1].GetProperty("sequence").GetInt32());

        AssertPropertyNames(results[0], "sequence", "eventId", "id", "outcome", "code", "generation", "resultingVersion");
        AssertPropertyNames(results[1], "sequence", "id", "outcome", "code");

        var summary = response.GetProperty("summary");
        AssertPropertyNames(
            summary,
            "total",
            "applied",
            "duplicate",
            "equivalent",
            "stale",
            "invalid",
            "conflict");
    }

    [Fact]
    public void ReadmeEntityAndAdministrativeExamplesUseExactTopLevelPropertyNames()
    {
        var readme = ReadRepositoryFile("README.md");

        var listJson = ExtractFencedBlock(readme, "### Example: entity list response", "json");
        using var listDocument = JsonDocument.Parse(listJson);
        var list = listDocument.RootElement;
        AssertPropertyNames(list, "items", "pageSize", "nextCursor");

        var listItem = list.GetProperty("items")[0];
        AssertPropertyNames(
            listItem,
            "id",
            "generation",
            "latestVersion",
            "payload",
            "cmsPublicationStatus",
            "currentVersionOccurredAtUtc",
            "entityEventHighWatermarkUtc",
            "administrativeDisabled");

        var detailJson = ExtractFencedBlock(readme, "### Example: entity detail response", "json");
        using var detailDocument = JsonDocument.Parse(detailJson);
        AssertPropertyNames(
            detailDocument.RootElement,
            "id",
            "generation",
            "latestVersion",
            "payload",
            "cmsPublicationStatus",
            "currentVersionOccurredAtUtc",
            "entityEventHighWatermarkUtc",
            "administrativeDisabled");

        var requestJson = ExtractFencedBlock(readme, "### Example: administrative-state request", "json");
        using var requestDocument = JsonDocument.Parse(requestJson);
        AssertPropertyNames(requestDocument.RootElement, "Disabled");
        Assert.Equal(JsonValueKind.True, requestDocument.RootElement.GetProperty("Disabled").ValueKind);

        var responseJson = ExtractFencedBlock(readme, "### Example: administrative-state response", "json");
        using var responseDocument = JsonDocument.Parse(responseJson);
        AssertPropertyNames(
            responseDocument.RootElement,
            "id",
            "administrativeDisabled",
            "administrativeStateChangedAtUtc",
            "administrativeStateChangedBy");
    }

    [Fact]
    public void ReadmeDocumentsEndpointAccessStatusRetrySafetyCiPlatformAndOpenQuestions()
    {
        var readme = ReadRepositoryFile("README.md");

        var requiredEndpoints = new[]
        {
            "POST /cms/events",
            "GET /api/entities",
            "GET /api/entities/{entityId}",
            "PUT /api/entities/{entityId}/administrative-state",
            "GET /health/live",
            "GET /health/ready",
        };

        foreach (var endpoint in requiredEndpoints)
        {
            Assert.Contains(endpoint, readme, StringComparison.Ordinal);
        }

        Assert.Contains("Basic realm=\"CmsBasic\"", readme, StringComparison.Ordinal);
        Assert.Contains("Basic realm=\"ConsumerBasic\"", readme, StringComparison.Ordinal);
        Assert.Contains("Normal consumer credentials return `403` without a challenge.", readme, StringComparison.Ordinal);
        Assert.Contains("non-disclosing `404`", readme, StringComparison.Ordinal);

        foreach (var status in new[] { "200", "400", "401", "403", "404", "413", "415", "500", "503" })
        {
            Assert.Contains($"| `{status}` |", readme, StringComparison.Ordinal);
        }

        Assert.Contains("Malformed JSON or invalid envelope for `POST /cms/events`", readme, StringComparison.Ordinal);
        Assert.Contains("`400`, `413`, and `415` webhook request-level failures perform no event processing.", readme, StringComparison.Ordinal);
        Assert.Contains("Retry the entire original request.", readme, StringComparison.Ordinal);
        Assert.Contains("Previously committed earlier items remain committed.", readme, StringComparison.Ordinal);
        Assert.Contains("Deterministic invalid/conflict items must not be retried unchanged.", readme, StringComparison.Ordinal);
        Assert.Contains("Do not retry only a guessed suffix of the batch.", readme, StringComparison.Ordinal);
        Assert.Contains("Cancellation does not undo already committed item transactions.", readme, StringComparison.Ordinal);

        Assert.Contains("A higher version wins even when its timestamp is older.", readme, StringComparison.Ordinal);
        Assert.Contains("`CurrentVersionOccurredAtUtc` may move backward.", readme, StringComparison.Ordinal);
        Assert.Contains("`EntityEventHighWatermarkUtc` never moves backward.", readme, StringComparison.Ordinal);
        Assert.Contains("Same-version payload is immutable.", readme, StringComparison.Ordinal);
        Assert.Contains("Same-version ordering uses `CurrentVersionOccurredAtUtc`.", readme, StringComparison.Ordinal);
        Assert.Contains("Delete ordering uses `EntityEventHighWatermarkUtc` only.", readme, StringComparison.Ordinal);
        Assert.Contains("Delete removes the active entity and payload-bearing revisions.", readme, StringComparison.Ordinal);
        Assert.Contains("Delete advances or creates the payload-free tombstone.", readme, StringComparison.Ordinal);
        Assert.Contains("A versioned event at or before the tombstone timestamp is stale.", readme, StringComparison.Ordinal);
        Assert.Contains("Recreation after the tombstone begins the next local generation.", readme, StringComparison.Ordinal);
        Assert.Contains("Recreation resets `AdministrativeDisabled` to `false`.", readme, StringComparison.Ordinal);
        Assert.Contains("Publish and unpublish preserve `AdministrativeDisabled`.", readme, StringComparison.Ordinal);
        Assert.Contains("Delete removes local administrative-state data with the entity.", readme, StringComparison.Ordinal);
        Assert.Contains("Deleted and unknown administrative updates return the same `404` shape.", readme, StringComparison.Ordinal);
        Assert.Contains("Repeating the current `Disabled` value does not rewrite audit fields or rowversion state.", readme, StringComparison.Ordinal);

        Assert.Contains("Entity responses use no-store protections.", readme, StringComparison.Ordinal);
        Assert.Contains("Authentication failures use no-store protections.", readme, StringComparison.Ordinal);
        Assert.Contains("Safe Problem Details responses do not expose stack traces or database details.", readme, StringComparison.Ordinal);
        Assert.Contains("`X-Correlation-ID` is accepted when safe or generated when absent/unsafe, and it is separate from `HttpContext.TraceIdentifier`.", readme, StringComparison.Ordinal);
        Assert.Contains("Structured processing logs do not contain raw payloads.", readme, StringComparison.Ordinal);
        Assert.Contains("Metrics use low-cardinality labels", readme, StringComparison.Ordinal);
        Assert.Contains("Authorization headers, decoded Basic credentials, secrets, connection strings, and raw payloads must not be logged.", readme, StringComparison.Ordinal);
        Assert.Contains("It does not claim an observability backend that is not configured here.", readme, StringComparison.Ordinal);

        Assert.Contains("`GET /health/live` is anonymous and does not query SQL.", readme, StringComparison.Ordinal);
        Assert.Contains("`GET /health/ready` is anonymous and checks both read and write SQL dependencies.", readme, StringComparison.Ordinal);
        Assert.Contains("do not expose provider names, exceptions, credentials, or connection details.", readme, StringComparison.Ordinal);

        Assert.Contains("pull requests targeting `main`", readme, StringComparison.Ordinal);
        Assert.Contains("pushes to `main`", readme, StringComparison.Ordinal);
        Assert.Contains("pushes to `feature/t016-*`", readme, StringComparison.Ordinal);
        Assert.Contains("`workflow_dispatch`", readme, StringComparison.Ordinal);
        Assert.Contains("`ubuntu-24.04` on x86-64", readme, StringComparison.Ordinal);
        Assert.Contains("`contents: read`", readme, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", readme, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68", readme, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", readme, StringComparison.Ordinal);
        Assert.Contains("repository-policy validation", readme, StringComparison.Ordinal);
        Assert.Contains("restore from NuGet.org", readme, StringComparison.Ordinal);
        Assert.Contains("Release build", readme, StringComparison.Ordinal);
        Assert.Contains("format verification", readme, StringComparison.Ordinal);
        Assert.Contains("SQL Server smoke tests", readme, StringComparison.Ordinal);
        Assert.Contains("TRX and Cobertura", readme, StringComparison.Ordinal);
        Assert.Contains("clean-volume Compose smoke", readme, StringComparison.Ordinal);
        Assert.Contains("cleanup verification", readme, StringComparison.Ordinal);
        Assert.Contains("artifact upload: `ci-test-evidence`", readme, StringComparison.Ordinal);
        Assert.Contains("does not replace GitHub secret scanning or a dedicated security product.", readme, StringComparison.Ordinal);

        Assert.Contains("Rosetta is unsupported for the SQL Server Linux container path.", readme, StringComparison.Ordinal);
        Assert.Contains("QEMU is unsupported for this local SQL Server container path.", readme, StringComparison.Ordinal);
        Assert.Contains("Other emulation or translation layers are unsupported.", readme, StringComparison.Ordinal);
        Assert.Contains("Do not add `platform: linux/amd64` as a workaround.", readme, StringComparison.Ordinal);
        Assert.Contains("Use a remote supported SQL Server instance or Azure SQL.", readme, StringComparison.Ordinal);
        Assert.Contains("Use a migration-authorized connection only for migration execution.", readme, StringComparison.Ordinal);
        Assert.Contains("Use a SELECT-only principal for `ConnectionStrings__ReadDatabase`.", readme, StringComparison.Ordinal);
        Assert.Contains("SQL Server Testcontainers are not the Apple Silicon local verification path.", readme, StringComparison.Ordinal);

        Assert.Contains("webhook raw-array shape and case-sensitive property names", readme, StringComparison.Ordinal);
        Assert.Contains("uncertainty around CMS `eventId` availability and uniqueness guarantees", readme, StringComparison.Ordinal);
        Assert.Contains("delete events have no CMS version, sequence, generation, or incarnation identifier", readme, StringComparison.Ordinal);
        Assert.Contains("timestamp precision, clock-skew, and timestamp-reuse risks remain external concerns", readme, StringComparison.Ordinal);
        Assert.Contains("credential provisioning and rotation remain operator concerns", readme, StringComparison.Ordinal);
        Assert.Contains("production migration-principal provisioning remains an operator concern", readme, StringComparison.Ordinal);
        Assert.Contains("production SELECT-only read-principal provisioning remains an operator concern", readme, StringComparison.Ordinal);
        Assert.Contains("local tombstone and generation behavior is deterministic but does not replace a future CMS incarnation protocol", readme, StringComparison.Ordinal);
        Assert.Contains("No production-integration readiness claim should be made until these external questions are confirmed.", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TasksFileShowsT017AndT018Completed()
    {
        var tasks = ReadRepositoryFile("specs/cms-event-ingestion/tasks.md");
        var t018Block = ExtractTaskBlock(tasks, "T018");

        Assert.Contains("- [x] **T017", tasks, StringComparison.Ordinal);
        Assert.Contains("- [x] **T018", tasks, StringComparison.Ordinal);
        Assert.DoesNotContain("- [ ] **T018", tasks, StringComparison.Ordinal);
        Assert.Contains("Completion evidence (", t018Block, StringComparison.Ordinal);
    }

    private static void AssertPropertyNames(JsonElement value, params string[] expectedPropertyNames)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        var propertyNames = value.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(expectedPropertyNames, propertyNames);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
    }

    private static bool IsPathWithinRepository(string repositoryRoot, string fullPath)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        if (string.Equals(relativePath, "..", StringComparison.Ordinal))
        {
            return false;
        }

        if (relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string FlipPathLetterCasing(string path)
    {
        var characters = path.ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            if (!char.IsLetter(character))
            {
                continue;
            }

            characters[index] = char.IsUpper(character)
                ? char.ToLowerInvariant(character)
                : char.ToUpperInvariant(character);
        }

        return new string(characters);
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

    private static string ExtractTaskBlock(string tasks, string taskId)
    {
        var marker = "- [";
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
}
