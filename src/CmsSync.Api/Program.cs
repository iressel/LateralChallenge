using CmsSync.Api.Errors;
using CmsSync.Api.Health;
using CmsSync.Api.Observability;
using CmsSync.Api.OpenApi;
using CmsSync.Api.Security;
using CmsSync.Api.Webhook;
using CmsSync.Application.EventIngestion;
using CmsSync.Application.Observability;
using CmsSync.Infrastructure;
using CmsSync.Infrastructure.Authentication;
using CmsSync.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var writeConnectionString = GetRequiredConnectionString(builder.Configuration, "WriteDatabase");
var readConnectionString = GetRequiredConnectionString(builder.Configuration, "ReadDatabase");

builder.Services.AddCmsPersistence(writeConnectionString, readConnectionString);
builder.Services.AddCmsAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddCmsSyncOpenApi();
builder.Services.AddSingleton<CmsEventBatchTelemetry>();
builder.Services.AddSingleton<CmsEventIngestionLimits>();
builder.Services.AddSingleton<CmsEventArrayParser>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck(
        "write-database",
        new SqlServerConnectivityHealthCheck(
            writeConnectionString,
            CmsOperationalMetrics.ReadinessWriteOperation),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(3))
    .AddCheck(
        "read-database",
        new SqlServerConnectivityHealthCheck(
            readConnectionString,
            CmsOperationalMetrics.ReadinessReadOperation),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(3));

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);

var app = builder.Build();

app.UseMiddleware<CorrelationContextMiddleware>();
app.UseMiddleware<SafeResponseHeadersMiddleware>();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseMiddleware<SafeRequestLoggingMiddleware>();
app.UseMiddleware<CmsWebhookRequestSizeMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseCmsSyncSwagger();
}

app.MapControllers();
app.MapHealthChecks(
    HealthEndpointRoutes.Liveness,
    new HealthCheckOptions
    {
        AllowCachingResponses = false,
        Predicate = registration => registration.Tags.Contains("live", StringComparer.Ordinal),
        ResponseWriter = SafeHealthResponseWriter.WriteAsync,
    });
app.MapHealthChecks(
    HealthEndpointRoutes.Readiness,
    new HealthCheckOptions
    {
        AllowCachingResponses = false,
        Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal),
        ResponseWriter = SafeHealthResponseWriter.WriteAsync,
    });

app.Run();

static string GetRequiredConnectionString(IConfiguration configuration, string name)
{
    var connectionString = configuration.GetConnectionString(name);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
    }

    try
    {
        var parsedConnectionString = new SqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(parsedConnectionString.DataSource) ||
            string.IsNullOrWhiteSpace(parsedConnectionString.InitialCatalog))
        {
            throw new InvalidOperationException($"ConnectionStrings:{name} is invalid.");
        }
    }
    catch (ArgumentException)
    {
        throw new InvalidOperationException($"ConnectionStrings:{name} is invalid.");
    }

    return connectionString;
}

public partial class Program
{
}
