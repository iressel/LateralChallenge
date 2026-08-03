using System.Net.Http.Headers;
using System.Text;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal sealed class AdministrativeStateTestHost : IAsyncDisposable
{
    private readonly CmsSyncWebApplicationFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _configuredFactory;
    private readonly List<HttpClient> _clients = [];

    private AdministrativeStateTestHost(
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

    public static AdministrativeStateTestHost Create(
        SqlServerFixture fixture,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var baseFactory = new CmsSyncWebApplicationFactory(
            fixture.WriteConnectionString,
            fixture.ReadConnectionString);
        var configuredFactory = baseFactory.WithWebHostBuilder(builder =>
        {
            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });

        return new AdministrativeStateTestHost(baseFactory, configuredFactory);
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

    public HttpRequestMessage CreateAdministratorPut(string entityId, string json)
    {
        return CreateAuthenticatedPut(
            entityId,
            json,
            Credentials.AdministratorUsername,
            Credentials.AdministratorPassword);
    }

    public HttpRequestMessage CreateConsumerPut(string entityId, string json)
    {
        return CreateAuthenticatedPut(
            entityId,
            json,
            Credentials.ConsumerUsername,
            Credentials.ConsumerPassword);
    }

    public HttpRequestMessage CreateCmsPut(string entityId, string json)
    {
        return CreateAuthenticatedPut(
            entityId,
            json,
            Credentials.CmsUsername,
            Credentials.CmsPassword);
    }

    public static HttpRequestMessage CreateUnauthenticatedPut(string entityId, string json)
    {
        return CreatePut(entityId, json);
    }

    public static HttpRequestMessage CreatePutWithAuthorization(
        string entityId,
        string json,
        string scheme,
        string? parameter)
    {
        var request = CreatePut(entityId, json);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);
        return request;
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

    private static HttpRequestMessage CreateAuthenticatedPut(
        string entityId,
        string json,
        string username,
        string password)
    {
        var request = CreatePut(entityId, json);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            AuthenticationRequestFactory.CreateBasicParameter(username, password));
        return request;
    }

    private static HttpRequestMessage CreatePut(string entityId, string json)
    {
        return new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/entities/{Uri.EscapeDataString(entityId)}/administrative-state")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
