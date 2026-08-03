using System.Net;
using System.Text.Json;
using Xunit;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal static class AdministrativeStateResponseAssertions
{
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: TestContext.Current.CancellationToken);

        return document.RootElement.Clone();
    }

    public static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertConsumerChallenge(HttpResponseMessage response)
    {
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Equal("realm=\"ConsumerBasic\"", challenge.Parameter);
    }
}
