using CmsSync.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CmsSync.Infrastructure.Persistence.Configurations;

internal sealed class CmsEventProcessingLogConfiguration : IEntityTypeConfiguration<CmsEventProcessingLog>
{
    public void Configure(EntityTypeBuilder<CmsEventProcessingLog> builder)
    {
        builder.ToTable(
            PersistenceModelConstants.CmsEventProcessingLogsTable,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsSequenceNonNegative,
                    PersistenceConstraintSql.CreateNonNegativeCheck(nameof(CmsEventProcessingLog.Sequence)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsIdempotencyOwner,
                    "[OwnsIdempotencyKey] = 0 OR [IdempotencyKey] IS NOT NULL");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsReplayDoesNotOwnIdentity,
                    "[ReplayOfProcessingLogId] IS NULL OR [OwnsIdempotencyKey] = 0");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsEventType,
                    "[EventType] IS NULL OR [EventType] IN ('publish', 'unpublish', 'delete')");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsOutcome,
                    "[Outcome] IN ('Applied', 'Duplicate', 'Equivalent', 'Stale', 'Invalid', 'Conflict')");
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsVersionPositive,
                    PersistenceConstraintSql.CreateNullablePositiveCheck(nameof(CmsEventProcessingLog.Version)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsGenerationNonNegative,
                    PersistenceConstraintSql.CreateNullableNonNegativeCheck(nameof(CmsEventProcessingLog.Generation)));
                tableBuilder.HasCheckConstraint(
                    PersistenceConstraintNames.CmsEventProcessingLogsResultingVersionPositive,
                    PersistenceConstraintSql.CreateNullablePositiveCheck(
                        nameof(CmsEventProcessingLog.ResultingVersion)));
            });

        builder.HasKey(log => log.ProcessingLogId);

        builder.Property(log => log.ProcessingLogId)
            .ValueGeneratedOnAdd();
        builder.Property(log => log.BatchId)
            .HasColumnType(PersistenceModelConstants.UniqueIdentifierColumnType)
            .IsRequired();
        builder.Property(log => log.Sequence)
            .HasColumnType(PersistenceModelConstants.IntegerColumnType)
            .IsRequired();
        builder.Property(log => log.IdempotencyKey)
            .HasMaxLength(PersistenceModelConstants.IdempotencyKeyMaxLength)
            .UseCollation(PersistenceModelConstants.CaseSensitiveCollation);
        builder.Property(log => log.OwnsIdempotencyKey)
            .HasColumnType(PersistenceModelConstants.BitColumnType)
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
            .HasColumnType(PersistenceModelConstants.BigIntColumnType);
        builder.Property(log => log.EventOccurredAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision);
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
            .HasColumnType(PersistenceModelConstants.BigIntColumnType);
        builder.Property(log => log.ResultingVersion)
            .HasColumnType(PersistenceModelConstants.BigIntColumnType);
        builder.Property(log => log.ProcessedAtUtc)
            .HasColumnType(PersistenceModelConstants.DateTimeColumnType)
            .HasPrecision(PersistenceModelConstants.DateTimePrecision)
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
            .HasDatabaseName(PersistenceIndexNames.CmsEventProcessingLogsBatchIdSequence);
        builder.HasIndex(log => log.IdempotencyKey)
            .IsUnique()
            .HasFilter(PersistenceIndexFilters.CmsEventProcessingLogsIdempotencyOwner)
            .HasDatabaseName(PersistenceIndexNames.CmsEventProcessingLogsIdempotencyOwner);

        builder.HasOne<CmsEventProcessingLog>()
            .WithMany()
            .HasForeignKey(log => log.ReplayOfProcessingLogId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
