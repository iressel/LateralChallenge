# Implementation Plan: CMS Event Ingestion and Entity Visibility

**Specification:** spec.md\
**Status:** Draft for Phase 3 execution\
**Implementation target:** Stable .NET 10, EF Core 10, ASP.NET Core, Microsoft SQL Server

## 1. Summary

Build one ASP.NET Core Web API using a lightweight Clean Architecture. The CMS webhook receives the challenge's raw JSON array, keeps external property `id`, and normalizes event-type values case-insensitively. A pure domain state machine will make every publish, unpublish, and delete decision. An application service will validate and process the array sequentially, wrapping each event in its own SQL Server transaction. Infrastructure will provide EF Core 10 persistence, SQL Server concurrency/idempotency enforcement, and isolated Basic Authentication. Consumer reads will use a separate no-tracking read context and application abstraction.

The implementation remains deliberately small: four production projects, two test projects, four required tables, no mediator, no generic repositories, no mapper framework, no background queue, and no separate batch table.

## 2. Technical context and constraints

| Concern | Decision |
|---|---|
| Runtime | Stable .NET 10, nullable reference types enabled, warnings treated as errors for repository code |
| Web | ASP.NET Core Web API with controllers or route groups kept transport-only |
| Persistence | EF Core 10 and official Microsoft.EntityFrameworkCore.SqlServer provider |
| Database | Microsoft SQL Server only |
| Solution | Traditional LateralChallenge.sln, not .slnx |
| Architecture | Domain, Application, Infrastructure, API |
| Processing | Synchronous request lifecycle; asynchronous I/O; sequential items |
| Transactions | One durable SQL Server transaction per item |
| Tests | xUnit unit and integration projects; SQL Server Testcontainers where supported |
| Authentication | Two custom Basic schemes using configuration-backed challenge credentials |
| Explicit exclusions | MediatR, AutoMapper, generic repositories, microservices, EF Core InMemory, LocalDB, and in-memory queues |

Repository rules require production-ready, small, reviewable changes; clear C# naming; async/CancellationToken; and efficient no-tracking EF Core queries. The specification is the public behavior source of truth.

Exact SDK, package patch versions, SQL Server image tag/digest, and Testcontainers compatibility MUST be verified during implementation rather than guessed in this plan. The major version constraints are .NET 10 and EF Core 10.

## 3. Current flow

There is no current executable flow because the workspace is empty. The planned write and read flows are:

    CMS service
      → CMS Basic authentication/policy
      → POST /cms/events raw-array transport validation
      → batch application service
      → sequential per-item transaction
      → pure state machine
      → CmsWriteDbContext
      → SQL Server

    Consumer or administrator
      → Consumer Basic authentication/policy
      → read/admin endpoint
      → application query or command abstraction
      → CmsReadDbContext for queries / CmsWriteDbContext for admin command
      → SQL Server
      → role-filtered response

## 4. Architecture and dependency direction

### 4.1 Projects

| Project | Responsibility | Allowed project dependencies |
|---|---|---|
| CmsSync.Domain | Entity state, value types, transition input/output, outcome codes, pure state machine | None |
| CmsSync.Application | Use cases, ports, request/result contracts, validation orchestration, read projections | Domain |
| CmsSync.Infrastructure | EF Core models/configurations/contexts, SQL coordination, authentication verifier/configuration | Application, Domain |
| CmsSync.Api | HTTP contracts, controllers/endpoints, middleware, Problem Details, DI composition, health endpoints | Application, Infrastructure |
| CmsSync.UnitTests | Pure state-machine, canonicalization, and application-unit tests | Domain, Application |
| CmsSync.IntegrationTests | SQL Server, migrations, API factory, auth/policy, endpoint and concurrency tests | API, Infrastructure |

Dependency direction is inward. Domain has no framework reference. Application owns interfaces only when they represent a real boundary needed by a use case. Infrastructure implements those ports. API is the composition root and does not contain business rules.

Controllers/endpoints translate HTTP to application contracts, call one use case, and translate results. They do not use DbContext directly. A single explicit application service is preferable to command/handler ceremony.

### 4.2 Proposed repository structure

    /
    ├── AGENTS.md
    ├── README.md
    ├── .editorconfig
    ├── .env.example
    ├── .gitignore
    ├── Directory.Build.props
    ├── Directory.Packages.props
    ├── global.json
    ├── LateralChallenge.sln
    ├── compose.yaml
    ├── Dockerfile
    ├── specs/
    │   └── cms-event-ingestion/
    │       ├── spec.md
    │       ├── plan.md
    │       └── tasks.md
    ├── src/
    │   ├── CmsSync.Domain/
    │   │   ├── Entities/
    │   │   ├── Events/
    │   │   └── Processing/
    │   ├── CmsSync.Application/
    │   │   ├── Abstractions/
    │   │   ├── EventIngestion/
    │   │   ├── EntityQueries/
    │   │   └── AdministrativeState/
    │   ├── CmsSync.Infrastructure/
    │   │   ├── Authentication/
    │   │   ├── Persistence/
    │   │   │   ├── Configurations/
    │   │   │   └── Migrations/
    │   │   └── DependencyInjection.cs
    │   └── CmsSync.Api/
    │       ├── Contracts/
    │       ├── Controllers/
    │       ├── Program.cs
    │       └── appsettings.json
    └── tests/
        ├── CmsSync.UnitTests/
        └── CmsSync.IntegrationTests/

This is a proposed Phase 3 structure, not a Phase 2 file-creation instruction. ADR files are not required for the challenge.

## 5. Domain and application design

### 5.1 Pure decision model

The Domain project will represent:

- Current active entity snapshot, including generation, latest version, canonical payload hash, publication status, CurrentVersionOccurredAtUtc, monotonic EntityEventHighWatermarkUtc, and local override.
- Tombstone snapshot.
- Previously observed same-version revision when needed.
- Validated incoming publish/unpublish/delete event.
- A closed decision result: Applied with state operations, Duplicate, Equivalent, Stale, Invalid, or Conflict, each with a stable code.

The pure state machine receives snapshots and a validated event, applies the precedence and transition tables in spec.md Sections 12.1–12.4, and returns a decision without performing I/O. Administrative disable transitions are a separate small domain operation so CMS transitions cannot accidentally clear the local flag.

Transport parsing, credential checking, SQL locking, DbContext tracking, clocks, and logging do not enter the state machine.

The implementation and its tests will preserve these ordering invariants verbatim:

- Version is primary for publish/unpublish: lower is stale, higher becomes latest, first observed need not be 1, and gaps are accepted.
- A payload is immutable within EntityId/Generation/Version. Same-version different content conflicts.
- Same-version identical content compares Timestamp with CurrentVersionOccurredAtUtc: earlier is stale, equal incompatible status conflicts, and later status changes apply while both timestamp invariants are updated.
- A higher Version always sets CurrentVersionOccurredAtUtc to its own Timestamp, even when older than the prior version timestamp; EntityEventHighWatermarkUtc becomes max(previous high watermark, incoming Timestamp) and never regresses.
- A higher-version unpublish stores that version and payload as latest and makes it administrator-visible but normal-consumer-hidden.
- Delete is unversioned and compares only with EntityEventHighWatermarkUtc: earlier is stale, equal is incompatible/conflict unless idempotency already resolved an exact replay, and later hard-deletes the entity and all revisions while retaining a payload-free tombstone.
- Publish or unpublish at/before a tombstone is stale. Either type strictly after it starts the next generation with any positive Version, AdministrativeDisabled false, and both active timestamps equal to the incoming Timestamp.

### 5.2 Canonical payload and event identity

A dedicated deterministic component will:

1. Require the top-level JSON value to be the raw array itself with 1 through 50 positions; reject object, null, string, number, empty, and oversized envelopes at request level.
2. Reject duplicate JSON names inside an event or payload using an explicit Utf8JsonReader traversal before model binding loses that information.
3. Require case-sensitive wire property `id`, map it to internal EntityId, trim `type`, and normalize supported type values case-insensitively to canonical `publish`, `unpublish`, or `delete`.
4. Preserve the raw first-observed payload for storage and response.
5. Produce the spec-defined canonical byte representation for equality/hash only.
6. Hash the representation with SHA-256.
7. Produce a length-prefixed normalized event-content representation using canonical type and internal EntityId.
8. Use external:{EventId} when EventId exists, otherwise sha256:{derived digest}.

Golden tests will freeze object ordering, arrays, decoded strings, numeric-token significance, nested payloads, unknown event properties, timestamps, delete sentinels, property-name case sensitivity, raw-array shape, wire `id`, and every required event-type casing example. The implementation will cap traversal depth and bytes before allocating large object graphs.

### 5.3 Application ports

Introduce only use-case-shaped abstractions, for example:

- An event transaction executor that coordinates one SQL Server-backed item.
- An entity read query service returning projections.
- An administrative-state service.
- A current time/correlation abstraction only if deterministic tests require one.

Do not expose IQueryable, DbContext, or tracked persistence entities outside Infrastructure. Do not add generic repository CRUD abstractions.

## 6. SQL Server persistence design

### 6.1 Required model

Implement only the four tables required by spec.md Section 13:

| Table | Key/invariants | Lifecycle |
|---|---|---|
| CmsEntities | EntityId PK; positive Generation/LatestVersion; JSON-object Payload; PayloadHash; required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc datetime2(7); rowversion | One active row; high watermark is monotonic within its generation; hard-deleted by CMS delete |
| CmsEntityRevisions | Unique EntityId + Generation + Version; immutable payload/hash | Insert per first observed version; all entity rows hard-deleted by CMS delete |
| CmsDeletionTombstones | EntityId PK; payload-free deletion watermark; rowversion | Created/advanced by delete; retained through recreation |
| CmsEventProcessingLogs | Internal PK; unique BatchId + Sequence; one filtered-unique identity owner plus replay references; metadata/outcome only | Durable audit/idempotency/attempt record; retained after delete |

Use explicit configuration classes for names, sizes, required fields, conversions, check constraints, indexes, delete behavior, datetime precision, and the selected case-sensitive binary collation. Store enum values using bounded stable strings where operational readability is useful, protected by check constraints where practical.

Payload object checks use SQL Server ISJSON with object semantics supported by the selected SQL Server version. Application validation remains primary; the database check is defense in depth.

No navigation/cascade arrangement may accidentally delete tombstones or processing logs. Applying CMS delete will explicitly delete all revisions for EntityId and the current row inside the event transaction.

### 6.2 No batch table

BatchId and Sequence are columns of CmsEventProcessingLogs and the batch summary is computed from in-memory per-item results after all transactions commit. There is no batch replay/resume/query use case. Therefore CmsIngestionBatches adds no required invariant and will not be implemented.

If a future requirement demands batch status retrieval or resumption, it must first amend spec.md and migrations.

### 6.3 Read/write DbContexts

CmsWriteDbContext:

- Owns all four entity configurations and migrations.
- Is used by event processing and administrative state changes.
- Uses ConnectionStrings:WriteDatabase.
- Does not run migrations automatically in production.

CmsReadDbContext:

- Maps the active entity read model to CmsEntities without owning migrations.
- Uses ConnectionStrings:ReadDatabase.
- Applies AsNoTracking and projects directly to immutable response-oriented application records.
- Overrides both SaveChanges variants to throw NotSupportedException.
- Is hidden behind an application query abstraction.

The two contexts may share mapping constants but not migration ownership. Production deployment provisions a SELECT-only read login; the code override is only an early-development failure signal. Local development may use one login for both strings.

## 7. Event-processing orchestration

For each position in a valid raw-array batch:

    begin one resilient SQL Server execution unit
      begin one event transaction
      validate and derive identity/content hashes
      check completed identity and content
      acquire/establish per-entity serialization
      load entity with both active timestamps, tombstone, and relevant revision
      ask pure state machine for one decision
      apply exactly that decision without permitting high-watermark regression
      insert durable processing log
      save and commit
    return per-item result

The batch service awaits this flow sequentially and accumulates results only after each transaction commits. Validation outcomes that belong to individual items are also persisted transactionally as payload-free processing logs. If an event lacks enough valid fields to derive its normal idempotency key, it receives an internal attempt record but no business mutation.

If a transaction fails after bounded transient retry, the service stops. It does not process later items and does not return a normal 200 body because the full valid batch was not durably completed. Existing prior commits make whole-request retry safe.

## 8. Transaction and concurrency strategy

Use the SQL Server provider execution strategy to wrap explicit transaction creation so retryable transaction work is replayed as a unit. Pass CancellationToken throughout.

The preferred serialization mechanism is:

- Serializable transaction isolation for the event unit.
- A transaction-owned sp_getapplock resource derived from a stable fixed-length hash of EntityId.
- A filtered unique index allowing one OwnsIdempotencyKey row per IdempotencyKey; replay rows refer to that owner without claiming the key.
- The revision composite unique key.
- RowVersion on current entities and tombstones.

The application lock prevents competing state transitions for one EntityId across API instances, including absent-row and delete/recreate races. A stable hash avoids lock-name length and sensitive identifier problems. The identity-owner constraint resolves cross-entity reuse of the same external EventId. When an ownership race occurs, reload the winning log: preserve its invalid/conflict outcome for an exact replay, otherwise return duplicate; return conflict when the external EventId content hash differs. Persist a replay row for the current BatchId/Sequence without claiming the key.

All locks must be acquired in a documented consistent order. Integration tests will run concurrent scopes for duplicate EventId, same entity competing versions, same-version conflicting payloads, higher-version older-timestamp acceptance, delete versus publish/high-watermark boundaries, and recreation. They must prove that serialized writes cannot regress EntityEventHighWatermarkUtc and that delete reads that value rather than CurrentVersionOccurredAtUtc. Bounded deadlock/transient retries must emit safe metrics.

An alternative to sp_getapplock is acceptable only if it proves the same absent-row, cross-instance, idempotency, and delete/recreation properties under real SQL Server concurrency.

## 9. Validation approach

Validation has three boundaries:

1. Server request-size and media-type checks.
2. Streaming JSON parsing that requires a raw top-level array of 1 through 50 positions, preserves per-item raw slices, and detects duplicate names within each event/payload.
3. Application validation of case-sensitive known property names, wire `id` to internal EntityId mapping, trim-aware case-insensitive event-type normalization, applicability, bounds, timestamp precision, payload object/size, and normalized identity.

Malformed JSON and a top-level object, null, string, number, empty array, or array above 50 items produce 400 before event transactions. Once the raw top-level array is valid, unsupported or malformed individual items become durable per-item invalid results and the final status remains 200 if all logs commit.

Do not bind directly to a permissive DTO before duplicate-name detection. Do not deserialize arbitrary payloads into business types. Payload is opaque validated JSON.

Stable validation codes will be constants shared by state decisions and API results. Problem Details is reserved for request-level and server-level failures.

## 10. Authentication and authorization design

Implement two named Basic schemes:

- CmsBasic: recognizes only the configured CMS service identity.
- ConsumerBasic: recognizes the configured normal consumer and administrator, assigning the appropriate role.

Policies:

| Policy | Scheme | Required identity/role |
|---|---|---|
| CmsEvents | CmsBasic | Authenticated CMS service |
| ConsumerAccess | ConsumerBasic | Normal consumer or administrator |
| AdministratorAccess | ConsumerBasic | Administrator |

The handler will:

- Validate one bounded Authorization header and Basic parameter.
- Decode without logging the value.
- Split at the first colon, reject invalid username syntax, and retain the password as opaque text.
- Compare username and GUID-format password using fixed-time byte comparison of consistently sized digests.
- Return Fail/NoResult so ASP.NET Core produces the correct named challenge.
- Clear/discard decoded buffers as practical.

Configuration binds three distinct username/password pairs from environment variables or user-secrets. Startup validation enforces presence, uniqueness, GUID password format, CMS username length 10–20, and safe maximum lengths. Operators do not precompute PBKDF2 verifiers for this challenge.

Authorization tests use the real middleware pipeline. A CMS credential on a consumer endpoint and a consumer credential on the webhook are 401 because the endpoint-specific scheme does not recognize them. A normal consumer on the admin endpoint is authenticated but forbidden, producing 403.

Use HTTPS redirection/forwarded-header configuration appropriate to deployment, and require HTTPS outside Development. Authentication responses and entity responses use no-store.

## 11. API implementation design

### 11.1 Webhook

The endpoint owns only:

- CmsEvents authorization.
- Content-type and raw-array top-level transport handling, with no `events` wrapper.
- Wire-contract parsing that accepts exactly `id` for the entity property and case-insensitively normalizes trimmed event-type values.
- BatchId/correlation creation.
- Calling the sequential ingestion service.
- Mapping completed item results to the 200 response.
- Mapping request/server failures to safe Problem Details.

It never contains state-transition logic. Response types explicitly define all six outcomes and summary counters and use response property `id`. JSON serialization contract tests freeze raw-array input, wire/request/result property names, all required event-type casing examples, canonical enum tokens, null omission, and request-order preservation.

### 11.2 Read API

The query abstraction receives caller visibility and cursor/page size. Infrastructure applies:

- Role visibility predicate in SQL.
- EntityId greater than cursor using the configured case-sensitive collation.
- OrderBy EntityId.
- Take page size plus one to derive NextCursor.
- Direct no-tracking projection; no revisions or tracked entities.

Detail lookup incorporates the visibility predicate so normal consumers receive the same 404 for hidden and absent entities. Administrators receive all active current states. Results include the opaque JSON payload and state fields permitted by the contract.

### 11.3 Administrative state

The administrator endpoint invokes one write use case. The use case loads the current row, changes only AdministrativeDisabled and its administrative audit metadata when needed, handles RowVersion concurrency, and returns the final state. Repeating the value is successful. It never writes publication, version, payload, CurrentVersionOccurredAtUtc, EntityEventHighWatermarkUtc, generation, revision, tombstone, or CMS processing-log data.

## 12. Observability and security hardening

Configure:

- Structured request and event logging with an allowlist of safe fields.
- Trace/correlation propagation and BatchId scope.
- Metrics for outcomes, codes, latency, authentication failures, transient retries, deadlocks, and readiness.
- Global exception handling to Problem Details with safe 500/503 classification.
- Request-body logging disabled for the webhook and Authorization redaction globally.
- No raw payload in log message templates, scopes, exception enrichment, or CmsEventProcessingLogs.
- /health/live without dependencies and /health/ready with short write/read SQL checks.
- Cache-Control: no-store on auth failures, Problem Details, and entity API responses.
- Startup configuration validation for credentials and connection strings.

Tests inspect captured logs and database processing records with sentinel secrets/payloads to prove absence, rather than relying only on code review.

## 13. Testing strategy

### 13.1 Unit tests

CmsSync.UnitTests will cover:

- Every row and boundary of the version, same-version status, delete, recreation, and visibility tables.
- High-watermark non-regression, including Version 5 at 10:00, Version 6 at 09:00, and deletes at 09:30, 10:00, and after 10:00.
- Administrative-disable preservation/removal.
- Canonical JSON golden cases, SHA-256 identities, external EventId reuse, raw-array envelope validation, wire `id` mapping, property-name case sensitivity, all required event-type casing/whitespace normalization cases, and timestamp normalization.
- Individual validation null/empty/type/range/size/applicability cases.
- Summary count calculation and sequential application-service behavior using narrow fakes only where database behavior is irrelevant.

Use data-driven tests for transition matrices. Domain tests must not reference EF Core or ASP.NET Core.

### 13.2 SQL Server integration tests

CmsSync.IntegrationTests will use a real migrated SQL Server database through Testcontainers on supported x86-64 environments. Verification is split at the executable production-code boundary:

T008 establishes the relational test boundary and verifies infrastructure that does not require the production event transaction executor:

- A supported SQL Server Testcontainers fixture, clean database creation, and migration application.
- Schema metadata, table/column definitions, explicit collations, JSON checks, keys/foreign keys, the filtered unique idempotency-owner constraint, revision uniqueness, and rowversion mappings.
- Separate required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc datetime2(7) columns.
- Both DbContexts and production DI registrations, no-tracking read behavior, throwing CmsReadDbContext SaveChanges, and direct database constraint failures.

T009 then implements the production transaction executor and proves behavior through that real processing path:

- One durable transaction per event, state/log atomicity, partial batch durability, cancellation after prior commits, bounded transient retry, and ambiguous-commit-safe whole-request replay.
- Exact/derived replay classification, external EventId reuse conflicts, and idempotency-owner races.
- Revision immutability, delete/recreation, both active-timestamp updates, and delete decisions against EntityEventHighWatermarkUtc.
- Concurrent duplicate/competing events and publish/delete races from separate scopes/connections, including per-entity SQL Server serialization and high-watermark non-regression.
- The complete AC-053 and AC-057 production flows, including Version 5 at 10:00, Version 6 at 09:00, and deletes at 09:30, 10:00, and after 10:00.

Later API integration tests verify the same behavior through the real HTTP pipeline. In addition, the integration suite will verify:

- Auth handlers, named schemes, policies, 401/403 challenges, endpoint status precedence, Problem Details, no-store, and serialization.
- Logs and processing records contain no sentinel payload/credential/header data.
- Liveness/readiness behavior.

EF Core InMemory and mocks cannot establish these properties. If a developer host cannot run the SQL Server container, the x86-64 CI suite remains the required verification boundary and the developer uses the documented remote SQL Server option.

### 13.3 Coverage and quality

Coverage is a diagnostic, not a substitute for scenario evidence. CI should collect coverage and enforce a reasonable threshold only after the initial baseline is measured. Build warnings, analyzer findings, failed tests, secret scanning, and an unpinned container tag all fail CI.

## 14. Docker and cross-platform strategy

During implementation:

1. Verify a supported official SQL Server Linux container tag/digest from Microsoft documentation and confirm Testcontainers compatibility.
2. Pin the verified value in Compose/CI; optionally expose a SQL_SERVER_IMAGE override whose committed default remains pinned.
3. Configure a persistent named volume, SQL health check, acceptance of the license, and an uncommitted password supplied through environment configuration.
4. Use a one-shot migration/init workflow after SQL health rather than automatic production startup migrations.
5. Put placeholders only in .env.example.
6. Document Docker Compose as an x86-64 compatible path.
7. Document remote SQL Server or Azure SQL connection instructions for Apple Silicon. Do not describe Rosetta/QEMU emulation as supported.

No SQL Server LocalDB dependency will be added. The application Dockerfile and Compose setup belong to Phase 3 Task T015, not this phase.

## 15. Delivery, migration, and rollback strategy

The challenge begins from an empty database, so the initial migration creates all four tables atomically. Production-like deployment order is:

1. Provision/write secrets and read/write SQL principals.
2. Start/verify SQL Server.
3. Run the migration as a dedicated deployment step using the write/migration identity.
4. Start the API and wait for readiness.
5. Run smoke tests for named authentication and read/write paths.

Do not auto-migrate on normal API startup outside an explicit local option.

Before data exists, rollback is removal of the new application and empty schema through a reviewed rollback migration or environment recreation. After data exists, destructive rollback requires backup/restore planning because revisions and tombstones are correctness data. No implementation task should silently drop data.

Each numbered task is a suggested later commit boundary so changes remain reviewable. Execution of T001 is expressly authorized to run `git init` and only initialize the local repository. Its whitespace check uses `git add --intent-to-add .` solely to expose untracked files to `git diff --check`; it does not stage file contents. T001 then MUST run `git reset` to restore the index, and its final `git status --short` MUST show the created files as untracked. T001 MUST NOT create a commit, push, create or configure a remote, create a GitHub repository, or create a pull request. Every commit and all other publishing/remote operations require separate authorization at a later task boundary.

## 16. Implementation phases and task mapping

| Phase | Outcome | Tasks |
|---|---|---|
| 1. Repository foundations | Local `git init` only, SDK/build/package conventions, and safe config placeholders | T001 |
| 2. Solution scaffold | Traditional solution and six project dependency graph | T002 |
| 3. Pure event-processing model | Domain contracts, validation/canonical identity, state machine | T003, T004 |
| 4. State-machine unit tests | Exhaustive deterministic transition and identity proof | T005 |
| 5. SQL Server models/configurations | Four-table EF model and two contexts | T006 |
| 6. Migrations | Initial write-context-owned migration | T007 |
| 7. SQL Server integration infrastructure and schema verification | Testcontainers, clean migration, relational metadata, DbContext wiring, and direct constraints | T008 |
| 8. Transactional event processing and integration proof | Production batch/executor flow, atomicity, replay, retry, delete/recreation, and concurrency | T009 |
| 9. Basic Authentication | Two named schemes, three actors, policies | T010 |
| 10. Webhook API | POST contract, validation, statuses, responses | T011 |
| 11. Read API | Role-filtered cursor list and detail | T012 |
| 12. Administrative disable API | Idempotent administrator-only override | T013 |
| 13. Observability/security | Safe logs, metrics, errors, cache, health, startup validation | T014 |
| 14. Docker/cross-platform | Pinned SQL container path and remote Apple Silicon path | T015 |
| 15. CI | x86-64 build, SQL integration, quality/security checks | T016 |
| 16. README | Setup, contracts, retry, operations, platform guidance | T017 |
| 17. Final review | Full traceability, tests, diff/security/contract audit | T018 |

Controllers are intentionally after the pure model and its unit tests.

## 17. Phase-by-phase validation commands

Commands are planned for Phase 3 and run from the repository root. Exact filters may be refined when test names exist.

### Foundations (T001)

    git init
    git status --short
    git add --intent-to-add .
    git diff --check
    git reset
    git status --short
    dotnet --info
    dotnet --list-sdks

`git add --intent-to-add .` is a non-content-staging inspection step that makes the currently untracked specification and T001 files visible to `git diff --check`. `git reset` MUST immediately restore the index; the second status check MUST show the files as untracked. These commands do not authorize a commit or any remote, push, GitHub repository, or pull-request operation.

### Solution scaffold (T002)

    dotnet restore LateralChallenge.sln
    dotnet build LateralChallenge.sln --configuration Release --no-restore

### Pure model

    dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Processing"

### Persistence and migration

    dotnet ef migrations list --project src/CmsSync.Infrastructure --startup-project src/CmsSync.Api --context CmsWriteDbContext
    dotnet ef database update --project src/CmsSync.Infrastructure --startup-project src/CmsSync.Api --context CmsWriteDbContext

### Relational integration

    dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=SqlServer"

### Authentication and APIs

    dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=Api"

### Docker

    docker compose config
    docker compose up --build --wait
    docker compose ps

### Final

    dotnet test LateralChallenge.sln --configuration Release
    dotnet build LateralChallenge.sln --configuration Release --no-restore
    dotnet format LateralChallenge.sln --verify-no-changes --no-restore
    git diff --check
    git status --short

The final Docker environment will be stopped with a scoped docker compose down command after test evidence is captured; persistent-volume deletion is not part of routine validation.

## 18. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Delete has timestamp only | Old/new incarnations can be ambiguous | Retain tombstone, require strictly later recreation, document limitation, request CMS sequence/incarnation |
| Canonical JSON ambiguity | False duplicate or payload conflict | Normative equality rules, length-prefixed representation, golden tests, raw first payload preserved |
| Concurrent absent-row/create/delete races | Lost updates or duplicate generations | Per-entity SQL serialization plus unique constraints and real concurrency tests |
| Response lost after commits | CMS retries already-applied work | Durable per-event identity and whole-request retry guidance |
| Basic credentials leak | Security compromise | Secret-only configuration, no request-body/header logs, fixed-time comparison, sentinel leakage tests, HTTPS |
| Read context writes accidentally | Data corruption | Read abstraction, no tracking, throwing SaveChanges, production SELECT-only login |
| Mutable/unsupported SQL image | Non-reproducible or unsupported CI | Verify official support during implementation and pin tag/digest; remote option for Apple Silicon |
| Large opaque payloads | Memory/SQL pressure | Server request limit, per-payload UTF-8 limit, streaming duplicate detection |
| Tombstone/log retention grows indefinitely | Storage growth | Accept for challenge; add explicit retention only after external requirements and safety analysis |
| Administrator reads reveal unpublished content | Privacy mismatch | Confirm contract question before production; keep role tests explicit |

## 19. Open questions

The unresolved external contract questions are authoritative in spec.md Section 20. The most implementation-sensitive are:

- Availability and scope of immutable CMS EventId.
- A CMS sequence/generation/incarnation signal for delete and recreation.
- Timestamp precision and identifier case semantics.
- Payload exposure/retention constraints and operational size limits.
- CMS batch retry/acknowledgement behavior.
- Read-replica consistency expectations.

No material placeholder is left in the chosen challenge implementation. T001 and every later Phase 3 challenge task may proceed under the deterministic assumptions in spec.md Section 18; an unresolved external question does not block that work when an applicable assumption exists. Implementation pauses only if newly discovered information invalidates an adopted API contract, ordering rule, identity rule, security rule, or persistence invariant. Production-integration readiness MUST NOT be claimed until the applicable external questions are confirmed.
