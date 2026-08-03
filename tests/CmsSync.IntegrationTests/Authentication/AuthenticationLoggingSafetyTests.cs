using System.Net;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
[Trait("Category", "Security")]
public sealed class AuthenticationLoggingSafetyTests
{
    [Fact]
    public async Task FailedAuthenticationDoesNotLeakHeadersOrCredentialMaterialToCapturedLogs()
    {
        using var capturedLogs = new CapturedLogProvider();
        await using var host = await AuthenticationTestHost.CreateAsync(capturedLogs: capturedLogs);
        var suppliedUsername = $"sentinel-{Guid.NewGuid():N}";
        var suppliedPassword = $"sentinel-{Guid.NewGuid():N}";
        var encodedParameter = AuthenticationRequestFactory.CreateBasicParameter(
            suppliedUsername,
            suppliedPassword);
        var authorizationHeader = $"Basic {encodedParameter}";
        var rawCredentialPair = $"{suppliedUsername}:{suppliedPassword}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            AuthenticationProbeRoutes.Consumer);
        Assert.True(request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader));

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sensitiveValues = new[]
        {
            authorizationHeader,
            encodedParameter,
            suppliedUsername,
            suppliedPassword,
            rawCredentialPair,
            host.Credentials.ConsumerPassword,
            host.Credentials.AdministratorPassword,
            host.Credentials.CmsPassword,
        };

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(capturedLogs.Entries);
        Assert.False(
            capturedLogs.ContainsAny(sensitiveValues),
            "Captured authentication logs exposed authentication material.");
        Assert.False(
            sensitiveValues.Any(value => body.Contains(value, StringComparison.Ordinal)),
            "An authentication failure response exposed authentication material.");
    }
}
