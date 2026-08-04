#!/usr/bin/env bash
set -Eeuo pipefail

: "${READ_SQL_PASSWORD:?Read SQL configuration is required.}"

readonly sqlcmd_path="/opt/mssql-tools18/bin/sqlcmd"
readonly common_arguments=(-S sql -U CmsSyncReader -d CmsSync -C -b -o /dev/null)

export SQLCMDPASSWORD="$READ_SQL_PASSWORD"

"$sqlcmd_path" "${common_arguments[@]}" -Q "SELECT TOP (1) [EntityId] FROM [dbo].[CmsEntities]"

assert_denied() {
    local operation_name="$1"
    local statement="$2"

    if "$sqlcmd_path" "${common_arguments[@]}" -Q "$statement" >/dev/null 2>&1; then
        echo "The read principal unexpectedly completed the ${operation_name} probe." >&2
        exit 1
    fi
}

assert_denied \
    "INSERT" \
    "BEGIN TRANSACTION; INSERT INTO [dbo].[CmsDeletionTombstones] ([EntityId], [LastDeletedGeneration], [DeletedAtUtc], [CreatedAtUtc], [UpdatedAtUtc]) VALUES (N'container-read-denied', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()); ROLLBACK TRANSACTION;"
assert_denied \
    "UPDATE" \
    "BEGIN TRANSACTION; UPDATE [dbo].[CmsDeletionTombstones] SET [UpdatedAtUtc] = SYSUTCDATETIME() WHERE [EntityId] = N'container-read-denied'; ROLLBACK TRANSACTION;"
assert_denied \
    "DELETE" \
    "BEGIN TRANSACTION; DELETE FROM [dbo].[CmsDeletionTombstones] WHERE [EntityId] = N'container-read-denied'; ROLLBACK TRANSACTION;"
assert_denied \
    "schema-change" \
    "BEGIN TRANSACTION; CREATE TABLE [dbo].[ContainerReadDenied] ([Id] int NOT NULL); ROLLBACK TRANSACTION;"

unset SQLCMDPASSWORD
echo "Read-principal permission probes passed."
