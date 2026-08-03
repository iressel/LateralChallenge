using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class FixedTimeCredentialVerifierTests
{
    [Fact]
    public void ExactCredentialIdentitySucceeds()
    {
        var credentials = TestCredentialSet.Create();
        var configured = CreateIdentity(credentials.ConsumerUsername, credentials.ConsumerPassword);

        var result = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            credentials.ConsumerPassword,
            configured);

        Assert.True(result);
    }

    [Fact]
    public void WrongUsernameAndWrongPasswordFail()
    {
        var credentials = TestCredentialSet.Create();
        var configured = CreateIdentity(credentials.ConsumerUsername, credentials.ConsumerPassword);

        var wrongUsername = FixedTimeCredentialVerifier.Verify(
            credentials.AdministratorUsername,
            credentials.ConsumerPassword,
            configured);
        var wrongPassword = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            credentials.AdministratorPassword,
            configured);

        Assert.False(wrongUsername);
        Assert.False(wrongPassword);
    }

    [Fact]
    public void UsernameAndPasswordCasingRemainExact()
    {
        var credentials = TestCredentialSet.Create();
        var configured = CreateIdentity(credentials.ConsumerUsername, credentials.ConsumerPassword);

        var usernameCaseMismatch = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername.ToUpperInvariant(),
            credentials.ConsumerPassword,
            configured);
        var passwordCaseMismatch = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            credentials.ConsumerPassword.ToUpperInvariant(),
            configured);

        Assert.False(usernameCaseMismatch);
        Assert.False(passwordCaseMismatch);
    }

    [Fact]
    public void MixedActorCredentialPartsFail()
    {
        var credentials = TestCredentialSet.Create();
        var configured = CreateIdentity(credentials.ConsumerUsername, credentials.ConsumerPassword);

        var otherUsername = FixedTimeCredentialVerifier.Verify(
            credentials.AdministratorUsername,
            credentials.ConsumerPassword,
            configured);
        var otherPassword = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            credentials.AdministratorPassword,
            configured);

        Assert.False(otherUsername);
        Assert.False(otherPassword);
    }

    [Fact]
    public void BoundedAndInvalidTextInputsFailSafely()
    {
        var credentials = TestCredentialSet.Create();
        var configured = CreateIdentity(credentials.ConsumerUsername, credentials.ConsumerPassword);
        var oversizedUsername = new string(
            'u',
            AuthenticationConstants.MaximumUsernameLength + 1);
        var oversizedPassword = new string(
            'p',
            AuthenticationConstants.MaximumSuppliedPasswordLength + 1);
        var invalidUnicodeUsername = new string(['\ud800']);

        Assert.False(FixedTimeCredentialVerifier.Verify(
            oversizedUsername,
            credentials.ConsumerPassword,
            configured));
        Assert.False(FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            oversizedPassword,
            configured));
        Assert.False(FixedTimeCredentialVerifier.Verify(
            invalidUnicodeUsername,
            credentials.ConsumerPassword,
            configured));
    }

    [Fact]
    public void MissingConfiguredFieldsFailClosed()
    {
        var credentials = TestCredentialSet.Create();
        var configured = new CredentialIdentityOptions();

        var result = FixedTimeCredentialVerifier.Verify(
            credentials.ConsumerUsername,
            credentials.ConsumerPassword,
            configured);

        Assert.False(result);
    }

    private static CredentialIdentityOptions CreateIdentity(string username, string password)
    {
        return new CredentialIdentityOptions
        {
            Username = username,
            Password = password,
        };
    }
}
