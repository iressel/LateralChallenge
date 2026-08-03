using System.Net;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class CrossSchemeAuthenticationTests
{
    [Fact]
    public async Task CmsCredentialsCannotAuthenticateConsumerPolicies()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();

        await AssertCrossSchemeUnauthorizedAsync(
            host,
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword,
            AuthenticationConstants.ConsumerScheme);
        await AssertCrossSchemeUnauthorizedAsync(
            host,
            AuthenticationProbeRoutes.Administrator,
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword,
            AuthenticationConstants.ConsumerScheme);
    }

    [Fact]
    public async Task ConsumerActorsCannotAuthenticateCmsPolicy()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();

        await AssertCrossSchemeUnauthorizedAsync(
            host,
            AuthenticationProbeRoutes.Cms,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword,
            AuthenticationConstants.CmsScheme);
        await AssertCrossSchemeUnauthorizedAsync(
            host,
            AuthenticationProbeRoutes.Cms,
            host.Credentials.AdministratorUsername,
            host.Credentials.AdministratorPassword,
            AuthenticationConstants.CmsScheme);
    }

    private static async Task AssertCrossSchemeUnauthorizedAsync(
        AuthenticationTestHost host,
        string path,
        string username,
        string password,
        string expectedRealm)
    {
        using var request = AuthenticationRequestFactory.CreateBasicGet(
            path,
            username,
            password);
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Equal($"realm=\"{expectedRealm}\"", challenge.Parameter);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }
}
