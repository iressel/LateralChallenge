using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CmsSyncWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _writeConnectionString;
    private readonly string? _readConnectionString;

    public CmsSyncWebApplicationFactory(
        string? writeConnectionString,
        string? readConnectionString)
    {
        _writeConnectionString = writeConnectionString;
        _readConnectionString = readConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>();

            if (_writeConnectionString is not null)
            {
                settings["ConnectionStrings:WriteDatabase"] = _writeConnectionString;
            }

            if (_readConnectionString is not null)
            {
                settings["ConnectionStrings:ReadDatabase"] = _readConnectionString;
            }

            configuration.AddInMemoryCollection(settings);
        });

        if (_writeConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:WriteDatabase", _writeConnectionString);
        }

        if (_readConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:ReadDatabase", _readConnectionString);
        }
    }
}
