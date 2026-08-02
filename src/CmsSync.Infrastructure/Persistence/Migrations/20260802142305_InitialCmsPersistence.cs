using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CmsSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCmsPersistence : Migration
    {
        private static readonly string[] ProcessingLogBatchIdentityColumns =
        [
            "BatchId",
            "Sequence",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CmsDeletionTombstones",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LastDeletedGeneration = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    LastDeleteEventKey = table.Column<string>(type: "nvarchar(209)", maxLength: 209, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsDeletionTombstones", x => x.EntityId);
                    table.CheckConstraint("CK_CmsDeletionTombstones_Generation_NonNegative", "[LastDeletedGeneration] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CmsEntities",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    LatestVersion = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    CmsPublicationStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CurrentVersionOccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    EntityEventHighWatermarkUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    AdministrativeDisabled = table.Column<bool>(type: "bit", nullable: false),
                    AdministrativeStateChangedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    AdministrativeStateChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEntities", x => x.EntityId);
                    table.CheckConstraint("CK_CmsEntities_AdministrativeAudit", "([AdministrativeStateChangedAtUtc] IS NULL AND [AdministrativeStateChangedBy] IS NULL) OR ([AdministrativeStateChangedAtUtc] IS NOT NULL AND [AdministrativeStateChangedBy] IS NOT NULL)");
                    table.CheckConstraint("CK_CmsEntities_EventTimestamps", "[EntityEventHighWatermarkUtc] >= [CurrentVersionOccurredAtUtc]");
                    table.CheckConstraint("CK_CmsEntities_Generation_Positive", "[Generation] > 0");
                    table.CheckConstraint("CK_CmsEntities_LatestVersion_Positive", "[LatestVersion] > 0");
                    table.CheckConstraint("CK_CmsEntities_Payload_JsonObject", "ISJSON([Payload], OBJECT) = 1");
                    table.CheckConstraint("CK_CmsEntities_PublicationStatus", "[CmsPublicationStatus] IN ('Published', 'Unpublished')");
                });

            migrationBuilder.CreateTable(
                name: "CmsEntityRevisions",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    FirstObservedPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    FirstObservedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEntityRevisions", x => new { x.EntityId, x.Generation, x.Version });
                    table.CheckConstraint("CK_CmsEntityRevisions_Generation_Positive", "[Generation] > 0");
                    table.CheckConstraint("CK_CmsEntityRevisions_Payload_JsonObject", "ISJSON([FirstObservedPayload], OBJECT) = 1");
                    table.CheckConstraint("CK_CmsEntityRevisions_Version_Positive", "[Version] > 0");
                });

            migrationBuilder.CreateTable(
                name: "CmsEventProcessingLogs",
                columns: table => new
                {
                    ProcessingLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(209)", maxLength: 209, nullable: true, collation: "Latin1_General_100_BIN2"),
                    OwnsIdempotencyKey = table.Column<bool>(type: "bit", nullable: false),
                    ReplayOfProcessingLogId = table.Column<long>(type: "bigint", nullable: true),
                    ExternalEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    EventContentHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: true),
                    PayloadHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: true),
                    EventType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: true),
                    EventOccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: true),
                    Outcome = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Code = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Generation = table.Column<long>(type: "bigint", nullable: true),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(7)", precision: 7, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AuthenticatedCmsSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsEventProcessingLogs", x => x.ProcessingLogId);
                    table.CheckConstraint("CK_CmsEventProcessingLogs_EventType", "[EventType] IS NULL OR [EventType] IN ('publish', 'unpublish', 'delete')");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_Generation_NonNegative", "[Generation] IS NULL OR [Generation] >= 0");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_IdempotencyOwner", "[OwnsIdempotencyKey] = 0 OR [IdempotencyKey] IS NOT NULL");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_Outcome", "[Outcome] IN ('Applied', 'Duplicate', 'Equivalent', 'Stale', 'Invalid', 'Conflict')");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_ReplayDoesNotOwnIdentity", "[ReplayOfProcessingLogId] IS NULL OR [OwnsIdempotencyKey] = 0");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_ResultingVersion_Positive", "[ResultingVersion] IS NULL OR [ResultingVersion] > 0");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_Sequence_NonNegative", "[Sequence] >= 0");
                    table.CheckConstraint("CK_CmsEventProcessingLogs_Version_Positive", "[Version] IS NULL OR [Version] > 0");
                    table.ForeignKey(
                        name: "FK_CmsEventProcessingLogs_CmsEventProcessingLogs_ReplayOfProcessingLogId",
                        column: x => x.ReplayOfProcessingLogId,
                        principalTable: "CmsEventProcessingLogs",
                        principalColumn: "ProcessingLogId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CmsEventProcessingLogs_ReplayOfProcessingLogId",
                table: "CmsEventProcessingLogs",
                column: "ReplayOfProcessingLogId");

            migrationBuilder.CreateIndex(
                name: "UX_CmsEventProcessingLogs_BatchId_Sequence",
                table: "CmsEventProcessingLogs",
                columns: ProcessingLogBatchIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CmsEventProcessingLogs_IdempotencyOwner",
                table: "CmsEventProcessingLogs",
                column: "IdempotencyKey",
                unique: true,
                filter: "[OwnsIdempotencyKey] = CAST(1 AS bit) AND [IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CmsDeletionTombstones");

            migrationBuilder.DropTable(
                name: "CmsEntities");

            migrationBuilder.DropTable(
                name: "CmsEntityRevisions");

            migrationBuilder.DropTable(
                name: "CmsEventProcessingLogs");
        }
    }
}
