#!/usr/bin/env bash
set -Eeuo pipefail

: "${MIGRATION_SQL_PASSWORD:?Migration SQL configuration is required.}"

export SQLCMDPASSWORD="$MIGRATION_SQL_PASSWORD"

/opt/mssql-tools18/bin/sqlcmd \
    -S sql \
    -U CmsSyncMigration \
    -d CmsSync \
    -C \
    -b \
    -I \
    -i /opt/cms-sync/migrations.sql

unset SQLCMDPASSWORD
echo "Database migration completed."
