using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.Extensions.Options;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class CredentialOptionsValidatorTests
{
    private readonly CredentialOptionsValidator _validator = new();

    [Fact]
    public void ValidDistinctIdentitiesPass()
    {
        var options = CreateValidOptions();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void CmsUsernameAcceptedBoundariesPass(int length)
    {
        var options = CreateValidOptions();
        options.Cms!.Username = new string('c', length);

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public void CmsUsernameOutsideBoundariesFails(int length)
    {
        var options = CreateValidOptions();
        options.Cms!.Username = new string('c', length);

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void MissingSectionAndIdentityFailSafely()
    {
        var missingSection = _validator.Validate(Options.DefaultName, new CredentialOptions());
        var missingIdentityOptions = CreateValidOptions();
        missingIdentityOptions.Consumer = null;

        var missingIdentity = _validator.Validate(Options.DefaultName, missingIdentityOptions);

        Assert.False(missingSection.Succeeded);
        Assert.False(missingIdentity.Succeeded);
    }

    [Fact]
    public void EmptyAndWhitespaceFieldsFail()
    {
        var emptyUsername = CreateValidOptions();
        emptyUsername.Consumer!.Username = string.Empty;
        var whitespacePassword = CreateValidOptions();
        whitespacePassword.Administrator!.Password = " ";

        Assert.False(_validator.Validate(Options.DefaultName, emptyUsername).Succeeded);
        Assert.False(_validator.Validate(Options.DefaultName, whitespacePassword).Succeeded);
    }

    [Fact]
    public void SharedUsernameFailsUsingOrdinalComparison()
    {
        var options = CreateValidOptions();
        options.Administrator!.Username = options.Consumer!.Username;

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void CaseDifferentFormsOfOneGuidPasswordFailAsShared()
    {
        var options = CreateValidOptions();
        var sharedPassword = CreateGuidWithHexadecimalLetter();
        options.Consumer!.Password = sharedPassword.ToLowerInvariant();
        options.Administrator!.Password = sharedPassword.ToUpperInvariant();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void NonExactGuidPasswordFormatsFail()
    {
        var passwordIdentifier = Guid.NewGuid();
        var invalidFormats = new[]
        {
            passwordIdentifier.ToString("B"),
            passwordIdentifier.ToString("P"),
            passwordIdentifier.ToString("N"),
            passwordIdentifier.ToString("X"),
            $"{passwordIdentifier:D}x",
            $" {passwordIdentifier:D}",
            $"{passwordIdentifier:D} ",
        };

        foreach (var invalidPassword in invalidFormats)
        {
            var options = CreateValidOptions();
            options.Consumer!.Password = invalidPassword;
            var result = _validator.Validate(Options.DefaultName, options);
            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public void UnsafeUsernameSyntaxAndLengthFail()
    {
        var unsafeUsernames = new[]
        {
            $" {Guid.NewGuid():N}",
            $"{Guid.NewGuid():N} ",
            $"name:{Guid.NewGuid():N}",
            $"name{Environment.NewLine}{Guid.NewGuid():N}",
            new string('u', AuthenticationConstants.MaximumUsernameLength + 1),
        };

        foreach (var unsafeUsername in unsafeUsernames)
        {
            var options = CreateValidOptions();
            options.Consumer!.Username = unsafeUsername;
            var result = _validator.Validate(Options.DefaultName, options);
            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public void ConsumerAndAdministratorHaveNoUndocumentedMinimumUsernameLength()
    {
        var options = CreateValidOptions();
        options.Consumer!.Username = "c";
        options.Administrator!.Username = "a";

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationFailuresIdentifyFieldsWithoutExposingValues()
    {
        var options = CreateValidOptions();
        var sensitiveUsername = $" unsafe-{Guid.NewGuid():N}";
        var sensitivePassword = $"invalid-{Guid.NewGuid():N}";
        options.Consumer!.Username = sensitiveUsername;
        options.Consumer.Password = sensitivePassword;

        var result = _validator.Validate(Options.DefaultName, options);
        var failures = result.Failures?.ToArray() ?? [];

        Assert.False(result.Succeeded);
        Assert.Contains(failures, failure => failure.Contains(
            $"{AuthenticationConstants.CredentialSection}:Consumer:Username",
            StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains(
            $"{AuthenticationConstants.CredentialSection}:Consumer:Password",
            StringComparison.Ordinal));
        Assert.False(
            failures.Any(failure =>
                failure.Contains(sensitiveUsername, StringComparison.Ordinal) ||
                failure.Contains(sensitivePassword, StringComparison.Ordinal)),
            "A configuration validation message exposed authentication material.");
    }

    private static CredentialOptions CreateValidOptions()
    {
        var credentials = TestCredentialSet.Create();
        return new CredentialOptions
        {
            Cms = new CredentialIdentityOptions
            {
                Username = credentials.CmsUsername,
                Password = credentials.CmsPassword,
            },
            Consumer = new CredentialIdentityOptions
            {
                Username = credentials.ConsumerUsername,
                Password = credentials.ConsumerPassword,
            },
            Administrator = new CredentialIdentityOptions
            {
                Username = credentials.AdministratorUsername,
                Password = credentials.AdministratorPassword,
            },
        };
    }

    private static string CreateGuidWithHexadecimalLetter()
    {
        while (true)
        {
            var candidate = Guid.NewGuid().ToString("D");

            if (candidate.Any(character => character is >= 'a' and <= 'f'))
            {
                return candidate;
            }
        }
    }
}
