# Container development

## Supported Compose path

The complete local Compose path is supported on an Intel/AMD x86-64 Docker host. It uses the repository's pinned SQL Server image, creates three distinct database principals, applies the existing EF Core migration in a dedicated one-shot service, and starts the API only after migration succeeds.

1. Copy `.env.example` to the ignored `.env` file.
2. Replace every descriptive placeholder outside source control. Use four distinct SQL passwords containing 20 to 128 characters from letters, digits, and `!@#%^*_.+=,?-`. Use three distinct GUID-format actor passwords and three distinct usernames; the CMS username must contain 10 to 20 characters.
3. Validate interpolation without rendering secrets: `docker compose config --quiet`.
4. Start from a clean database and wait for readiness: `docker compose down --volumes --remove-orphans`, then `docker compose up --build --wait`.
5. Verify `http://localhost:8080/health/live` and `http://localhost:8080/health/ready`, or the configured `CMS_API_PORT` equivalents.
6. Inspect bounded service state with `docker compose ps`.
7. Stop while retaining local database state with `docker compose down --remove-orphans`.
8. Stop and reset to a clean database with `docker compose down --volumes --remove-orphans`.

The named `cms-sync-sql-data` volume persists `/var/opt/mssql` across ordinary shutdowns. The `db-init` service idempotently creates the database and fixed login/user names using runtime passwords. The `migration` service uses the separate migration principal and the repository-local `dotnet-ef` 10.0.10 tool, applies migrations owned by `CmsWriteDbContext`, and exits. The API never applies migrations during normal startup. It uses the write principal for `WriteDatabase` and the SELECT-only principal for `ReadDatabase`; SA is limited to local SQL initialization.

For the deterministic clean-volume verification used by this task and reusable by CI, run `pwsh ./scripts/validate-container-setup.ps1`. The script generates all credentials in memory, validates liveness/readiness, migration history, SQL permissions, an authenticated consumer read, and a real webhook write, then always removes project containers and volumes.

## Optional Aspire local orchestration path

The Aspire AppHost path is an additional local workflow. It does not replace Docker Compose support.

1. Confirm Aspire CLI 13.4.0 is available: `aspire --version`.
2. Configure local Aspire secrets: `pwsh ./scripts/configure-aspire-local.ps1`.
3. Start an isolated local Aspire run: `aspire start --apphost ./apphost.cs --isolated --non-interactive`.
4. Check chained readiness: `aspire wait sql --status healthy --timeout 480 --apphost ./apphost.cs --non-interactive`, `aspire wait db-init --status down --timeout 480 --apphost ./apphost.cs --non-interactive`, `aspire wait migration --status down --timeout 480 --apphost ./apphost.cs --non-interactive`, and `aspire wait api --status healthy --timeout 480 --apphost ./apphost.cs --non-interactive`.
5. Verify orchestration state and endpoints through AppHost output: `aspire describe --apphost ./apphost.cs --format Json --non-interactive`.
6. Run deterministic Aspire smoke validation and cleanup: `pwsh ./scripts/validate-aspire-setup.ps1`.
7. Stop AppHost resources after manual runs: `aspire stop --apphost ./apphost.cs --non-interactive`.

## Apple Silicon

The SQL Server Linux container path is not supported through Rosetta, QEMU, or any other emulation or translation layer. Do not add `platform: linux/amd64` as a workaround. SQL Server Testcontainers are not the local verification path on Apple Silicon.

Use a remote supported SQL Server instance or Azure SQL instead. Supply `ConnectionStrings__WriteDatabase` and `ConnectionStrings__ReadDatabase` through local secrets or environment variables, never source control. Run the dedicated migration step with a migration-capable remote connection, use an application write principal for normal writes, and use a SELECT-only principal for `ReadDatabase`. Run the API locally or in an architecture-compatible API container. Coordinate remote database creation, firewall access, TLS trust, and secret distribution with the database operator; no organization-specific host or reusable connection string belongs in the repository.

For production, provision and rotate migration, write, and read credentials independently. Grant migration permissions only to the deployment step, grant the API write principal schema-level DML needed by event and administrative processing, and grant the read principal SELECT only. Do not run the normal API as SA or with migration permissions.
