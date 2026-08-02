using CmsSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CmsSync.Api.Persistence.DesignTime;

public sealed class CmsWriteDbContextFactory : IDesignTimeDbContextFactory<CmsWriteDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=design-time.invalid;Database=CmsSyncDesignTimeOnly;Integrated Security=true;" +
        "Encrypt=true;TrustServerCertificate=false";

    public CmsWriteDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CmsWriteDbContext>();
        optionsBuilder.UseSqlServer(
            DesignTimeConnectionString,
            sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                typeof(CmsWriteDbContext).Assembly.GetName().Name));

        return new CmsWriteDbContext(optionsBuilder.Options);
    }
}
