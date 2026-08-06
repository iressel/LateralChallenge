# LateralChallenge

This repository implements the CMS event ingestion and entity visibility challenge using ASP.NET Core, EF Core, and SQL Server.

## 1. Repository scope

- Source-of-truth requirements: [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md)
- Architecture and implementation plan: [specs/cms-event-ingestion/plan.md](specs/cms-event-ingestion/plan.md)
- Execution checklist and task boundaries: [specs/cms-event-ingestion/tasks.md](specs/cms-event-ingestion/tasks.md)
- Repository guardrails: [AGENTS.md](AGENTS.md)

This README documents implemented behavior from current source and tests. It does not replace Section 18 or Section 20 of the specification.

## 2. Implemented stack and boundaries

- Runtime: .NET 10
- API shape: ASP.NET Core attribute-routed controllers
- Persistence: EF Core 10 with Microsoft SQL Server only
- Solution file: [LateralChallenge.sln](LateralChallenge.sln)
- Production write/read boundary: separate write and read connection strings
- No automatic startup migration path in normal API startup

## 3. Required configuration and secret handling

Required environment-variable keys include:

- `ConnectionStrings__WriteDatabase`
- `ConnectionStrings__ReadDatabase`
- `Authentication__Credentials__Cms__Username`
- `Authentication__Credentials__Cms__Password`
- `Authentication__Credentials__Consumer__Username`
- `Authentication__Credentials__Consumer__Password`
- `Authentication__Credentials__Administrator__Username`
- `Authentication__Credentials__Administrator__Password`

Credential rules enforced by configuration validation and tests:

- CMS username length must be 10 through 20 characters.
- CMS, Consumer, and Administrator usernames are distinct.
- CMS, Consumer, and Administrator passwords are distinct GUID `D` format values.
- Basic Authentication requires HTTPS outside Development.
- Real credentials never belong in source control.

Container-local SQL secret keys:

- `MSSQL_SA_PASSWORD`
- `MIGRATION_SQL_PASSWORD`
- `WRITE_SQL_PASSWORD`
- `READ_SQL_PASSWORD`

Copy [.env.example](.env.example) to `.env` for local development only, then replace placeholders with local secrets.

## 4. SQL principal separation and migration boundaries

Database initialization and migration assets:

- [scripts/container/initialize-database.sql](scripts/container/initialize-database.sql)
- [scripts/container/initialize-database.sh](scripts/container/initialize-database.sh)
- [scripts/container/apply-migrations.sh](scripts/container/apply-migrations.sh)
- [scripts/container/verify-migration.sh](scripts/container/verify-migration.sh)
- [scripts/container/verify-read-only.sh](scripts/container/verify-read-only.sh)

Principal separation:

- `sa` is used only for local database initialization checks and setup.
- `CmsSyncMigration` is the migration principal and applies migrations.
- `CmsSyncWriter` is the API write-context principal.
- `CmsSyncReader` is SELECT-only.

Operational boundaries:

- Normal API startup does not call `Database.Migrate`, `EnsureCreated`, or equivalent auto-migration behavior.
- Production migrations require a separately authorized migration principal.
- The API write identity must not receive migration permissions.

## 5. Supported startup paths

Compose and container artifacts:

- [compose.yaml](compose.yaml)
- [Dockerfile](Dockerfile)
- [docs/container-development.md](docs/container-development.md)
- [apphost.cs](apphost.cs)
- [scripts/configure-aspire-local.ps1](scripts/configure-aspire-local.ps1)
- [scripts/validate-aspire-setup.ps1](scripts/validate-aspire-setup.ps1)

Supported local Compose path is x86-64:

```powershell
docker compose config --quiet
docker compose down --volumes --remove-orphans
docker compose up --build --wait
docker compose ps
```

Optional local Aspire orchestration path (additional path; Compose remains supported):

```powershell
pwsh ./scripts/configure-aspire-local.ps1

aspire start --apphost ./apphost.cs --isolated --non-interactive
aspire wait sql --status healthy --timeout 480 --apphost ./apphost.cs --non-interactive
aspire wait db-init --status down --timeout 480 --apphost ./apphost.cs --non-interactive
aspire wait migration --status down --timeout 480 --apphost ./apphost.cs --non-interactive
aspire wait api --status healthy --timeout 480 --apphost ./apphost.cs --non-interactive
aspire describe --apphost ./apphost.cs --format Json --non-interactive
aspire stop --apphost ./apphost.cs --non-interactive
```

Optional local API startup against an existing SQL Server:

```powershell
dotnet restore LateralChallenge.sln --source https://api.nuget.org/v3/index.json
dotnet build LateralChallenge.sln --configuration Release --no-restore
dotnet run --project src/CmsSync.Api/CmsSync.Api.csproj --configuration Release
```

Deterministic container validation and cleanup:

```powershell
pwsh ./scripts/validate-container-setup.ps1
pwsh ./scripts/verify-container-cleanup.ps1
```

Deterministic Aspire validation and cleanup:

```powershell
pwsh ./scripts/validate-aspire-setup.ps1
```

## 6. Authentication and authorization contract

Implementation sources:

- [src/CmsSync.Infrastructure/Authentication/AuthenticationConstants.cs](src/CmsSync.Infrastructure/Authentication/AuthenticationConstants.cs)
- [src/CmsSync.Infrastructure/Authentication/AuthenticationRegistration.cs](src/CmsSync.Infrastructure/Authentication/AuthenticationRegistration.cs)
- [src/CmsSync.Infrastructure/Authentication/BasicAuthenticationHandler.cs](src/CmsSync.Infrastructure/Authentication/BasicAuthenticationHandler.cs)

Schemes:

- `CmsBasic`
- `ConsumerBasic`

Policies:

- `CmsEvents`
- `ConsumerAccess`
- `AdministratorAccess`

Roles:

- `CmsService`
- `NormalConsumer`
- `Administrator`

Behavior:

- Missing, malformed, or invalid credentials return `401` with `Basic realm="<scheme>"`.
- Wrong-scheme credentials return `401` for the endpoint scheme.
- A normal consumer on administrator-only operations returns `403` with no challenge.
- Authentication failures and HTTPS redirects are protected with `Cache-Control: no-store`.

### Development Swagger and OpenAPI

- Swagger UI is enabled only in Development.
- With the default Compose API port, local documentation endpoints are:
	- `http://localhost:8080/swagger`
	- `http://localhost:8080/swagger/index.html`
	- `http://localhost:8080/swagger/v1/swagger.json`
- The Swagger authorization dialog exposes two separate Basic entries:
	- `CmsBasic` for `POST /cms/events`
	- `ConsumerBasic` for `GET /api/entities`, `GET /api/entities/{entityId}`, and `PUT /api/entities/{entityId}/administrative-state`
- Administrative-state updates still authenticate through `ConsumerBasic` and require the `Administrator` role; normal consumer credentials return `403`.
- Credentials are never committed or prefilled in repository artifacts.
- The documented webhook request example in Swagger remains a raw JSON array (no wrapper object).
- Swagger UI and the OpenAPI JSON route are not mapped outside Development.

## 7. CMS webhook request contract

Endpoint and accepted media types:

- `POST /cms/events`
- `application/json`
- `application/*+json`

Request constraints:

- Top-level JSON must be a raw array of 1 through 50 items.
- No `{ "events": [...] }` envelope.
- Request limit: 16 MiB.
- Per-versioned-event payload limit: 256 KiB.
- Property names are case-sensitive.
- Webhook entity property is exactly `id`; `entityId` is not accepted.

### Example: raw webhook array request

```json
[
	{
		"eventId": "evt-0001",
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
		"eventId": "evt-0002",
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

The request example intentionally mixes event-type casing and surrounding whitespace and shows both present and omitted `eventId` values.

## 8. Webhook response contract

Response contract source:

- [src/CmsSync.Api/Contracts/CmsEvents/CmsEventBatchResponse.cs](src/CmsSync.Api/Contracts/CmsEvents/CmsEventBatchResponse.cs)

### Example: webhook 200 OK batch response

```json
{
	"batchId": "8e97276f-d710-4ef1-a2c3-6f1a4f63237a",
	"results": [
		{
			"sequence": 0,
			"eventId": "evt-0001",
			"id": "entity-ac057",
			"outcome": "applied",
			"code": "VERSION_ADVANCED",
			"generation": 1,
			"resultingVersion": 6
		},
		{
			"sequence": 1,
			"id": "entity-ac057",
			"outcome": "conflict",
			"code": "DELETE_CONFLICT"
		}
	],
	"summary": {
		"total": 2,
		"applied": 1,
		"duplicate": 0,
		"equivalent": 0,
		"stale": 0,
		"invalid": 0,
		"conflict": 1
	}
}
```

`results` are ordered by request sequence. Optional fields (`eventId`, `id`, `generation`, `resultingVersion`) can be omitted per item when not applicable.

## 9. Entity list and detail response contracts

Contract sources:

- [src/CmsSync.Api/Contracts/Entities/CmsEntityListResponse.cs](src/CmsSync.Api/Contracts/Entities/CmsEntityListResponse.cs)
- [src/CmsSync.Api/Contracts/Entities/CmsEntityResponse.cs](src/CmsSync.Api/Contracts/Entities/CmsEntityResponse.cs)

### Example: entity list response

```json
{
	"items": [
		{
			"id": "entity-ac057",
			"generation": 1,
			"latestVersion": 6,
			"payload": {
				"value": 6,
				"source": "documentation-example"
			},
			"cmsPublicationStatus": "Unpublished",
			"currentVersionOccurredAtUtc": "2026-08-02T09:00:00Z",
			"entityEventHighWatermarkUtc": "2026-08-02T10:00:00Z",
			"administrativeDisabled": false
		}
	],
	"pageSize": 20,
	"nextCursor": "entity-ac057"
}
```

### Example: entity detail response

```json
{
	"id": "entity-ac057",
	"generation": 1,
	"latestVersion": 6,
	"payload": {
		"value": 6,
		"source": "documentation-example"
	},
	"cmsPublicationStatus": "Unpublished",
	"currentVersionOccurredAtUtc": "2026-08-02T09:00:00Z",
	"entityEventHighWatermarkUtc": "2026-08-02T10:00:00Z",
	"administrativeDisabled": false
}
```

The list contract is the wrapper object with `items`, `pageSize`, and optional `nextCursor`; the item contract is `CmsEntityResponse`.

## 10. Administrative-state request and response contracts

Contract sources:

- [src/CmsSync.Api/Contracts/Entities/CmsAdministrativeStateRequest.cs](src/CmsSync.Api/Contracts/Entities/CmsAdministrativeStateRequest.cs)
- [src/CmsSync.Api/Contracts/Entities/CmsAdministrativeStateResponse.cs](src/CmsSync.Api/Contracts/Entities/CmsAdministrativeStateResponse.cs)

### Example: administrative-state request

```json
{
	"Disabled": true
}
```

### Example: administrative-state response

```json
{
	"id": "entity-ac057",
	"administrativeDisabled": true,
	"administrativeStateChangedAtUtc": "2026-08-03T11:14:52Z",
	"administrativeStateChangedBy": "administrator-local-user"
}
```

The request requires an object with exact-case boolean `Disabled`.

## 11. Endpoint access matrix

| Endpoint | Required access | Allowed actor | Important behavior |
|---|---|---|---|
| `POST /cms/events` | `CmsBasic` + `CmsEvents` | CMS service identity | Wrong-scheme credentials, missing credentials, malformed credentials, and invalid credentials return `401` with `Basic realm="CmsBasic"`. |
| `GET /api/entities` | `ConsumerBasic` + `ConsumerAccess` | Normal consumer or administrator | Wrong-scheme credentials return `401` with `Basic realm="ConsumerBasic"`. |
| `GET /api/entities/{entityId}` | `ConsumerBasic` + `ConsumerAccess` | Normal consumer or administrator | Hidden/deleted/unknown entity behavior is non-disclosing `404` for normal consumer visibility boundaries. |
| `PUT /api/entities/{entityId}/administrative-state` | `ConsumerBasic` + `AdministratorAccess` | Administrator only | Normal consumer credentials return `403` without a challenge. Deleted and unknown entity updates return the same non-disclosing `404`. |
| `GET /health/live` | Anonymous | Any caller | No SQL query; liveness only. |
| `GET /health/ready` | Anonymous | Any caller | Verifies both write and read SQL connectivity. |

## 12. HTTP status and retry matrix

| Status | When returned | Processing and retry guidance |
|---|---|---|
| `200` | A syntactically valid webhook batch completes durably. | This includes batches containing deterministic `invalid` or `conflict` items; valid request-level processing completed. |
| `400` | Malformed JSON or invalid envelope for `POST /cms/events`; also invalid administrative-state request body shape/casing. | For webhook request-level `400`, no event processing occurs. Correct the request before resubmitting. |
| `401` | Missing, malformed, invalid, or wrong-scheme credentials. | Response includes the endpoint scheme challenge (`Basic realm="CmsBasic"` or `Basic realm="ConsumerBasic"`). |
| `403` | Authenticated caller lacks required role (for example, normal consumer on administrator endpoint). | Do not retry with the same actor identity. |
| `404` | Entity not found under role visibility rules. | Hidden/deleted/unknown detail behavior is non-disclosing where applicable. |
| `413` | Webhook request size exceeds 16 MiB. | No event processing occurs. Reduce request size before retrying. |
| `415` | Authenticated webhook request has unsupported media type. | No event processing occurs. Send JSON media type before retrying. |
| `500` | Unexpected failure prevents durable completion of the valid webhook batch. | Retry the entire original request. Previously committed earlier items remain committed. Deterministic invalid/conflict items must not be retried unchanged. Do not retry only a guessed suffix of the batch. |
| `503` | Recognized dependency-unavailable failure prevents durable completion. | Retry the entire original request. Previously committed earlier items remain committed. Deterministic invalid/conflict items must not be retried unchanged. Do not retry only a guessed suffix of the batch. |

Additional processing guarantees:

- `400`, `413`, and `415` webhook request-level failures perform no event processing.
- Cancellation does not undo already committed item transactions.

## 13. Ordering, tombstone, and administrative semantics

Event ordering and version/timestamp rules:

- A higher version wins even when its timestamp is older.
- `CurrentVersionOccurredAtUtc` may move backward.
- `EntityEventHighWatermarkUtc` never moves backward.
- Same-version payload is immutable.
- Same-version ordering uses `CurrentVersionOccurredAtUtc`.
- Delete ordering uses `EntityEventHighWatermarkUtc` only.
- Delete removes the active entity and payload-bearing revisions.
- Delete advances or creates the payload-free tombstone.
- A versioned event at or before the tombstone timestamp is stale.
- Recreation after the tombstone begins the next local generation.
- Recreation resets `AdministrativeDisabled` to `false`.

Administrative-state rules:

- Publish and unpublish preserve `AdministrativeDisabled`.
- Delete removes local administrative-state data with the entity.
- Recreation resets `AdministrativeDisabled` to `false`.
- Deleted and unknown administrative updates return the same `404` shape.
- Repeating the current `Disabled` value does not rewrite audit fields or rowversion state.

## 14. Response safety and observability behavior

Security and response-safety guidance:

- Entity responses use no-store protections.
- Authentication failures use no-store protections.
- Safe Problem Details responses do not expose stack traces or database details.
- `X-Correlation-ID` is accepted when safe or generated when absent/unsafe, and it is separate from `HttpContext.TraceIdentifier`.

Logging and metrics guidance:

- Structured processing logs do not contain raw payloads.
- Metrics use low-cardinality labels (for example outcome/result class, operation, scheme).
- Authorization headers, decoded Basic credentials, secrets, connection strings, and raw payloads must not be logged.
- This repository documents in-process structured logs and metrics only. It does not claim an observability backend that is not configured here.

## 15. Health behavior

Health endpoint sources:

- [src/CmsSync.Api/Health/HealthEndpointRoutes.cs](src/CmsSync.Api/Health/HealthEndpointRoutes.cs)
- [src/CmsSync.Api/Health/SafeHealthResponseWriter.cs](src/CmsSync.Api/Health/SafeHealthResponseWriter.cs)
- [src/CmsSync.Infrastructure/Health/SqlServerConnectivityHealthCheck.cs](src/CmsSync.Infrastructure/Health/SqlServerConnectivityHealthCheck.cs)

Behavior:

- `GET /health/live` is anonymous and does not query SQL.
- `GET /health/ready` is anonymous and checks both read and write SQL dependencies.
- Health responses are minimal (`{"status":"Healthy"}` or `{"status":"Unhealthy"}`) and do not expose provider names, exceptions, credentials, or connection details.

## 16. CI workflow contract

Workflow source:

- [.github/workflows/ci.yml](.github/workflows/ci.yml)

Committed CI behavior:

- Triggers:
	- pull requests targeting `main`
	- pushes to `main`
	- pushes to `feature/t016-*`
	- `workflow_dispatch`
- Runner: `ubuntu-24.04` on x86-64
- Workflow permissions are limited to `contents: read`
- Official actions pinned to immutable full SHAs:
	- `actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1`
	- `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68`
	- `actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a`
- Quality gates run in CI:
	- repository-policy validation
	- restore from NuGet.org
	- Release build
	- format verification
	- SQL Server smoke tests
	- unit and full integration tests with TRX and Cobertura output
	- clean-volume Compose smoke
	- cleanup verification
	- artifact upload: `ci-test-evidence`

The deterministic repository-policy scan is valuable, but it does not replace GitHub secret scanning or a dedicated security product.

## 17. Apple Silicon and unsupported emulation paths

Authoritative platform guidance:

- [docs/container-development.md](docs/container-development.md)

Apple Silicon constraints:

- Rosetta is unsupported for the SQL Server Linux container path.
- QEMU is unsupported for this local SQL Server container path.
- Other emulation or translation layers are unsupported.
- Do not add `platform: linux/amd64` as a workaround.
- Use a remote supported SQL Server instance or Azure SQL.
- Use a migration-authorized connection only for migration execution.
- Use a SELECT-only principal for `ConnectionStrings__ReadDatabase`.
- SQL Server Testcontainers are not the Apple Silicon local verification path.

## 18. Validation commands

```powershell
pwsh ./scripts/validate-repository-policy.ps1

dotnet restore LateralChallenge.sln --source https://api.nuget.org/v3/index.json

dotnet build LateralChallenge.sln --configuration Release --no-restore

dotnet format LateralChallenge.sln --verify-no-changes --no-restore

dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "Category=Documentation"

dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --no-build --no-restore

dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --no-build --no-restore

pwsh ./scripts/validate-container-setup.ps1

pwsh ./scripts/validate-aspire-setup.ps1

pwsh ./scripts/verify-container-cleanup.ps1

git diff --check
git status --short
git diff --name-only
```

## 19. Assumptions and unresolved external questions

Normative assumptions for challenge implementation remain in Section 18 of [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md), including:

- webhook raw-array shape and case-sensitive property names with trimmed, case-insensitive supported type normalization
- exact wire property `id` mapping to internal `EntityId`
- bounded request/payload limits and case-sensitive identifier handling

Unresolved external questions remain in Section 20 of [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md), including:

- uncertainty around CMS `eventId` availability and uniqueness guarantees
- delete events have no CMS version, sequence, generation, or incarnation identifier
- timestamp precision, clock-skew, and timestamp-reuse risks remain external concerns
- credential provisioning and rotation remain operator concerns
- production migration-principal provisioning remains an operator concern
- production SELECT-only read-principal provisioning remains an operator concern
- local tombstone and generation behavior is deterministic but does not replace a future CMS incarnation protocol

No production-integration readiness claim should be made until these external questions are confirmed.

Production-integration readiness requires resolving the external questions documented in [specs/cms-event-ingestion/spec.md](specs/cms-event-ingestion/spec.md).
