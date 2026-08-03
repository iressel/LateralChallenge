using System.Net;
using System.Security.Cryptography;
using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class BasicAuthenticationParsingTests
{
    [Fact]
    public async Task BasicSchemeTokenIsCaseInsensitive()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var request = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword,
            "bAsIc");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsernameAndPasswordAreCaseSensitive()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var usernameCaseRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.ConsumerUsername.ToUpperInvariant(),
            host.Credentials.ConsumerPassword);
        using var passwordCaseRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword.ToUpperInvariant());

        await AssertUnauthorizedWithoutServerErrorAsync(host, usernameCaseRequest);
        await AssertUnauthorizedWithoutServerErrorAsync(host, passwordCaseRequest);
    }

    [Fact]
    public async Task LeadingAndTrailingUsernameWhitespaceDoNotAuthenticate()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var leadingWhitespaceRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            $" {host.Credentials.ConsumerUsername}",
            host.Credentials.ConsumerPassword);
        using var trailingWhitespaceRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            $"{host.Credentials.ConsumerUsername} ",
            host.Credentials.ConsumerPassword);

        await AssertUnauthorizedWithoutServerErrorAsync(host, leadingWhitespaceRequest);
        await AssertUnauthorizedWithoutServerErrorAsync(host, trailingWhitespaceRequest);
    }

    [Fact]
    public async Task InvalidUtf8AndControlCharactersFailWithoutServerError()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var invalidUtf8Bytes = new byte[] { 0xFF, (byte)':', (byte)'x' };
        var controlUsernameBytes = new byte[]
        {
            (byte)'u',
            0,
            (byte)'s',
            (byte)'e',
            (byte)'r',
            (byte)':',
            (byte)'x',
        };

        try
        {
            using var invalidUtf8Request = AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Consumer,
                "Basic",
                Convert.ToBase64String(invalidUtf8Bytes));
            using var controlUsernameRequest = AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Consumer,
                "Basic",
                Convert.ToBase64String(controlUsernameBytes));

            await AssertUnauthorizedWithoutServerErrorAsync(host, invalidUtf8Request);
            await AssertUnauthorizedWithoutServerErrorAsync(host, controlUsernameRequest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(invalidUtf8Bytes);
            CryptographicOperations.ZeroMemory(controlUsernameBytes);
        }
    }

    [Fact]
    public async Task DecodedCredentialsAreBoundedIndependentlyOfEncodedLength()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var oversizedDecodedBytes = GC.AllocateUninitializedArray<byte>(
            AuthenticationConstants.MaximumDecodedCredentialByteLength + 1);
        Array.Fill(oversizedDecodedBytes, (byte)'a');

        try
        {
            var parameter = Convert.ToBase64String(oversizedDecodedBytes);
            Assert.True(
                parameter.Length <= AuthenticationConstants.MaximumEncodedCredentialLength,
                "The decoded-bound test did not remain within the encoded bound.");
            using var request = AuthenticationRequestFactory.CreateGet(
                AuthenticationProbeRoutes.Consumer,
                "Basic",
                parameter);

            await AssertUnauthorizedWithoutServerErrorAsync(host, request);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oversizedDecodedBytes);
        }
    }

    [Fact]
    public async Task EmptyUsernameAndMalformedParametersNeverProduceServerError()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        using var emptyUsernameRequest = AuthenticationRequestFactory.CreateBasicGet(
            AuthenticationProbeRoutes.Consumer,
            string.Empty,
            host.Credentials.ConsumerPassword);
        using var malformedRequest = AuthenticationRequestFactory.CreateGet(
            AuthenticationProbeRoutes.Consumer,
            "Basic",
            "%%%!");

        await AssertUnauthorizedWithoutServerErrorAsync(host, emptyUsernameRequest);
        await AssertUnauthorizedWithoutServerErrorAsync(host, malformedRequest);
    }

    private static async Task AssertUnauthorizedWithoutServerErrorAsync(
        AuthenticationTestHost host,
        HttpRequestMessage request)
    {
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(response.Headers.WwwAuthenticate);
    }
}
