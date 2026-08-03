using CmsSync.Api.Entities;
using CmsSync.Api.Webhook;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure;
using CmsSync.Infrastructure.Authentication;

var builder = WebApplication.CreateBuilder(args);

var writeConnectionString = GetRequiredConnectionString(builder.Configuration, "WriteDatabase");
var readConnectionString = GetRequiredConnectionString(builder.Configuration, "ReadDatabase");

builder.Services.AddCmsPersistence(writeConnectionString, readConnectionString);
builder.Services.AddCmsAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<CmsEventIngestionLimits>();
builder.Services.AddSingleton<CmsEventArrayParser>();

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<CmsWebhookRequestSizeMiddleware>();
app.UseMiddleware<CmsEntityResponseSecurityMiddleware>();
app.UseMiddleware<AuthenticationResponseSecurityMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapCmsEvents();
app.MapCmsEntities();

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
