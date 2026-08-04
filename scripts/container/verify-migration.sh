#!/usr/bin/env bash
set -Eeuo pipefail

: "${MSSQL_SA_PASSWORD:?SQL administrator configuration is required.}"

readonly sqlcmd_path="/opt/mssql-tools18/bin/sqlcmd"
readonly migration_id="20260802142305_InitialCmsPersistence"

export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"

migration_result=$("$sqlcmd_path" \
    -S sql \
    -U sa \
    -d CmsSync \
    -C \
    -b \
    -h -1 \
    -W \
    -Q "SET NOCOUNT ON; SELECT CASE WHEN COUNT(*) = 1 AND SUM(CASE WHEN [MigrationId] = N'$migration_id' THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END FROM [dbo].[__EFMigrationsHistory]")

if [[ ! "$migration_result" =~ ^[[:space:]]*1[[:space:]]*$ ]]; then
    echo "The expected migration was not applied exactly once." >&2
    exit 1
fi

unset SQLCMDPASSWORD
echo "Migration history verification passed."
