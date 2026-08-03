using CmsSync.Application.Abstractions;
using CmsSync.Application.AdministrativeState;
using CmsSync.Application.EventIngestion;
using CmsSync.Application.Observability;
using CmsSync.Infrastructure.Observability;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.EventProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CmsSync.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCmsPersistence(
        this IServiceCollection services,
        string writeConnectionString,
        string readConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(writeConnectionString))
        {
            throw new ArgumentException(
                "The write database connection string is required.",
                nameof(writeConnectionString));
        }

        if (string.IsNullOrWhiteSpace(readConnectionString))
        {
            throw new ArgumentException(
                "The read database connection string is required.",
                nameof(readConnectionString));
        }

        var writeTelemetry = new SqlServerTelemetryCommandInterceptor(
            CmsOperationalMetrics.WriteDatabaseOperation);
        var readTelemetry = new SqlServerTelemetryCommandInterceptor(
            CmsOperationalMetrics.ReadDatabaseOperation);

        services.AddDbContext<CmsWriteDbContext>(options =>
            options
                .UseSqlServer(
                    writeConnectionString,
                    sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(2),
                        errorNumbersToAdd: [1205]))
                .AddInterceptors(writeTelemetry));
        services.AddDbContext<CmsReadDbContext>(options =>
            options
                .UseSqlServer(readConnectionString)
                .AddInterceptors(readTelemetry));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EventValidator>();
        services.TryAddSingleton<SqlServerEntityApplicationLock>();
        services.AddScoped<IEventTransactionExecutor, SqlServerEventTransactionExecutor>();
        services.AddScoped<ICmsEntityQueries, CmsEntityQueries>();
        services.AddScoped<IAdministrativeStateService, CmsAdministrativeStateService>();
        services.AddScoped<CmsEventBatchService>();

        return services;
    }
}
