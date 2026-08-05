using System.Reflection;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Extensions.Options;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
[Trait("Category", "Security")]
public sealed class StartupCredentialValidationTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task ProductionStartupFailsWhenCredentialSectionIsAbsent()
    {
        await AssertProductionStartupFailsAsync(
            credentialOverrides: null,
            includeCredentials: false,
            sensitiveValues: []);
    }

    [Fact]
    public async Task ProductionStartupFailsWhenOneIdentityIsMissing()
    {
        var credentials = TestCredentialSet.Create();
        var configuration = credentials.CreateConfiguration()
            .Where(entry => !entry.Key.Contains(":Cms:", StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        await AssertProductionStartupFailsAsync(
            configuration,
            includeCredentials: false,
            [credentials.CmsUsername, credentials.CmsPassword]);
    }

    [Fact]
    public async Task ProductionStartupFailsForMalformedGuidAndDuplicateIdentities()
    {
        var credentials = TestCredentialSet.Create();
        var malformedPassword = $"malformed-{Guid.NewGuid():N}";
        var malformed = new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Consumer:Password"] = malformedPassword,
        };
        var duplicateUsername = new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Administrator:Username"] =
                credentials.ConsumerUsername,
            [$"{AuthenticationConstants.CredentialSection}:Consumer:Username"] =
                credentials.ConsumerUsername,
        };
        var duplicatePassword = new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Administrator:Password"] =
                credentials.ConsumerPassword.ToUpperInvariant(),
            [$"{AuthenticationConstants.CredentialSection}:Consumer:Password"] =
                credentials.ConsumerPassword.ToLowerInvariant(),
        };

        await AssertProductionStartupFailsAsync(
            malformed,
            includeCredentials: true,
            [malformedPassword]);
        await AssertProductionStartupFailsAsync(
            duplicateUsername,
            includeCredentials: true,
            [credentials.ConsumerUsername]);
        await AssertProductionStartupFailsAsync(
            duplicatePassword,
            includeCredentials: true,
            [credentials.ConsumerPassword]);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public async Task ProductionStartupFailsForCmsUsernameOutsideBoundary(int length)
    {
        var username = new string('c', length);
        var configuration = new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Cms:Username"] = username,
        };

        await AssertProductionStartupFailsAsync(
            configuration,
            includeCredentials: true,
            [username]);
    }

    [Fact]
    public async Task ProductionStartupFailsForUnsafeUsernameSyntax()
    {
        var unsafeUsernames = new[]
        {
            $" unsafe-{Guid.NewGuid():N}",
            $"unsafe-{Guid.NewGuid():N} ",
            $"unsafe:{Guid.NewGuid():N}",
            $"unsafe{Environment.NewLine}{Guid.NewGuid():N}",
        };

        foreach (var username in unsafeUsernames)
        {
            var configuration = new Dictionary<string, string?>
            {
                [$"{AuthenticationConstants.CredentialSection}:Consumer:Username"] = username,
            };
            await AssertProductionStartupFailsAsync(
                configuration,
                includeCredentials: true,
                [username]);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public async Task ProductionStartupSucceedsAtCmsUsernameBoundaries(int length)
    {
        var configuration = new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Cms:Username"] = new string('c', length),
        };

        await AssertProductionStartupSucceedsAsync(configuration);
    }

    [Fact]
    public async Task ProductionStartupSucceedsForThreeValidDistinctActors()
    {
        await AssertProductionStartupSucceedsAsync(credentialOverrides: null);
    }

    private static async Task AssertProductionStartupFailsAsync(
        IReadOnlyDictionary<string, string?>? credentialOverrides,
        bool includeCredentials,
        IReadOnlyCollection<string> sensitiveValues)
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString,
            credentialOverrides: credentialOverrides,
            includeCredentials: includeCredentials);
        Exception? startupException = null;

        try
        {
            using var client = factory.CreateClient();
        }
        catch (Exception exception)
        {
            startupException = exception;
        }

        Assert.NotNull(startupException);
        var exceptionGraph = EnumerateExceptionGraph(startupException);
        var messageSummary = string.Join(
            Environment.NewLine,
            exceptionGraph.Select(exception => exception.Message));
        var typeSummary = string.Join(
            ", ",
            exceptionGraph
                .Select(exception => exception.GetType().FullName ?? exception.GetType().Name)
                .Distinct(StringComparer.Ordinal));

        Assert.False(
            exceptionGraph.All(exception => exception is ObjectDisposedException),
            $"Startup validation cannot be proven from ObjectDisposedException alone. Types: {typeSummary}");
        Assert.Contains(
            AuthenticationConstants.CredentialSection,
            messageSummary,
            StringComparison.Ordinal);
        Assert.Contains(
            exceptionGraph,
            exception => exception is OptionsValidationException);
        Assert.False(
            sensitiveValues.Any(value =>
                !string.IsNullOrWhiteSpace(value) &&
                messageSummary.Contains(value, StringComparison.Ordinal)),
            "A startup validation failure exposed authentication material.");
    }

    private static async Task AssertProductionStartupSucceedsAsync(
        IReadOnlyDictionary<string, string?>? credentialOverrides)
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString,
            credentialOverrides: credentialOverrides);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static List<Exception> EnumerateExceptionGraph(Exception rootException)
    {
        var graph = new List<Exception>();
        var pending = new Queue<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);

        pending.Enqueue(rootException);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            graph.Add(current);

            if (current.InnerException is not null)
            {
                pending.Enqueue(current.InnerException);
            }

            if (current is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    pending.Enqueue(innerException);
                }
            }

            if (current is ReflectionTypeLoadException typeLoadException)
            {
                foreach (var loaderException in typeLoadException.LoaderExceptions)
                {
                    if (loaderException is not null)
                    {
                        pending.Enqueue(loaderException);
                    }
                }
            }
        }

        return graph;
    }
}
