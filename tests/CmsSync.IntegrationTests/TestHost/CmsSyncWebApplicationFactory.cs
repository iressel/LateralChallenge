using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CmsSyncWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _writeConnectionString;
    private readonly string? _readConnectionString;
    private readonly IReadOnlyDictionary<string, string?>? _lowerPriorityConfiguration;
    private readonly IReadOnlyDictionary<string, string?>? _credentialOverrides;
    private readonly bool _includeCredentials;
    private readonly string? _environmentName;

    public CmsSyncWebApplicationFactory(
        string? writeConnectionString,
        string? readConnectionString,
        IReadOnlyDictionary<string, string?>? lowerPriorityConfiguration = null,
        IReadOnlyDictionary<string, string?>? credentialOverrides = null,
        bool includeCredentials = true,
        string? environmentName = null)
    {
        _writeConnectionString = writeConnectionString;
        _readConnectionString = readConnectionString;
        _lowerPriorityConfiguration = lowerPriorityConfiguration;
        _credentialOverrides = credentialOverrides;
        _includeCredentials = includeCredentials;
        _environmentName = environmentName;
        Credentials = TestCredentialSet.Create();
    }

    public TestCredentialSet Credentials { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var configurationBuilder = new ConfigurationBuilder();

        if (_lowerPriorityConfiguration is not null)
        {
            configurationBuilder.AddInMemoryCollection(_lowerPriorityConfiguration);
        }

        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:WriteDatabase"] = _writeConnectionString ?? string.Empty,
            ["ConnectionStrings:ReadDatabase"] = _readConnectionString ?? string.Empty,
        };

        if (_includeCredentials)
        {
            foreach (var credential in Credentials.CreateConfiguration())
            {
                overrides[credential.Key] = credential.Value;
            }
        }

        if (_credentialOverrides is not null)
        {
            foreach (var credentialOverride in _credentialOverrides)
            {
                overrides[credentialOverride.Key] = credentialOverride.Value;
            }
        }

        configurationBuilder.AddInMemoryCollection(overrides);

        var configuration = configurationBuilder.Build();
        builder.UseConfiguration(configuration);
        builder.ConfigureServices(services =>
        {
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
        });

        if (!string.IsNullOrWhiteSpace(_environmentName))
        {
            builder.UseEnvironment(_environmentName);
        }
    }
}
