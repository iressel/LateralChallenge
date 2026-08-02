using CmsSync.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var writeConnectionString = GetRequiredConnectionString(builder.Configuration, "WriteDatabase");
var readConnectionString = GetRequiredConnectionString(builder.Configuration, "ReadDatabase");

builder.Services.AddCmsPersistence(writeConnectionString, readConnectionString);

var app = builder.Build();

app.Run();

static string GetRequiredConnectionString(IConfiguration configuration, string name)
{
    var connectionString = configuration.GetConnectionString(name);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
    }

    return connectionString;
}

public partial class Program
{
}
