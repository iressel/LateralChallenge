using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class CmsBasicAuthenticationTests
{
    [Fact]
    public async Task ValidCmsCredentialsAuthenticateWithOnlyCmsRole()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var request = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Cms,
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var role = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AuthenticationConstants.CmsServiceRole, role);
        Assert.DoesNotContain(AuthenticationConstants.NormalConsumerRole, role, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthenticationConstants.AdministratorRole, role, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCmsCredentialsReturnExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthenticationProbeRoutes.Cms);

        await AssertCmsUnauthorizedAsync(host, request, []);
    }

    [Fact]
    public async Task MalformedAndInvalidCmsAuthorizationValuesReturnExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var decodedWithoutColon = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"value-{Guid.NewGuid():N}"));
        var invalidBase64 = $"invalid-{Guid.NewGuid():N}!";
        var requests = new[]
        {
            CreateRawAuthorizationRequest("Basic"),
            AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Cms,
                "Basic",
                invalidBase64),
            AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Cms,
                "Basic",
                decodedWithoutColon),
            AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Cms,
                "Bearer",
                Guid.NewGuid().ToString("N")),
        };

        foreach (var request in requests)
        {
            using (request)
            {
                await AssertCmsUnauthorizedAsync(
                    host,
                    request,
                    [invalidBase64, decodedWithoutColon]);
            }
        }
    }

    [Fact]
    public async Task WrongCmsUsernameAndPasswordReturnExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var wrongUsernameRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Cms,
            $"wrong-{Guid.NewGuid():N}",
            host.Credentials.CmsPassword);
        using var wrongPasswordRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Cms,
            host.Credentials.CmsUsername,
            Guid.NewGuid().ToString("D"));

        await AssertCmsUnauthorizedAsync(
            host,
            wrongUsernameRequest,
            [host.Credentials.CmsUsername, host.Credentials.CmsPassword]);
        await AssertCmsUnauthorizedAsync(
            host,
            wrongPasswordRequest,
            [host.Credentials.CmsUsername, host.Credentials.CmsPassword]);
    }

    [Fact]
    public async Task MultipleAuthorizationValuesReturnExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var parameter = AuthenticationRequestFactory.CreateBasicParameter(
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthenticationProbeRoutes.Cms);
        Assert.True(request.Headers.TryAddWithoutValidation(
            "Authorization",
            [$"Basic {parameter}", $"Basic {parameter}"]));

        await AssertCmsUnauthorizedAsync(host, request, [parameter]);
    }

    [Fact]
    public async Task OversizedEncodedCredentialReturnsExactNoStoreChallenge()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var oversizedParameter = new string(
            'A',
            AuthenticationConstants.MaximumEncodedCredentialLength + 1);
        using var request = AuthenticationRequestFactory.CreateGet(
            AuthenticationProbeRoutes.Cms,
            "Basic",
            oversizedParameter);

        await AssertCmsUnauthorizedAsync(host, request, []);
    }

    private static HttpRequestMessage CreateRawAuthorizationRequest(string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AuthenticationProbeRoutes.Cms);
        Assert.True(request.Headers.TryAddWithoutValidation("Authorization", value));
        return request;
    }

    private static async Task AssertCmsUnauthorizedAsync(
        AuthenticationTestHost host,
        HttpRequestMessage request,
        IReadOnlyCollection<string> sensitiveValues)
    {
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic", challenge.Scheme);
        Assert.Equal($"realm=\"{AuthenticationConstants.CmsScheme}\"", challenge.Parameter);
        Assert.Contains("no-store", GetCacheControl(response), StringComparison.OrdinalIgnoreCase);
        Assert.False(
            sensitiveValues.Any(value => body.Contains(value, StringComparison.Ordinal)),
            "An authentication failure response exposed supplied authentication material.");
    }

    private static string GetCacheControl(HttpResponseMessage response)
    {
        return string.Join(",", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }
}
