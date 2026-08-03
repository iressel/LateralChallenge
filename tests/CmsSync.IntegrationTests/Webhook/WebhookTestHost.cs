using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CmsSync.IntegrationTests.Webhook;

internal sealed class WebhookTestHost : IAsyncDisposable
{
    private readonly CmsSyncWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _configuredFactory;
    private readonly List<HttpClient> _clients = [];

    private WebhookTestHost(
        CmsSyncWebApplicationFactory baseFactory,
        WebApplicationFactory<Program> configuredFactory)
    {
        _baseFactory = baseFactory;
        _configuredFactory = configuredFactory;
        Client = CreateIndependentClient();
    }

    public HttpClient Client { get; }

    public TestCredentialSet Credentials => _baseFactory.Credentials;

    public IServiceProvider Services => _configuredFactory.Services;

    public static WebhookTestHost Create(
        SqlServerFixture fixture,
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

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });

        return new WebhookTestHost(baseFactory, configuredFactory);
    }

    public HttpClient CreateIndependentClient()
    {
        var client = _configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
        _clients.Add(client);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _configuredFactory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }
}
