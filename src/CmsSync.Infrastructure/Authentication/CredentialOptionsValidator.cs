using System.Text;
using Microsoft.Extensions.Options;

namespace CmsSync.Infrastructure.Authentication;

public sealed class CredentialOptionsValidator : IValidateOptions<CredentialOptions>
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public ValidateOptionsResult Validate(string? name, CredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateIdentity(options.Cms, "Cms", requiresCmsLength: true, failures);
        ValidateIdentity(options.Consumer, "Consumer", requiresCmsLength: false, failures);
        ValidateIdentity(options.Administrator, "Administrator", requiresCmsLength: false, failures);
        ValidateDistinctUsernames(options, failures);
        ValidateDistinctPasswords(options, failures);

        if (failures.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateIdentity(
        CredentialIdentityOptions? identity,
        string actor,
        bool requiresCmsLength,
        List<string> failures)
    {
        var fieldPrefix = $"{AuthenticationConstants.CredentialSection}:{actor}";

        if (identity is null)
        {
            failures.Add($"{fieldPrefix} is required.");
            return;
        }

        ValidateUsername(identity.Username, fieldPrefix, requiresCmsLength, failures);
        ValidatePassword(identity.Password, fieldPrefix, failures);
    }

    private static void ValidateUsername(
        string? username,
        string fieldPrefix,
        bool requiresCmsLength,
        List<string> failures)
    {
        var field = $"{fieldPrefix}:Username";

        if (string.IsNullOrWhiteSpace(username))
        {
            failures.Add($"{field} is required.");
            return;
        }

        if (!string.Equals(username, username.Trim(), StringComparison.Ordinal))
        {
            failures.Add($"{field} must not have leading or trailing whitespace.");
        }

        if (username.Contains(':', StringComparison.Ordinal))
        {
            failures.Add($"{field} must not contain a colon.");
        }

        if (username.Any(char.IsControl))
        {
            failures.Add($"{field} must not contain control characters.");
        }

        if (username.Length > AuthenticationConstants.MaximumUsernameLength)
        {
            failures.Add($"{field} exceeds the safe maximum length.");
        }

        if (!HasValidUtf8Length(username))
        {
            failures.Add($"{field} is not valid bounded UTF-8 text.");
        }

        if (requiresCmsLength &&
            (username.Length < AuthenticationConstants.MinimumCmsUsernameLength ||
             username.Length > AuthenticationConstants.MaximumCmsUsernameLength))
        {
            failures.Add(
                $"{field} must contain between " +
                $"{AuthenticationConstants.MinimumCmsUsernameLength} and " +
                $"{AuthenticationConstants.MaximumCmsUsernameLength} characters.");
        }
    }

    private static bool HasValidUtf8Length(string username)
    {
        try
        {
            return StrictUtf8.GetByteCount(username) <= AuthenticationConstants.MaximumUsernameByteLength;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static void ValidatePassword(
        string? password,
        string fieldPrefix,
        List<string> failures)
    {
        var field = $"{fieldPrefix}:Password";

        if (string.IsNullOrWhiteSpace(password))
        {
            failures.Add($"{field} is required.");
            return;
        }

        if (password.Length != 36 || !Guid.TryParseExact(password, "D", out _))
        {
            failures.Add($"{field} must use exact GUID D format.");
        }
    }

    private static void ValidateDistinctUsernames(
        CredentialOptions options,
        List<string> failures)
    {
        var usernames = GetCompleteIdentities(options)
            .Select(identity => identity.Username)
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .ToArray();

        for (var first = 0; first < usernames.Length; first++)
        {
            for (var second = first + 1; second < usernames.Length; second++)
            {
                if (string.Equals(usernames[first], usernames[second], StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{AuthenticationConstants.CredentialSection} usernames must be distinct.");
                    return;
                }
            }
        }
    }

    private static void ValidateDistinctPasswords(
        CredentialOptions options,
        List<string> failures)
    {
        var passwordIdentifiers = new List<Guid>();

        foreach (var identity in GetCompleteIdentities(options))
        {
            if (Guid.TryParseExact(identity.Password, "D", out var passwordIdentifier))
            {
                passwordIdentifiers.Add(passwordIdentifier);
            }
        }

        if (passwordIdentifiers.Count != passwordIdentifiers.Distinct().Count())
        {
            failures.Add(
                $"{AuthenticationConstants.CredentialSection} passwords must be distinct.");
        }
    }

    private static IEnumerable<CredentialIdentityOptions> GetCompleteIdentities(
        CredentialOptions options)
    {
        if (options.Cms is not null)
        {
            yield return options.Cms;
        }

        if (options.Consumer is not null)
        {
            yield return options.Consumer;
        }

        if (options.Administrator is not null)
        {
            yield return options.Administrator;
        }
    }
}
