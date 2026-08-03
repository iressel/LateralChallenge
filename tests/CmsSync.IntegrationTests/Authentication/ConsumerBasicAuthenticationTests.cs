using System.Net;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class ConsumerBasicAuthenticationTests
{
    [Fact]
    public async Task NormalConsumerAndAdministratorReceiveOnlyTheirApplicableRole()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var consumerRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword);
        using var administratorRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.AdministratorUsername,
            host.Credentials.AdministratorPassword);

        using var consumerResponse = await host.Client.SendAsync(
            consumerRequest,
            TestContext.Current.CancellationToken);
        using var administratorResponse = await host.Client.SendAsync(
            administratorRequest,
            TestContext.Current.CancellationToken);
        var consumerRole = await consumerResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var administratorRole = await administratorResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, consumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, administratorResponse.StatusCode);
        Assert.Equal(AuthenticationConstants.NormalConsumerRole, consumerRole);
        Assert.Equal(AuthenticationConstants.AdministratorRole, administratorRole);
    }

    [Fact]
    public async Task AdministratorCredentialsSucceedOnAdministratorPolicy()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var request = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Administrator,
            host.Credentials.AdministratorUsername,
            host.Credentials.AdministratorPassword);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var role = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AuthenticationConstants.AdministratorRole, role);
    }

    [Fact]
    public async Task NormalConsumerDeniedAdministratorPolicyReceivesNoStoreForbidWithoutChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var request = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Administrator,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(
            body.Contains(host.Credentials.ConsumerUsername, StringComparison.Ordinal) ||
            body.Contains(host.Credentials.ConsumerPassword, StringComparison.Ordinal),
            "A forbidden response exposed authentication material.");
    }

    [Fact]
    public async Task MissingAndWrongConsumerCredentialsReturnExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            AuthenticationProbeRoutes.Consumer);
        using var wrongRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.ConsumerUsername,
            Guid.NewGuid().ToString("D"));

        await AssertConsumerUnauthorizedAsync(host, missingRequest);
        await AssertConsumerUnauthorizedAsync(host, wrongRequest);
    }

    [Fact]
    public async Task MixedConsumerAndAdministratorCredentialPartsFail()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var consumerNameWithAdministratorPassword =
            AuthenticationRequestFactory.CreateBasicGet(
                AuthenticationProbeRoutes.Consumer,
                host.Credentials.ConsumerUsername,
                host.Credentials.AdministratorPassword);
        using var administratorNameWithConsumerPassword =
            AuthenticationRequestFactory.CreateBasicGet(
                AuthenticationProbeRoutes.Consumer,
                host.Credentials.AdministratorUsername,
                host.Credentials.ConsumerPassword);

        await AssertConsumerUnauthorizedAsync(host, consumerNameWithAdministratorPassword);
        await AssertConsumerUnauthorizedAsync(host, administratorNameWithConsumerPassword);
    }

    private static async Task AssertConsumerUnauthorizedAsync(
        AuthenticationTestHost host,
        HttpRequestMessage request)
    {
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Equal($"realm=\"{AuthenticationConstants.ConsumerScheme}\"", challenge.Parameter);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }
}
