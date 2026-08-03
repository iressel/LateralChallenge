using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CmsSync.IntegrationTests.ReadApi;

internal sealed class ReadApiTestHost : IAsyncDisposable
{
    private readonly CmsSyncWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _configuredFactory;

    private ReadApiTestHost(
        CmsSyncWebApplicationFactory baseFactory,
        WebApplicationFactory<Program> configuredFactory)
    {
        _baseFactory = baseFactory;
        _configuredFactory = configuredFactory;
        Client = _configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    public HttpClient Client { get; }

    public TestCredentialSet Credentials => _baseFactory.Credentials;

    public IServiceProvider Services => _configuredFactory.Services;

    public static ReadApiTestHost Create(
        SqlServerFixture fixture,
        ReadApiSqlCommandInterceptor? commandInterceptor = null,
        Action<IServiceCollection>? configureServices = null,
        CapturedLogProvider? capturedLogs = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var baseFactory = new CmsSyncWebApplicationFactory(
            fixture.WriteConnectionString,
            fixture.ReadConnectionString);
        var configuredFactory = baseFactory.WithWebHostBuilder(builder =>
        {
            if (capturedLogs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(capturedLogs));
            }

            builder.ConfigureTestServices(services =>
            {
                if (commandInterceptor is not null)
                {
                    services.AddDbContext<CmsReadDbContext>(options =>
                        options.AddInterceptors(commandInterceptor));
                }

                configureServices?.Invoke(services);
            });
        });

        return new ReadApiTestHost(baseFactory, configuredFactory);
    }

    public HttpRequestMessage CreateConsumerGet(string requestUri)
    {
        return AuthenticationRequestFactory.CreateBasicGet(
            requestUri,
            Credentials.ConsumerUsername,
            Credentials.ConsumerPassword);
    }

    public HttpRequestMessage CreateAdministratorGet(string requestUri)
    {
        return AuthenticationRequestFactory.CreateBasicGet(
            requestUri,
            Credentials.AdministratorUsername,
            Credentials.AdministratorPassword);
    }

    public HttpRequestMessage CreateCmsGet(string requestUri)
    {
        return AuthenticationRequestFactory.CreateBasicGet(
            requestUri,
            Credentials.CmsUsername,
            Credentials.CmsPassword);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _configuredFactory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }
}
