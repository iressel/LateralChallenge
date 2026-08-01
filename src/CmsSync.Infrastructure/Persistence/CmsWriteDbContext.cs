using CmsSync.Infrastructure.Persistence.Configurations;
using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace CmsSync.Infrastructure.Persistence;

public sealed class CmsWriteDbContext(DbContextOptions<CmsWriteDbContext> options) : DbContext(options)
{
    public DbSet<CmsEntity> CmsEntities => Set<CmsEntity>();

    public DbSet<CmsEntityRevision> CmsEntityRevisions => Set<CmsEntityRevision>();

    public DbSet<CmsDeletionTombstone> CmsDeletionTombstones => Set<CmsDeletionTombstone>();

    public DbSet<CmsEventProcessingLog> CmsEventProcessingLogs => Set<CmsEventProcessingLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CmsEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CmsEntityRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new CmsDeletionTombstoneConfiguration());
        modelBuilder.ApplyConfiguration(new CmsEventProcessingLogConfiguration());
    }
}
