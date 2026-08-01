using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsEntityRevisionConfiguration : IEntityTypeConfiguration<CmsEntityRevision>
{
    public void Configure(EntityTypeBuilder<CmsEntityRevision> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsEntityRevisionsTable,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntityRevisions_Generation_Positive",
                    "[Generation] > 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntityRevisions_Version_Positive",
                    "[Version] > 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEntityRevisions_Payload_JsonObject",
                    PersistenceModelConstants.CreateJsonObjectCheck(nameof(CmsEntityRevision.FirstObservedPayload)));
            });

        builder.HasKey(revision => new
        {
            revision.EntityId,
            revision.Generation,
            revision.Version,
        });

        builder.Property(revision => revision.EntityId)
            .HasMaxLength(PersistenceModelConstants.EntityIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();
        builder.Property(revision => revision.Generation)
            .HasColumnType("bigint")
            .IsRequired();
        builder.Property(revision => revision.Version)
            .HasColumnType("bigint")
            .IsRequired();

        var payloadProperty = builder.Property(revision => revision.FirstObservedPayload)
            .HasColumnType(PersistenceModelConstants.PayloadColumnType)
            .IsRequired();
        payloadProperty.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        var hashProperty = builder.Property(revision => revision.PayloadHash)
            .HasColumnType(PersistenceModelConstants.HashColumnType)
            .HasMaxLength(PersistenceModelConstants.HashLength)
            .IsFixedLength()
            .IsRequired();
        hashProperty.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        var timestampProperty = builder.Property(revision => revision.FirstObservedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        timestampProperty.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
