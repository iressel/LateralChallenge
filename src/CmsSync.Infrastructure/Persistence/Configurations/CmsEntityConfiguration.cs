using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsEntityConfiguration : IEntityTypeConfiguration<CmsEntity>
{
    public void Configure(EntityTypeBuilder<CmsEntity> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsEntitiesTable,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesGenerationPositive,
                    PersistenceConstraintSql.CreatePositiveCheck(nameof(CmsEntity.Generation)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesLatestVersionPositive,
                    PersistenceConstraintSql.CreatePositiveCheck(nameof(CmsEntity.LatestVersion)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesPayloadJsonObject,
                    PersistenceConstraintSql.CreateJsonObjectCheck(nameof(CmsEntity.Payload)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesPublicationStatus,
                    "[CmsPublicationStatus] IN ('Published', 'Unpublished')");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesEventTimestamps,
                    "[EntityEventHighWatermarkUtc] >= [CurrentVersionOccurredAtUtc]");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEntitiesAdministrativeAudit,
                    "([AdministrativeStateChangedAtUtc] IS NULL AND [AdministrativeStateChangedBy] IS NULL) OR " +
                    "([AdministrativeStateChangedAtUtc] IS NOT NULL AND [AdministrativeStateChangedBy] IS NOT NULL)");
            });

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
        builder.Property(entity => entity.PayloadHash)
            .HasColumnType(PersistenceModelConstants.HashColumnType)
            .HasMaxLength(PersistenceModelConstants.HashLength)
            .IsFixedLength()
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
        builder.Property(entity => entity.CreatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
            .IsRequired();
        builder.Property(entity => entity.RowVersion)
            .IsRowVersion()
            .IsRequired();
    }
}
