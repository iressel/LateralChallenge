using System.Security.Claims;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class AuthenticationTestHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private AuthenticationTestHost(
        WebApplication application,
        HttpClient client,
        TestCredentialSet credentials)
    {
        _application = application;
        Client = client;
        Credentials = credentials;
    }

    public HttpClient Client { get; }

    public TestCredentialSet Credentials { get; }

    public IServiceProvider Services
    {
        get
        {
            return _application.Services;
        }
    }

    public static async Task<AuthenticationTestHost> CreateAsync(
        string environmentName = "Development",
        IReadOnlyDictionary<string, string?>? credentialOverrides = null,
        bool includeCredentials = true,
        CapturedLogProvider? capturedLogs = null)
    {
        var credentials = TestCredentialSet.Create();
        var configuration = new Dictionary<string, string?>();

        if (includeCredentials)
        {
            foreach (var credential in credentials.CreateConfiguration())
            {
                configuration[credential.Key] = credential.Value;
            }
        }

        if (credentialOverrides is not null)
        {
            foreach (var credentialOverride in credentialOverrides)
            {
                configuration[credentialOverride.Key] = credentialOverride.Value;
            }
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AuthenticationTestHost).Assembly.FullName,
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration);

        if (capturedLogs is not null)
        {
            builder.Logging.AddProvider(capturedLogs);
        }

        builder.Services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
        builder.Services.AddCmsAuthentication(builder.Configuration);

        var application = builder.Build();
        application.UseMiddleware<AuthenticationResponseSecurityMiddleware>();

        if (!application.Environment.IsDevelopment())
        {
            application.UseHttpsRedirection();
        }

        application.UseAuthentication();
        application.UseAuthorization();
        MapProbeEndpoints(application);

        try
        {
            await application.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }

        var client = application.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return new AuthenticationTestHost(application, client, credentials);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _application.DisposeAsync();
    }

    private static void MapProbeEndpoints(WebApplication application)
    {
        application.MapGet(
                AuthenticationProbeRoutes.Cms,
                (HttpContext context) => Results.Text(ReadRoles(context)))
            .RequireAuthorization(AuthenticationConstants.CmsEventsPolicy);
        application.MapGet(
                AuthenticationProbeRoutes.Consumer,
                (HttpContext context) => Results.Text(ReadRoles(context)))
            .RequireAuthorization(AuthenticationConstants.ConsumerAccessPolicy);
        application.MapGet(
                AuthenticationProbeRoutes.Administrator,
                (HttpContext context) => Results.Text(ReadRoles(context)))
            .RequireAuthorization(AuthenticationConstants.AdministratorAccessPolicy);
    }

    private static string ReadRoles(HttpContext context)
    {
        return string.Join(
            ",",
            context.User.Claims
                .Where(claim => claim.Type == ClaimTypes.Role)
                .Select(claim => claim.Value)
                .Order(StringComparer.Ordinal));
    }
}
