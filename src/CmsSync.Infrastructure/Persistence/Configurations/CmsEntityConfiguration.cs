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
                    "CK_CmsEntities_Generation_Positive",
                    "[Generation] > 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntities_LatestVersion_Positive",
                    "[LatestVersion] > 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntities_Payload_JsonObject",
                    PersistenceModelConstants.CreateJsonObjectCheck(nameof(CmsEntity.Payload)));
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntities_PublicationStatus",
                    "[CmsPublicationStatus] IN ('Published', 'Unpublished')");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntities_EventTimestamps",
                    "[EntityEventHighWatermarkUtc] >= [CurrentVersionOccurredAtUtc]");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntities_AdministrativeAudit",
                    "([AdministrativeStateChangedAtUtc] IS NULL AND [AdministrativeStateChangedBy] IS NULL) OR " +
                    "([AdministrativeStateChangedAtUtc] IS NOT NULL AND [AdministrativeStateChangedBy] IS NOT NULL)");
            });

        builder.HasKey(entity => entity.EntityId);

        builder.Property(entity => entity.EntityId)
            .HasMaxLength(PersistenceModelConstants.EntityIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();
        builder.Property(entity => entity.Generation)
            .HasColumnType("bigint")
            .IsRequired();
        builder.Property(entity => entity.LatestVersion)
            .HasColumnType("bigint")
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
            .IsUnicode(false)
            .IsRequired();
        builder.Property(entity => entity.CurrentVersionOccurredAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(entity => entity.EntityEventHighWatermarkUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(entity => entity.AdministrativeDisabled)
            .HasColumnType("bit")
            .IsRequired();
        builder.Property(entity => entity.AdministrativeStateChangedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7);
        builder.Property(entity => entity.AdministrativeStateChangedBy)
            .HasMaxLength(PersistenceModelConstants.AdministrativeSubjectMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(entity => entity.CreatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(entity => entity.RowVersion)
            .IsRowVersion()
            .IsRequired();
    }
}
