using CmsSync.Infrastructure;
using CmsSync.Infrastructure.Authentication;

var builder = WebApplication.CreateBuilder(args);

var writeConnectionString = GetRequiredConnectionString(builder.Configuration, "WriteDatabase");
var readConnectionString = GetRequiredConnectionString(builder.Configuration, "ReadDatabase");

builder.Services.AddCmsPersistence(writeConnectionString, readConnectionString);
builder.Services.AddCmsAuthentication(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<AuthenticationResponseSecurityMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

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
