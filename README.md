# LateralChallenge

This repository implements the CMS event ingestion and entity visibility challenge using ASP.NET Core, EF Core, and SQL Server.

## 1. Repository scope

- Source-of-truth requirements: [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md)
- Architecture and implementation plan: [specs/cms-event-ingestion/plan.md](specs/cms-event-ingestion/plan.md)
- Execution checklist and task boundaries: [specs/cms-event-ingestion/tasks.md](specs/cms-event-ingestion/tasks.md)
- Agent guardrails for this repository: [AGENTS.md](AGENTS.md)

This README documents the implemented behavior and operational constraints from the current code and tests.

## 2. Implemented stack and constraints

- Runtime: .NET 10
- API: ASP.NET Core minimal APIs
- Persistence: EF Core 10 with SQL Server only
- Solution file: [LateralChallenge.sln](LateralChallenge.sln)
- Production database provider: Microsoft SQL Server only
- Required app connection strings:
	- `ConnectionStrings:WriteDatabase`
	- `ConnectionStrings:ReadDatabase`

## 3. Prerequisites

- Windows/macOS/Linux with .NET SDK from [global.json](global.json)
- Docker Engine and Docker Compose
- Supported local Compose path requires an x86-64 Docker host

For platform-specific behavior and Apple Silicon constraints, see [docs/container-development.md](docs/container-development.md).

## 4. Secrets and local configuration

1. Copy [.env.example](.env.example) to `.env`.
2. Replace every placeholder with local secrets that stay out of source control.
3. Keep all four SQL passwords distinct and strong.
4. Keep actor usernames distinct.
5. Use GUID `D` format passwords for CMS, Consumer, and Administrator credentials.

Configuration keys used by Compose and runtime:

- `MSSQL_SA_PASSWORD`
- `MIGRATION_SQL_PASSWORD`
- `WRITE_SQL_PASSWORD`
- `READ_SQL_PASSWORD`
- `Authentication__Credentials__Cms__Username`
- `Authentication__Credentials__Cms__Password`
- `Authentication__Credentials__Consumer__Username`
- `Authentication__Credentials__Consumer__Password`
- `Authentication__Credentials__Administrator__Username`
- `Authentication__Credentials__Administrator__Password`
- `SQL_SERVER_PORT` (optional)
- `CMS_API_PORT` (optional)
- `SQL_SERVER_IMAGE` (optional override, keep default pinned value)

## 5. Supported local startup (x86-64 Compose)

Compose artifacts:

- [compose.yaml](compose.yaml)
- [Dockerfile](Dockerfile)
- [scripts/container/initialize-database.sh](scripts/container/initialize-database.sh)
- [scripts/container/apply-migrations.sh](scripts/container/apply-migrations.sh)
- [scripts/container/verify-migration.sh](scripts/container/verify-migration.sh)
- [scripts/container/verify-read-only.sh](scripts/container/verify-read-only.sh)

Run sequence:

```powershell
docker compose config --quiet
docker compose down --volumes --remove-orphans
docker compose up --build --wait
docker compose ps
```

Health probes:

- `http://localhost:8080/health/live`
- `http://localhost:8080/health/ready`

Cleanup:

```powershell
docker compose down --remove-orphans
docker compose down --volumes --remove-orphans
```

Deterministic clean-volume validation script:

```powershell
pwsh ./scripts/validate-container-setup.ps1
```

## 6. Optional API startup against an existing SQL Server

When Compose is not your runtime path, provide all required configuration (connection strings and authentication credentials) by environment variables or local user-secrets, then run:

```powershell
dotnet restore LateralChallenge.sln --source https://api.nuget.org/v3/index.json
dotnet build LateralChallenge.sln --configuration Release --no-restore
dotnet run --project src/CmsSync.Api/CmsSync.Api.csproj --configuration Release
```

The API requires both `ConnectionStrings:WriteDatabase` and `ConnectionStrings:ReadDatabase`.

## 7. Authentication and authorization contract

Implementation sources:

- [src/CmsSync.Infrastructure/Authentication/AuthenticationConstants.cs](src/CmsSync.Infrastructure/Authentication/AuthenticationConstants.cs)
- [src/CmsSync.Infrastructure/Authentication/AuthenticationRegistration.cs](src/CmsSync.Infrastructure/Authentication/AuthenticationRegistration.cs)
- [src/CmsSync.Infrastructure/Authentication/BasicAuthenticationHandler.cs](src/CmsSync.Infrastructure/Authentication/BasicAuthenticationHandler.cs)

Schemes:

- `CmsBasic`
- `ConsumerBasic`

Policies:

- `CmsEvents` (CMS webhook)
- `ConsumerAccess` (read endpoints)
- `AdministratorAccess` (administrative disable endpoint)

Roles:

- `CmsService`
- `NormalConsumer`
- `Administrator`

Challenge/forbid behavior:

- Missing/malformed/invalid credentials for an endpoint scheme return `401` with `Basic realm="<scheme>"`.
- Valid normal-consumer credentials on administrator-only endpoint return `403` with no challenge header.
- Cross-scheme credentials return `401` (not `403`).
- HTTPS redirection is enforced outside the Development environment.

## 8. CMS webhook endpoint contract

Endpoint:

- `POST /cms/events`

Accepted media types:

- `application/json`
- `application/*+json`

Raw-array requirement:

- Top-level JSON must be a raw array.
- No `{ "events": [...] }` wrapper.

Webhook event property names are case-sensitive. The external entity property is exactly `id`.

### Example: raw webhook array request

```json
[
	{
		"eventId": "<choose-a-distinct-example-event-id-1>",
		"type": "Publish",
		"id": "entity-ac057",
		"version": 5,
		"timestamp": "2026-08-02T10:00:00Z",
		"payload": {
			"value": 5,
			"source": "documentation-example"
		}
	},
	{
		"eventId": "<choose-a-distinct-example-event-id-2>",
		"type": "  unPublish  ",
		"id": "entity-ac057",
		"version": 6,
		"timestamp": "2026-08-02T09:00:00Z",
		"payload": {
			"value": 6,
			"source": "documentation-example"
		}
	},
	{
		"type": "DELETE",
		"id": "entity-ac057",
		"timestamp": "2026-08-02T10:00:00.0000001Z"
	}
]
```

The example intentionally mixes event-type casing and surrounding whitespace, and it shows both present and omitted `eventId` values.

## 9. Webhook validation rules and limits

Implementation sources:

- [src/CmsSync.Application/EventIngestion/CmsEventIngestionLimits.cs](src/CmsSync.Application/EventIngestion/CmsEventIngestionLimits.cs)
- [src/CmsSync.Application/EventIngestion/EventValidator.cs](src/CmsSync.Application/EventIngestion/EventValidator.cs)
- [src/CmsSync.Api/Webhook/CmsWebhookRequestSizeMiddleware.cs](src/CmsSync.Api/Webhook/CmsWebhookRequestSizeMiddleware.cs)

Limits:

- Request size: 16 MiB
- Batch size: 1..50 events
- Payload size (per versioned event): 256 KiB
- Maximum JSON depth: 64
- Identifier length (`id`, `eventId`): 1..200 characters

Validation highlights:

- `type` is trimmed and case-insensitively normalized to canonical `publish`, `unpublish`, or `delete`.
- `entityId` is not accepted in the webhook request contract.
- `timestamp` must include `Z` or explicit `+/-HH:MM` offset and at most 7 fractional digits.
- `version` must be a positive integer for publish/unpublish.
- `delete` must not include `version` or `payload`.
- Duplicate JSON property names inside an event (including `payload`) make that item invalid.
- Unknown event-envelope properties are ignored.

## 10. Webhook responses, outcomes, and codes

Response contract sources:

- [src/CmsSync.Api/Contracts/CmsEvents/CmsEventBatchResponse.cs](src/CmsSync.Api/Contracts/CmsEvents/CmsEventBatchResponse.cs)
- [src/CmsSync.Api/Contracts/CmsEvents/CmsEventResultResponse.cs](src/CmsSync.Api/Contracts/CmsEvents/CmsEventResultResponse.cs)
- [src/CmsSync.Api/Contracts/CmsEvents/CmsEventSummaryResponse.cs](src/CmsSync.Api/Contracts/CmsEvents/CmsEventSummaryResponse.cs)

`200 OK` response shape:

- `batchId`
- `results[]`
	- `sequence`
	- `eventId` (optional)
	- `id` (optional)
	- `outcome`
	- `code`
	- `generation` (optional)
	- `resultingVersion` (optional)
- `summary`
	- `total`
	- `applied`
	- `duplicate`
	- `equivalent`
	- `stale`
	- `invalid`
	- `conflict`

Outcome tokens:

- `applied`
- `duplicate`
- `equivalent`
- `stale`
- `invalid`
- `conflict`

Common request-level Problem Details codes:

- `REQUEST_TOO_LARGE`
- `MALFORMED_JSON`
- `INVALID_ENVELOPE`
- `BATCH_SIZE_OUT_OF_RANGE`
- `UNSUPPORTED_MEDIA_TYPE`
- `DEPENDENCY_UNAVAILABLE`
- `UNEXPECTED_PROCESSING_FAILURE`

Item-level validation codes:

- `EVENT_MUST_BE_OBJECT`
- `DUPLICATE_PROPERTY_NAME`
- `EVENT_TYPE_REQUIRED`
- `EVENT_TYPE_INVALID`
- `ENTITY_ID_REQUIRED`
- `ENTITY_ID_INVALID`
- `EVENT_ID_INVALID`
- `TIMESTAMP_REQUIRED`
- `TIMESTAMP_INVALID`
- `VERSION_REQUIRED`
- `VERSION_INVALID`
- `VERSION_NOT_ALLOWED`
- `PAYLOAD_REQUIRED`
- `PAYLOAD_MUST_BE_OBJECT`
- `PAYLOAD_TOO_LARGE`
- `PAYLOAD_NOT_ALLOWED`

Replay and conflict processing codes:

- `EXACT_DUPLICATE`
- `EVENT_ID_CONTENT_CONFLICT`

Domain transition codes:

- `ENTITY_CREATED`
- `ENTITY_RECREATED`
- `VERSION_ADVANCED`
- `SAME_VERSION_APPLIED`
- `VERSION_STALE`
- `EVENT_TIMESTAMP_STALE`
- `STATE_EQUIVALENT`
- `PAYLOAD_CONFLICT`
- `PUBLICATION_STATUS_CONFLICT`
- `TOMBSTONE_BLOCKED`
- `TOMBSTONE_CREATED`
- `TOMBSTONE_STALE`
- `TOMBSTONE_EQUIVALENT`
- `TOMBSTONE_ADVANCED`
- `DELETE_STALE`
- `DELETE_CONFLICT`
- `ENTITY_DELETED`
- `GENERATION_EXHAUSTED`

## 11. HTTP status precedence and retry semantics

Status precedence for webhook requests:

1. Request-size gate (`413`)
2. Authentication/authorization (`401`/`403`)
3. Media-type and top-level JSON envelope validation (`415`/`400`)
4. Item processing (`200` per-item outcomes or `500`/`503` on incomplete batch processing)

Processing and retry behavior:

- Valid batch items run sequentially in request order.
- Each item runs in its own SQL transaction.
- Earlier committed items are not rolled back by a later failure.
- On dependency exhaustion, the endpoint returns `503`.
- On unexpected processing failure, the endpoint returns `500`.
- Safe client behavior is to retry the entire original request after `500`/`503`.
- Do not retry unchanged deterministic `invalid` or `conflict` items.

## 12. Timestamp semantics and delete ordering

Active-entity timestamp fields:

- `CurrentVersionOccurredAtUtc`: timestamp of the latest accepted version; used for same-version ordering.
- `EntityEventHighWatermarkUtc`: monotonic high watermark of accepted versioned-event timestamps; used for delete ordering.

AC057 behavior through implemented pipeline:

1. Start at Version 5 with both timestamps at 10:00.
2. Accept Version 6 at 09:00.
3. `CurrentVersionOccurredAtUtc` becomes 09:00.
4. `EntityEventHighWatermarkUtc` remains 10:00.
5. Delete at 09:30 is `stale`.
6. Delete at 10:00 under a new identity is `conflict`.
7. Delete after 10:00 is `applied`.

## 13. Read API contract and visibility

Endpoints:

- `GET /api/entities?pageSize={1..100}&afterEntityId={cursor}`
- `GET /api/entities/{entityId}`

Defaults and limits:

- Default `pageSize`: 20
- Minimum `pageSize`: 1
- Maximum `pageSize`: 100

List/detail response fields:

- `id`
- `generation`
- `latestVersion`
- `payload`
- `cmsPublicationStatus`
- `currentVersionOccurredAtUtc`
- `entityEventHighWatermarkUtc`
- `administrativeDisabled`

Visibility matrix:

- Published + not administratively disabled: visible to normal consumer and administrator.
- Published + administratively disabled: administrator only.
- Unpublished + not administratively disabled: administrator only.
- Unpublished + administratively disabled: administrator only.
- Deleted: visible to neither role.

Security/behavior notes:

- Hidden, deleted, and unknown detail requests are indistinguishable `404` for normal consumers.
- Cursor ordering and comparison are case-sensitive.

## 14. Administrative state endpoint contract

Endpoint:

- `PUT /api/entities/{entityId}/administrative-state`

Authorization:

- Requires `AdministratorAccess` policy.

Request contract:

- JSON object with required property `Disabled` (exact casing, boolean value).

Response contract:

- `id`
- `administrativeDisabled`
- `administrativeStateChangedAtUtc`
- `administrativeStateChangedBy`

Behavior:

- Unknown request properties are ignored.
- Repeating the same `Disabled` value is idempotent and does not rewrite rowversion/audit.
- Only local administrative fields are changed.
- CMS-owned fields, revisions, and processing logs are not changed.

Common administrative Problem Details codes:

- `INVALID_ADMINISTRATIVE_STATE_REQUEST`
- `ENTITY_NOT_FOUND`
- `ADMINISTRATIVE_STATE_UNAVAILABLE`
- `ADMINISTRATIVE_STATE_UPDATE_FAILED`

## 15. Health endpoints and response safety

Endpoints:

- `GET /health/live`
- `GET /health/ready`

Behavior:

- Both endpoints are anonymous.
- Liveness does not require SQL.
- Readiness checks write and read SQL connectivity.
- Each readiness probe is bounded by a 3-second timeout.

Response body:

- Healthy: `{"status":"Healthy"}`
- Unhealthy: `{"status":"Unhealthy"}`

Caching headers:

- Health responses are emitted with `Cache-Control: no-store` and `Pragma: no-cache`.

## 16. Persistence and migration boundaries

Write-context migration owner:

- [src/CmsSync.Infrastructure/Persistence/CmsWriteDbContext.cs](src/CmsSync.Infrastructure/Persistence/CmsWriteDbContext.cs)

Read-context safeguards:

- [src/CmsSync.Infrastructure/Persistence/CmsReadDbContext.cs](src/CmsSync.Infrastructure/Persistence/CmsReadDbContext.cs)

Schema table set:

- `CmsEntities`
- `CmsEntityRevisions`
- `CmsDeletionTombstones`
- `CmsEventProcessingLogs`

Canonical SQL collation and timestamp precision:

- `Latin1_General_100_BIN2`
- `datetime2(7)`

Migration command used by repository artifacts:

```powershell
dotnet ef migrations script --idempotent --project src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj --startup-project src/CmsSync.Api/CmsSync.Api.csproj --context CmsWriteDbContext
```

Database principal setup script:

- [scripts/container/initialize-database.sql](scripts/container/initialize-database.sql)

## 17. Platform support and Apple Silicon limits

- Supported Compose path: x86-64 Docker host.
- Pinned SQL Server image:
	- `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`
- Apple Silicon guidance:
	- Use a remote supported SQL Server or Azure SQL.
	- Do not use emulation claims as a supported local SQL path.
	- Do not add `platform: linux/amd64` as a workaround.

See [docs/container-development.md](docs/container-development.md) for full platform guidance.

## 18. Validation and CI-equivalent commands

Repository and build gates:

```powershell
pwsh ./scripts/validate-repository-policy.ps1
dotnet restore LateralChallenge.sln --source https://api.nuget.org/v3/index.json
dotnet build LateralChallenge.sln --configuration Release --no-restore
dotnet format LateralChallenge.sln --verify-no-changes --no-restore
```

Test gates:

```powershell
dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "Category=SqlServer"
dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --no-build --no-restore
dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

Container and cleanup verification:

```powershell
pwsh ./scripts/validate-container-setup.ps1
pwsh ./scripts/verify-container-cleanup.ps1
```

## 19. assumptions and known external limitation

Normative implementation assumptions and unresolved contract questions remain in:

- Section 18 and Section 20 of [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md)

Known external limitation (explicitly tracked):

- Delete ordering is timestamp-based and does not include a CMS-provided version/sequence/incarnation identifier.

Production-integration readiness requires resolving the external questions documented in [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md).
