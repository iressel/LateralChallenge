using CmsSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddDbContext<CmsWriteDbContext>(options =>
            options.UseSqlServer(writeConnectionString));
        services.AddDbContext<CmsReadDbContext>(options =>
            options.UseSqlServer(readConnectionString));

        return services;
    }
}
