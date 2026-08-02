using CmsSync.Infrastructure.Persistence.Configurations;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace CmsSync.Infrastructure.Persistence;

public sealed class CmsReadDbContext : DbContext
{
    public CmsReadDbContext(DbContextOptions<CmsReadDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<CmsEntityReadModel> CmsEntities => Set<CmsEntityReadModel>();

    public override int SaveChanges()
    {
        throw CreateWriteNotSupportedException();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw CreateWriteNotSupportedException();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        throw CreateWriteNotSupportedException();
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        throw CreateWriteNotSupportedException();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CmsEntityReadModelConfiguration());
    }

    private static NotSupportedException CreateWriteNotSupportedException()
    {
        return new NotSupportedException("CmsReadDbContext is read-only and cannot save changes.");
    }
}
