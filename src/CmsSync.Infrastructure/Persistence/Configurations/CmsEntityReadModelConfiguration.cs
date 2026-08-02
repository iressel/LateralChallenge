using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsEntityReadModelConfiguration : IEntityTypeConfiguration<CmsEntityReadModel>
{
    public void Configure(EntityTypeBuilder<CmsEntityReadModel> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsEntitiesTable,
            tableBuilder => tableBuilder.ExcludeFromMigrations());

        builder.HasKey(entity => entity.EntityId);

        builder.Property(entity => entity.EntityId)
            .HasMaxLength(PersistenceModelConstants.EntityIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();
        builder.Property(entity => entity.Generation)
            .HasColumnType(PersistenceModelConstants.BigIntColumnType)
            .IsRequired();
        builder.Property(entity => entity.LatestVersion)
            .HasColumnType(PersistenceModelConstants.BigIntColumnType)
            .IsRequired();
        builder.Property(entity => entity.Payload)
            .HasColumnType(PersistenceModelConstants.PayloadColumnType)
            .IsRequired();
        builder.Property(entity => entity.CmsPublicationStatus)
            .HasMaxLength(PersistenceModelConstants.PublicationStatusMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.CurrentVersionOccurredAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(entity => entity.EntityEventHighWatermarkUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(entity => entity.AdministrativeDisabled)
            .HasColumnType(PersistenceModelConstants.BitColumnType)
            .IsRequired();
        builder.Property(entity => entity.AdministrativeStateChangedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision);
        builder.Property(entity => entity.AdministrativeStateChangedBy)
            .HasMaxLength(PersistenceModelConstants.AdministrativeSubjectMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
    }
}
