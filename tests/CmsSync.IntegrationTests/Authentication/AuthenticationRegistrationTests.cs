using CmsSync.Infrastructure.Authentication;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CmsSync.IntegrationTests.Authentication;

[Trait("Category", "Authentication")]
public sealed class AuthenticationRegistrationTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task ExactlyTwoIsolatedSchemesAreRegisteredWithoutUnsafeDefaults()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var schemeProvider = host.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var authenticationOptions = host.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        var schemes = await schemeProvider.GetAllSchemesAsync();

        Assert.Equal(
            new[] { AuthenticationConstants.CmsScheme, AuthenticationConstants.ConsumerScheme },
            schemes.Select(scheme => scheme.Name).Order(StringComparer.Ordinal));
        Assert.Null(authenticationOptions.DefaultScheme);
        Assert.Null(authenticationOptions.DefaultAuthenticateScheme);
        Assert.Null(authenticationOptions.DefaultChallengeScheme);
    }

    [Fact]
    public async Task PoliciesNameOnlyTheirSchemeAndRequiredRoles()
    {
        await using var host = await AuthenticationTestHost.CreateAsync();
        var policyProvider = host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var cmsPolicy = await policyProvider.GetPolicyAsync(AuthenticationConstants.CmsEventsPolicy);
        var consumerPolicy = await policyProvider.GetPolicyAsync(
            AuthenticationConstants.ConsumerAccessPolicy);
        var administratorPolicy = await policyProvider.GetPolicyAsync(
            AuthenticationConstants.AdministratorAccessPolicy);

        AssertPolicy(
            cmsPolicy,
            AuthenticationConstants.CmsScheme,
            AuthenticationConstants.CmsServiceRole);
        AssertPolicy(
            consumerPolicy,
            AuthenticationConstants.ConsumerScheme,
            AuthenticationConstants.NormalConsumerRole,
            AuthenticationConstants.AdministratorRole);
        AssertPolicy(
            administratorPolicy,
            AuthenticationConstants.ConsumerScheme,
            AuthenticationConstants.AdministratorRole);
    }

    [Fact]
    public async Task ProductionProgramRegistrationResolvesAndStartsWithRuntimeCredentials()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/",
            TestContext.Current.CancellationToken);
        var schemeProvider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, schemes.Count());
    }

    private static void AssertPolicy(
        AuthorizationPolicy? policy,
        string expectedScheme,
        params string[] expectedRoles)
    {
        Assert.NotNull(policy);
        Assert.Equal(new[] { expectedScheme }, policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
        var roleRequirement = Assert.Single(
            policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            expectedRoles.Order(StringComparer.Ordinal),
            roleRequirement.AllowedRoles.Order(StringComparer.Ordinal));
    }
}
