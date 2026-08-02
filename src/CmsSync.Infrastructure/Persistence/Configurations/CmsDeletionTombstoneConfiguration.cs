using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsDeletionTombstoneConfiguration : IEntityTypeConfiguration<CmsDeletionTombstone>
{
    public void Configure(EntityTypeBuilder<CmsDeletionTombstone> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsDeletionTombstonesTable,
            tableBuilder => tableBuilder.HasCheckConstraint(
                PersistenceConstraintNames.CmsDeletionTombstonesGenerationNonNegative,
                PersistenceConstraintSql.CreateNonNegativeCheck(
                    nameof(CmsDeletionTombstone.LastDeletedGeneration))));

        builder.HasKey(tombstone => tombstone.EntityId);

        builder.Property(tombstone => tombstone.EntityId)
            .HasMaxLength(PersistenceModelConstants.EntityIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();
        builder.Property(tombstone => tombstone.LastDeletedGeneration)
            .HasColumnType(PersistenceModelConstants.BigIntColumnType)
            .IsRequired();
        builder.Property(tombstone => tombstone.DeletedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(tombstone => tombstone.LastDeleteEventKey)
            .HasMaxLength(PersistenceModelConstants.IdempotencyKeyMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(tombstone => tombstone.CreatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(tombstone => tombstone.UpdatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(tombstone => tombstone.RowVersion)
            .IsRowVersion()
            .IsRequired();
    }
}
