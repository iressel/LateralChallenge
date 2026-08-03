using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CmsSync.Infrastructure.Authentication;

public sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private const string SafeFailureMessage = "Basic authentication failed.";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly CredentialOptions _credentials;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<CredentialOptions> credentials)
        : base(options, logger, encoder)
    {
        _credentials = credentials.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationValues = Request.Headers.Authorization;

        if (authorizationValues.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (authorizationValues.Count != 1 ||
            !TryReadCredential(authorizationValues[0], out var username, out var password))
        {
            return Task.FromResult(AuthenticateResult.Fail(SafeFailureMessage));
        }

        var result = Options.Audience switch
        {
            CredentialAudience.Cms => AuthenticateCms(username, password),
            CredentialAudience.Consumer => AuthenticateConsumer(username, password),
            _ => AuthenticateResult.Fail(SafeFailureMessage),
        };

        return Task.FromResult(result);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = $"Basic realm=\"{Options.Realm}\"";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.Remove(HeaderNames.WWWAuthenticate);
        return Task.CompletedTask;
    }

    private AuthenticateResult AuthenticateCms(string username, string password)
    {
        var cmsCredentials = _credentials.Cms;

        if (cmsCredentials is null ||
            !FixedTimeCredentialVerifier.Verify(username, password, cmsCredentials))
        {
            return AuthenticateResult.Fail(SafeFailureMessage);
        }

        return CreateSuccess(cmsCredentials, AuthenticationConstants.CmsServiceRole);
    }

    private AuthenticateResult AuthenticateConsumer(string username, string password)
    {
        var consumerCredentials = _credentials.Consumer;
        var administratorCredentials = _credentials.Administrator;

        if (consumerCredentials is null || administratorCredentials is null)
        {
            return AuthenticateResult.Fail(SafeFailureMessage);
        }

        var isConsumer = FixedTimeCredentialVerifier.Verify(username, password, consumerCredentials);
        var isAdministrator = FixedTimeCredentialVerifier.Verify(
            username,
            password,
            administratorCredentials);

        if (!(isConsumer | isAdministrator) || (isConsumer && isAdministrator))
        {
            return AuthenticateResult.Fail(SafeFailureMessage);
        }

        if (isConsumer)
        {
            return CreateSuccess(consumerCredentials, AuthenticationConstants.NormalConsumerRole);
        }

        return CreateSuccess(administratorCredentials, AuthenticationConstants.AdministratorRole);
    }

    private AuthenticateResult CreateSuccess(
        CredentialIdentityOptions configuredIdentity,
        string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, configuredIdentity.Username!),
            new Claim(ClaimTypes.Name, configuredIdentity.Username!),
            new Claim(ClaimTypes.Role, role),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static bool TryReadCredential(
        string? authorizationValue,
        out string username,
        out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (string.IsNullOrEmpty(authorizationValue) ||
            authorizationValue.Length > AuthenticationConstants.MaximumAuthorizationHeaderLength ||
            !AuthenticationHeaderValue.TryParse(authorizationValue, out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter) ||
            header.Parameter.Length > AuthenticationConstants.MaximumEncodedCredentialLength ||
            header.Parameter.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var decodedBytes = GC.AllocateUninitializedArray<byte>(
            AuthenticationConstants.MaximumDecodedCredentialByteLength);
        var decodedCharacters = GC.AllocateUninitializedArray<char>(
            StrictUtf8.GetMaxCharCount(AuthenticationConstants.MaximumDecodedCredentialByteLength));

        try
        {
            if (!Convert.TryFromBase64String(
                    header.Parameter,
                    decodedBytes,
                    out var decodedByteCount) ||
                decodedByteCount == 0 ||
                decodedByteCount > AuthenticationConstants.MaximumDecodedCredentialByteLength)
            {
                return false;
            }

            var decodedCharacterCount = StrictUtf8.GetChars(
                decodedBytes.AsSpan(0, decodedByteCount),
                decodedCharacters);
            var credentialCharacters = decodedCharacters.AsSpan(0, decodedCharacterCount);
            var separatorIndex = credentialCharacters.IndexOf(':');

            if (separatorIndex <= 0)
            {
                return false;
            }

            var usernameCharacters = credentialCharacters[..separatorIndex];
            var passwordCharacters = credentialCharacters[(separatorIndex + 1)..];

            if (!IsValidSuppliedUsername(usernameCharacters) ||
                passwordCharacters.Length > AuthenticationConstants.MaximumSuppliedPasswordLength)
            {
                return false;
            }

            username = new string(usernameCharacters);
            password = new string(passwordCharacters);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodedBytes);
            Array.Clear(decodedCharacters);
        }
    }

    private static bool IsValidSuppliedUsername(ReadOnlySpan<char> username)
    {
        if (username.IsEmpty ||
            username.Length > AuthenticationConstants.MaximumUsernameLength ||
            char.IsWhiteSpace(username[0]) ||
            char.IsWhiteSpace(username[^1]))
        {
            return false;
        }

        foreach (var character in username)
        {
            if (char.IsControl(character) || character == ':')
            {
                return false;
            }
        }

        return true;
    }
}
