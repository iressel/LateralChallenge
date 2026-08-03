using System.Net;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
[Trait("Category", "Security")]
public sealed class HttpsAuthenticationTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task NonDevelopmentHttpRedirectsToHttpsWithNoStoreAndHttpsContinuesNormally()
    {
        await using var host = await AuthenticationTestHost.CreateAsync(
            environmentName: "Production");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://localhost{AuthenticationProbeRoutes.Consumer}");

        using var redirectResponse = await host.Client.SendAsync(
            httpRequest,
            TestContext.Current.CancellationToken);
        using var httpsResponse = await host.Client.GetAsync(
            AuthenticationProbeRoutes.Consumer,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirectResponse.StatusCode);
        Assert.NotNull(redirectResponse.Headers.Location);
        Assert.Equal(Uri.UriSchemeHttps, redirectResponse.Headers.Location.Scheme);
        Assert.Contains(
            "no-store",
            redirectResponse.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, httpsResponse.StatusCode);
        Assert.Null(httpsResponse.Headers.Location);
    }

    [Fact]
    public async Task ProductionProgramUsesTheSameHttpsAndNoStoreOrderWithoutAddingRoutes()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString,
            environmentName: "Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
        });

        using var redirectResponse = await client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);
        using var httpsRequest = new HttpRequestMessage(HttpMethod.Get, "https://localhost/");
        using var httpsResponse = await client.SendAsync(
            httpsRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirectResponse.StatusCode);
        Assert.Equal(Uri.UriSchemeHttps, redirectResponse.Headers.Location?.Scheme);
        Assert.Contains(
            "no-store",
            redirectResponse.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, httpsResponse.StatusCode);
        Assert.Null(httpsResponse.Headers.Location);
    }
}
