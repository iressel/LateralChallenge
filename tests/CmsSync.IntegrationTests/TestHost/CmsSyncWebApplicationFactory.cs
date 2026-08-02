using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CmsSyncWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _writeConnectionString;
    private readonly string? _readConnectionString;
    private readonly IReadOnlyDictionary<string, string?>? _lowerPriorityConfiguration;

    public CmsSyncWebApplicationFactory(
        string? writeConnectionString,
        string? readConnectionString,
        IReadOnlyDictionary<string, string?>? lowerPriorityConfiguration = null)
    {
        _writeConnectionString = writeConnectionString;
        _readConnectionString = readConnectionString;
        _lowerPriorityConfiguration = lowerPriorityConfiguration;
    }

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

        configurationBuilder.AddInMemoryCollection(overrides);

        var configuration = configurationBuilder.Build();
        builder.UseConfiguration(configuration);
    }
}
