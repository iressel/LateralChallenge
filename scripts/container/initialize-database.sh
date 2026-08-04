#!/usr/bin/env bash
set -Eeuo pipefail

readonly sqlcmd_path="/opt/mssql-tools18/bin/sqlcmd"

require_value() {
    local variable_name="$1"

    if [[ -z "${!variable_name:-}" ]]; then
        echo "Required database initialization configuration is missing." >&2
        exit 1
    fi
}

validate_sql_password() {
    local password="$1"

    if [[ ${#password} -lt 20 || ${#password} -gt 128 ||
          ! "$password" =~ ^[A-Za-z0-9!@#%^*_.+=,?-]+$ ||
          ! "$password" =~ [A-Z] ||
          ! "$password" =~ [a-z] ||
          ! "$password" =~ [0-9] ||
          ! "$password" =~ [!@#%^*_.+=,?-] ]]; then
        echo "A supplied SQL password does not satisfy the local setup policy." >&2
        exit 1
    fi
}

require_value MSSQL_SA_PASSWORD
require_value MIGRATION_SQL_PASSWORD
require_value WRITE_SQL_PASSWORD
require_value READ_SQL_PASSWORD

validate_sql_password "$MSSQL_SA_PASSWORD"
validate_sql_password "$MIGRATION_SQL_PASSWORD"
validate_sql_password "$WRITE_SQL_PASSWORD"
validate_sql_password "$READ_SQL_PASSWORD"

if [[ "$MSSQL_SA_PASSWORD" == "$MIGRATION_SQL_PASSWORD" ||
      "$MSSQL_SA_PASSWORD" == "$WRITE_SQL_PASSWORD" ||
      "$MSSQL_SA_PASSWORD" == "$READ_SQL_PASSWORD" ||
      "$MIGRATION_SQL_PASSWORD" == "$WRITE_SQL_PASSWORD" ||
      "$MIGRATION_SQL_PASSWORD" == "$READ_SQL_PASSWORD" ||
      "$WRITE_SQL_PASSWORD" == "$READ_SQL_PASSWORD" ]]; then
    echo "SQL passwords must be distinct." >&2
    exit 1
fi

export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"

"$sqlcmd_path" \
    -S sql \
    -U sa \
    -C \
    -b \
    -i /opt/cms-sync/initialize-database.sql

unset SQLCMDPASSWORD
echo "Database initialization completed."
