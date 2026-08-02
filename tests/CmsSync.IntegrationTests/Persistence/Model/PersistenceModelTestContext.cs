using CmsSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CmsSync.IntegrationTests.Persistence.Model;

internal static class PersistenceModelTestContext
{
    private const string MetadataOnlyConnectionString =
        "Server=metadata-only.invalid;Database=CmsSyncMetadataOnly;Integrated Security=true;" +
        "Encrypt=true;TrustServerCertificate=false";

    public static CmsWriteDbContext CreateWriteContext()
    {
        var options = new DbContextOptionsBuilder<CmsWriteDbContext>()
            .UseSqlServer(MetadataOnlyConnectionString)
            .Options;

        return new CmsWriteDbContext(options);
    }

    public static CmsReadDbContext CreateReadContext()
    {
        var options = new DbContextOptionsBuilder<CmsReadDbContext>()
            .UseSqlServer(MetadataOnlyConnectionString)
            .Options;

        return new CmsReadDbContext(options);
    }

    public static IEntityType GetRequiredEntityType<TEntity>(IModel model)
    {
        return model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"The EF model does not contain {typeof(TEntity).Name}.");
    }

    public static IModel GetDesignTimeModel(DbContext context)
    {
        return context.GetService<IDesignTimeModel>().Model;
    }

    public static IProperty GetRequiredProperty(IEntityType entityType, string propertyName)
    {
        return entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"The EF model for {entityType.ClrType.Name} does not contain {propertyName}.");
    }
}
