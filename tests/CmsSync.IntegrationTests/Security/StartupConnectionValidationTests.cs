using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Security;

[Trait("Category", "Security")]
public sealed class StartupConnectionValidationTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingWriteOrReadConnectionFailsStartupWithOnlyTheSafeKey(
        bool writeIsMissing)
    {
        await AssertStartupFailsSafelyAsync(
            writeIsMissing ? null : SafeConnectionString,
            writeIsMissing ? SafeConnectionString : null,
            writeIsMissing ? "ConnectionStrings:WriteDatabase" : "ConnectionStrings:ReadDatabase",
            []);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedWriteOrReadConnectionFailsStartupWithoutEchoingItsValue(
        bool writeIsMalformed)
    {
        var sentinel = $"malformed-connection-{Guid.NewGuid():N}";
        await AssertStartupFailsSafelyAsync(
            writeIsMalformed ? sentinel : SafeConnectionString,
            writeIsMalformed ? SafeConnectionString : sentinel,
            writeIsMalformed ? "ConnectionStrings:WriteDatabase" : "ConnectionStrings:ReadDatabase",
            [sentinel]);
    }

    private static async Task AssertStartupFailsSafelyAsync(
        string? writeConnectionString,
        string? readConnectionString,
        string expectedKey,
        IReadOnlyCollection<string> sensitiveValues)
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            writeConnectionString,
            readConnectionString);
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
        var messages = ReadExceptionMessages(startupException);
        Assert.Contains(expectedKey, messages, StringComparison.Ordinal);
        Assert.False(
            sensitiveValues.Any(value => messages.Contains(value, StringComparison.Ordinal)),
            "Connection validation exposed a supplied configuration value.");
    }

    private static string ReadExceptionMessages(Exception exception)
    {
        var messages = new List<string>();

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(Environment.NewLine, messages);
    }
}
