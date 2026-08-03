using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CmsSync.Infrastructure.Authentication;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddCmsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<CredentialOptions>, CredentialOptionsValidator>();
        services.AddOptions<CredentialOptions>()
            .Bind(configuration.GetSection(AuthenticationConstants.CredentialSection))
            .ValidateOnStart();

        services.AddAuthentication()
            .AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>(
                AuthenticationConstants.CmsScheme,
                options =>
                {
                    options.Realm = AuthenticationConstants.CmsScheme;
                    options.Audience = CredentialAudience.Cms;
                })
            .AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>(
                AuthenticationConstants.ConsumerScheme,
                options =>
                {
                    options.Realm = AuthenticationConstants.ConsumerScheme;
                    options.Audience = CredentialAudience.Consumer;
                });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthenticationConstants.CmsEventsPolicy,
                BuildPolicy(
                    AuthenticationConstants.CmsScheme,
                    AuthenticationConstants.CmsServiceRole));
            options.AddPolicy(
                AuthenticationConstants.ConsumerAccessPolicy,
                BuildPolicy(
                    AuthenticationConstants.ConsumerScheme,
                    AuthenticationConstants.NormalConsumerRole,
                    AuthenticationConstants.AdministratorRole));
            options.AddPolicy(
                AuthenticationConstants.AdministratorAccessPolicy,
                BuildPolicy(
                    AuthenticationConstants.ConsumerScheme,
                    AuthenticationConstants.AdministratorRole));
        });

        return services;
    }

    private static AuthorizationPolicy BuildPolicy(
        string scheme,
        params string[] roles)
    {
        return new AuthorizationPolicyBuilder(scheme)
            .RequireAuthenticatedUser()
            .RequireRole(roles)
            .Build();
    }
}
