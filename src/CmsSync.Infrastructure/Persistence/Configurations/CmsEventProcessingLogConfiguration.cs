using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsEventProcessingLogConfiguration : IEntityTypeConfiguration<CmsEventProcessingLog>
{
    public const string IdempotencyOwnerFilter =
        "[OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL";

    public void Configure(EntityTypeBuilder<CmsEventProcessingLog> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_Sequence_NonNegative",
                    "[Sequence] >= 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_IdempotencyOwner",
                    "[OwnsIdempotencyKey] = 0 OR [IdempotencyKey] IS NOT NULL");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_ReplayDoesNotOwnIdentity",
                    "[ReplayOfProcessingLogId] IS NULL OR [OwnsIdempotencyKey] = 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_EventType",
                    "[EventType] IS NULL OR [EventType] IN ('publish', 'unpublish', 'delete')");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_Outcome",
                    "[Outcome] IN ('Applied', 'Duplicate', 'Equivalent', 'Stale', 'Invalid', 'Conflict')");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_Version_Positive",
                    "[Version] IS NULL OR [Version] > 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_Generation_NonNegative",
                    "[Generation] IS NULL OR [Generation] >= 0");
                tableBuilder.HasCheckConstraint(
                    "CK_CmsEventProcessingLogs_ResultingVersion_Positive",
                    "[ResultingVersion] IS NULL OR [ResultingVersion] > 0");
            });

        builder.HasKey(log => log.ProcessingLogId);

        builder.Property(log => log.ProcessingLogId)
            .ValueGeneratedOnAdd();
        builder.Property(log => log.BatchId)
            .HasColumnType("uniqueidentifier")
            .IsRequired();
        builder.Property(log => log.Sequence)
            .HasColumnType("int")
            .IsRequired();
        builder.Property(log => log.IdempotencyKey)
            .HasMaxLength(PersistenceModelConstants.IdempotencyKeyMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(log => log.OwnsIdempotencyKey)
            .HasColumnType("bit")
            .IsRequired();
        builder.Property(log => log.ExternalEventId)
            .HasMaxLength(PersistenceModelConstants.ExternalEventIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(log => log.EventContentHash)
            .HasColumnType(PersistenceModelConstants.HashColumnType)
            .HasMaxLength(PersistenceModelConstants.HashLength)
            .IsFixedLength();
        builder.Property(log => log.PayloadHash)
            .HasColumnType(PersistenceModelConstants.HashColumnType)
            .HasMaxLength(PersistenceModelConstants.HashLength)
            .IsFixedLength();
        builder.Property(log => log.EventType)
            .HasMaxLength(PersistenceModelConstants.EventTypeMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsUnicode(false);
        builder.Property(log => log.EntityId)
            .HasMaxLength(PersistenceModelConstants.EntityIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(log => log.Version)
            .HasColumnType("bigint");
        builder.Property(log => log.EventOccurredAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7);
        builder.Property(log => log.Outcome)
            .HasMaxLength(PersistenceModelConstants.ProcessingOutcomeMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(log => log.Code)
            .HasMaxLength(PersistenceModelConstants.ProcessingCodeMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(log => log.Generation)
            .HasColumnType("bigint");
        builder.Property(log => log.ResultingVersion)
            .HasColumnType("bigint");
        builder.Property(log => log.ProcessedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(7)
            .IsRequired();
        builder.Property(log => log.CorrelationId)
            .HasMaxLength(PersistenceModelConstants.CorrelationIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();
        builder.Property(log => log.AuthenticatedCmsSubject)
            .HasMaxLength(PersistenceModelConstants.CmsSubjectIdentifierMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation)
            .IsRequired();

        builder.HasIndex(log => new { log.BatchId, log.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_CmsEventProcessingLogs_BatchId_Sequence");
        builder.HasIndex(log => log.IdempotencyKey)
            .IsUnique()
            .HasFilter(IdempotencyOwnerFilter)
            .HasDatabaseName("UX_CmsEventProcessingLogs_IdempotencyOwner");

        builder.HasOne<CmsEventProcessingLog>()
            .WithMany()
            .HasForeignKey(log => log.ReplayOfProcessingLogId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
