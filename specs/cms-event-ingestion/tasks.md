# Tasks: CMS Event Ingestion and Entity Visibility

**Status:** Not started\
**Specification:** spec.md\
**Plan:** plan.md

No task is complete in Phase 2.2. Paths below are expected Phase 3 files, not files authorized for creation now. Every task is intentionally ordered by dependency.

## 1. Repository foundations

- [x] **T001 — Establish repository-wide .NET and secret-safety conventions**
  - **Objective:** Initialize the local repository with `git init`, pin a verified stable .NET 10 SDK, centralize build/package conventions, enable nullable/analyzers, and create ignore/placeholder configuration without real credentials. This authorization covers local Git initialization and the non-destructive local inspection sequence only: T001 MUST NOT create a commit, push, create or configure a remote, create a GitHub repository, or create a pull request.
  - **Expected files:** local .git metadata created only by `git init`; global.json; Directory.Build.props; Directory.Packages.props; .editorconfig; .gitignore; .env.example; AGENTS.md.
  - **Requirements / criteria:** NFR-001, NFR-006, NFR-008, SEC-001, SEC-003; AC-051, AC-052, AC-056.
  - **Tests to create:** No executable tests; add a secret-placeholder inspection checklist and SDK-resolution evidence.
  - **Validation commands:** git init; git status --short; git add --intent-to-add .; git diff --check; git reset; git status --short; dotnet --info; dotnet --list-sdks; secret scanner selected for CI. `git add --intent-to-add .` is used only to expose untracked files to `git diff --check` and does not stage file contents; `git reset` MUST restore the index immediately afterward.
  - **Dependencies:** None.
  - **Completion criteria:** The local repository is initialized; the intent-to-add/diff/reset sequence passes; the final `git status --short` shows the existing and T001-created files as untracked; the repository has no commit and no configured remote; the selected SDK exists; central versions target EF Core 10; real secrets are ignored; placeholder names contain no credential values; and no source/project scaffold exists beyond this task's scope. Any commit, remote configuration, push, GitHub repository, or pull request remains separately authorized at a later boundary.
  - **Completion evidence (2026-07-31):** Selected stable SDK 10.0.302 with `rollForward` set to `disable` and prerelease resolution disabled. Created only `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `.env.example`, `AGENTS.md`, and local `.git` metadata. `dotnet --version`, `dotnet --info`, `dotnet --list-sdks`, JSON/XML parsing, package-policy checks, ignore checks, placeholder inspection, focused credential-pattern inspection, authorized-file inspection, and the required `git status --short` → `git add --intent-to-add .` → `git diff --check` → `git reset` → `git status --short` sequence passed. The final index is empty, no commit or remote exists, all repository files remain untracked, and T002 was not started.
  - **Suggested commit boundary:** Repository foundations and safe configuration policy, to be committed only after separate authorization.

## 2. Solution scaffold

- [x] **T002 — Create the traditional solution and six-project dependency graph**
  - **Objective:** Scaffold Domain, Application, Infrastructure, API, unit-test, and integration-test projects with only the dependency direction defined in plan.md.
  - **Expected files:** LateralChallenge.sln; src/CmsSync.Domain/CmsSync.Domain.csproj; src/CmsSync.Application/CmsSync.Application.csproj; src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj; src/CmsSync.Api/CmsSync.Api.csproj; tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj; tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj; minimal generated project entry files.
  - **Requirements / criteria:** NFR-001, NFR-005; AC-052.
  - **Tests to create:** One architecture dependency test or equivalent project-reference inspection preventing outward Domain/Application references.
  - **Validation commands:** dotnet restore LateralChallenge.sln; dotnet build LateralChallenge.sln --configuration Release --no-restore; dotnet sln LateralChallenge.sln list.
  - **Dependencies:** T001.
  - **Completion criteria:** The traditional .sln builds on .NET 10, references are inward-only, and no excluded framework/pattern dependency is present.
  - **Completion evidence (2026-07-31):** Created the traditional `LateralChallenge.sln`, its `src`/`tests` solution folders, and exactly the six planned projects with the approved reference graph. A repository-root-relative xUnit project-reference test passed 1/1; the integration-test project executed successfully with zero feature tests. Restore succeeded with a process-scoped official NuGet.org source override because the host configuration also defines an inaccessible Nexus source; no NuGet configuration was persisted. Release build passed with 0 warnings and 0 errors. Direct/transitive package inspection found only the authorized test packages and their expected runtime/test dependencies, with no EF Core provider, EF Core InMemory, alternate database provider, MediatR, AutoMapper, or architecture-test package. Solution/reference inspection passed; the final Git index is empty, and no commit or remote exists. T003 was not started.
  - **Suggested commit boundary:** Compile-clean solution scaffold.

## 3. Pure event-processing model

- [x] **T003 — Implement the pure state model and transition engine**
  - **Objective:** Represent validated versioned/delete events, current entity/tombstone/revision snapshots, decisions, codes, generations, publication state, CurrentVersionOccurredAtUtc, monotonic EntityEventHighWatermarkUtc, and administrative state; implement spec.md transition precedence and tables without ASP.NET Core or EF Core. Same-version ordering uses the current-version timestamp; delete uses only the entity high watermark.
  - **Expected files:** src/CmsSync.Domain/Entities/*; src/CmsSync.Domain/Events/*; src/CmsSync.Domain/Processing/CmsEntityStateMachine.cs; src/CmsSync.Domain/Processing/ProcessingDecision.cs; src/CmsSync.Domain/Processing/ProcessingCodes.cs.
  - **Requirements / criteria:** FR-009, FR-013, FR-014, FR-015, FR-016, FR-017, FR-018, FR-019, FR-020, FR-021, NFR-002, NFR-007; AC-014, AC-018–AC-034, AC-053, AC-057.
  - **Tests to create:** Defer executable transition matrices to T005; include compile-time invariants/value-object guard tests for the two distinct timestamps and non-regressive high watermark only if needed alongside types.
  - **Validation commands:** dotnet build src/CmsSync.Domain/CmsSync.Domain.csproj --configuration Release.
  - **Dependencies:** T002.
  - **Completion criteria:** Domain compiles framework-free; every transition table row returns one closed deterministic decision; a higher Version may move CurrentVersionOccurredAtUtc backward but cannot regress EntityEventHighWatermarkUtc; delete compares only against the high watermark; CMS transitions cannot clear the local flag; delete/recreation semantics exactly match spec.md.
  - **Completion evidence (2026-07-31):** Created the framework-independent immutable state/value types under `src/CmsSync.Domain/Entities`, validated publish/unpublish/delete events and UTC timestamp type under `Events`, and typed outcomes, stable codes, closed state operations, decisions, and the pure `CmsEntityStateMachine` under `Processing`. Direct review covered every versioned, same-version, delete, tombstone, recreation, high-watermark, and administrative-flag row in spec.md Sections 12.2–12.5, including AC-057's 10:00 high-watermark non-regression sequence. Official NuGet.org restore repaired generated assets without repository configuration changes; the Domain and full Release builds then passed with 0 warnings and 0 errors, and the existing architecture test passed 1/1. Package inspection reported no Domain packages, prohibited dependency scanning found no framework/database/JSON/logging/DI/filesystem/network references, only authorized T003 paths and this evidence changed, and T004 was not started.
  - **Correction evidence (2026-08-01):** Internal snapshot and programmer inconsistencies now throw safe `InvalidOperationException` failures before creating state operations; obsolete internal outcome codes were removed while valid CMS event outcomes and `GENERATION_EXHAUSTED` remain unchanged. Domain and solution Release builds passed with 0 warnings and 0 errors, the architecture test passed 1/1, `git diff --check` passed, only authorized files changed, and T004 remains unstarted.
  - **Suggested commit boundary:** Pure domain transition model.

- [x] **T004 — Implement event validation, canonical payload equality, and idempotency identity**
  - **Objective:** Add raw-array JSON envelope inspection, per-event/payload duplicate-name detection, case-sensitive wire `id` mapping to internal EntityId, trim-aware case-insensitive `type` normalization, known-field validation, canonical payload hashing, normalized event-content hashing, external/derived key namespacing, and size/range checks without mutating payload content.
  - **Expected files:** src/CmsSync.Application/EventIngestion/CmsEventArrayParser.cs; src/CmsSync.Application/EventIngestion/EventValidator.cs; src/CmsSync.Application/EventIngestion/CanonicalJson.cs; src/CmsSync.Application/EventIngestion/EventIdentityFactory.cs; related contracts/constants.
  - **Requirements / criteria:** FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, NFR-003, NFR-007; AC-009–AC-017, AC-054.
  - **Tests to create:** Raw-array and invalid top-level cases; wire `id` versus rejected `entityId`; every required event-type casing and surrounding-whitespace case; canonical lowercase identity equivalence; golden canonicalization/identity cases; event/payload duplicate-name cases; unknown event-property cases; timestamp normalization/precision; ID/version/payload/request bounds; and payload-preservation cases.
  - **Validation commands:** dotnet build src/CmsSync.Application/CmsSync.Application.csproj --configuration Release; dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --filter "FullyQualifiedName~Canonical|FullyQualifiedName~Validation|FullyQualifiedName~Identity".
  - **Dependencies:** T002, T003.
  - **Completion criteria:** The parser requires a raw array with no `events` wrapper; wire `id` is the only external entity property; type casing/whitespace variants produce the same canonical type and identity; exact redelivery produces a stable key; EventId reuse can compare a content hash; duplicate event/payload names are detected before permissive binding; and raw payload content is never logged or rewritten.
  - **Suggested commit boundary:** Validation and deterministic identity primitives.
  - **Completion evidence (2026-08-01):** Added bounded raw-array parsing, per-item validation and duplicate-name detection, canonical payload hashing, normalized event-content hashing, namespaced external/derived identities, and construction of the existing validated Domain events. Direct review covered FR-006–FR-012, NFR-003, NFR-007, AC-009–AC-017, and AC-054. The Application and full solution Release builds passed with 0 warnings and 0 errors; all 133 EventIngestion/Architecture tests passed; full solution format verification and `git diff --check` passed; Domain remained unchanged; and T005 was not started.

## 4. State-machine unit tests

- [x] **T005 — Freeze every pure transition and identity boundary with data-driven unit tests**
  - **Objective:** Turn the specification tables into exhaustive tests before persistence/orchestration can obscure state-machine defects.
  - **Expected files:** tests/CmsSync.UnitTests/Processing/CmsEntityStateMachineTests.cs; tests/CmsSync.UnitTests/Processing/DeleteTransitionTests.cs; tests/CmsSync.UnitTests/Processing/RecreationTests.cs; tests/CmsSync.UnitTests/EventIngestion/CanonicalJsonTests.cs; tests/CmsSync.UnitTests/EventIngestion/EventValidationTests.cs; tests/CmsSync.UnitTests/Visibility/VisibilityPolicyTests.cs.
  - **Requirements / criteria:** FR-006–FR-021, FR-022, FR-023, NFR-002, NFR-005, NFR-007; AC-009–AC-035, AC-053, AC-055, AC-057.
  - **Tests to create:** All files named above, including lower/higher/same version, immutable payload, CurrentVersionOccurredAtUtc/status matrix, X+1 unpublish, high-watermark max/non-regression, every delete row against EntityEventHighWatermarkUtc, the exact 10:00 → higher Version at 09:00 → delete 09:30/equal/after scenario, publish and unpublish recreation initializing both timestamps, admin-flag lifecycle, raw-array/wire-`id`/event-type casing tests, canonical JSON golden vectors, and normal/admin visibility.
  - **Validation commands:** dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Processing|FullyQualifiedName~EventIngestion|FullyQualifiedName~Visibility".
  - **Dependencies:** T003, T004.
  - **Completion criteria:** Every AC-009 through AC-035 plus AC-057 has a named passing test; EntityEventHighWatermarkUtc never regresses; mutation inputs are not modified; tests do not reference EF Core/ASP.NET Core; boundary cases include long.MaxValue and timestamp equality.
  - **Completion evidence (2026-08-01):** Added exhaustive data-driven creation, version, same-version payload/status/timestamp, delete, recreation, fail-fast invariant, value-object, canonical JSON, event-identity, and four-state visibility coverage plus the minimal framework-free `EntityVisibilityPolicy`. The focused Processing/EventIngestion/Visibility suite passed 238/238 and the full unit suite passed 239/239 with 0 skipped tests in two consecutive runs; the Release solution build passed with 0 warnings and 0 errors, full format verification and `git diff --check` passed, and NuGet assets were regenerated from NuGet.org only without repository configuration changes. Input mutation, UTF-8 byte limits, timestamp equality, high-watermark non-regression, defensive hash copies, and `long.MaxValue` version/generation-exhaustion boundaries are covered. T006 remains unchecked and unimplemented.
  - **T005 AC traceability:** AC-009 → `EventValidatorTests.AC009DocumentedEventTypeVariantsNormalizeToCanonicalLowercase`; AC-010 → `AC010EntityIdAliasesAreIgnoredAndWireIdRemainsRequired` and `AC010NonObjectItemsAreIndividuallyInvalid`; AC-011 → `AC011UnknownEnvelopePropertiesAreIgnored` and `CanonicalJsonTests.AC011ValidationPreservesRawPayloadWhileUsingCanonicalEquality`; AC-012 → `DuplicatePropertyTests.AC012DuplicateNamesMakeOnlyTheContainingItemInvalid`; AC-013 → `AC013WireIdIsAcceptedMappedAndPreservedExactly`, `AC013VersionedEventsRequireVersionAndPayload`, and `AC013DeleteRejectsVersionAndPayload`; AC-014 → `CmsEntityStateMachineTests.AC014ArbitraryFirstVersionAndHigherVersionGapAreAccepted`; AC-015–AC-017 → the correspondingly numbered methods in `EventIdentityFactoryTests`; AC-018–AC-025 and AC-033 → the correspondingly numbered methods in `CmsEntityStateMachineTests`; AC-026–AC-029 and AC-057 → the correspondingly numbered methods in `DeleteTransitionTests`; AC-030–AC-032 and AC-034 → the correspondingly numbered methods in `RecreationTests`; AC-035 → `VisibilityPolicyTests.AC035NormalConsumerSeesOnlyPublishedAndAdministrativelyEnabledEntities`; AC-053 → `CmsEntityStateMachineTests.AC053PureDecisionIsDeterministicAndNeverRegressesTheHighWatermark`; AC-055 → `SpecificationBoundaryTests.AC055PureDomainAndEventIngestionAssembliesDoNotReferenceEfCoreOrAspNetCore`.
  - **Suggested commit boundary:** Pure state-machine and identity specification tests.

## 5. SQL Server models and configurations

- [ ] **T006 — Model the four-table SQL Server schema and read/write contexts**
  - **Objective:** Implement explicit EF Core 10 SQL Server models/configurations, including separate required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc columns on CmsEntities, immutable revision behavior, tombstone/log isolation, case-sensitive keys, rowversion, and no-tracking read projections.
  - **Expected files:** src/CmsSync.Infrastructure/Persistence/CmsWriteDbContext.cs; src/CmsSync.Infrastructure/Persistence/CmsReadDbContext.cs; src/CmsSync.Infrastructure/Persistence/Models/*; src/CmsSync.Infrastructure/Persistence/Configurations/*; src/CmsSync.Application/Abstractions/ICmsEntityQueries.cs; read projection records.
  - **Requirements / criteria:** FR-014, FR-016, FR-018, FR-019, FR-020, FR-021, FR-028, FR-029, NFR-007, SEC-003, SEC-004; AC-025–AC-034, AC-044–AC-047, AC-057.
  - **Tests to create:** EF model metadata tests for exactly four tables, both distinct required active timestamp columns with datetime2(7), keys, filtered indexes, types, precision, collation, JSON constraints, rowversion, delete behavior, migration owner, no-tracking query shape, and throwing read SaveChanges.
  - **Validation commands:** dotnet build src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj --configuration Release; dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --filter "FullyQualifiedName~Model".
  - **Dependencies:** T002, T003, T004, T005.
  - **Completion criteria:** The EF model matches spec.md Section 13; CmsEntities persists both timestamps without aliasing or a shared column; one filtered-unique owner per idempotency key and self-referenced replay rows share the required processing-log table; no batch/attempt/processed-event duplicate table exists; write/read boundaries are separate; and logs cannot map raw payload/auth fields.
  - **Suggested commit boundary:** EF Core SQL Server model and context boundaries.

## 6. Migrations

- [ ] **T007 — Create and review the initial write-context migration**
  - **Objective:** Generate the initial SQL Server migration owned only by CmsWriteDbContext and review generated DDL for every relational invariant, including separate required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc datetime2(7) columns.
  - **Expected files:** src/CmsSync.Infrastructure/Persistence/Migrations/*; optional migration-bundle/deployment documentation referenced by README later.
  - **Requirements / criteria:** FR-018, FR-028, FR-029, NFR-007; AC-044, AC-057.
  - **Tests to create:** Clean-database migration-up test and schema inspection proving both active timestamp columns are distinct/required with datetime2(7); optional migration-script smoke test.
  - **Validation commands:** dotnet ef migrations list --project src/CmsSync.Infrastructure --startup-project src/CmsSync.Api --context CmsWriteDbContext; dotnet ef migrations script --idempotent --project src/CmsSync.Infrastructure --startup-project src/CmsSync.Api --context CmsWriteDbContext.
  - **Dependencies:** T006.
  - **Completion criteria:** A clean SQL Server database migrates successfully; generated schema has both active timestamp columns plus all intended constraints/indexes/collation and no payload in tombstones/logs; CmsReadDbContext has no migration.
  - **Suggested commit boundary:** Reviewed initial SQL Server migration.

## 7. SQL Server integration infrastructure and schema verification

- [ ] **T008 — Establish SQL Server integration infrastructure and verify the relational schema**
  - **Objective:** Create the supported SQL Server Testcontainers fixture, create a clean database, apply the write-context migration, and verify relational metadata, direct database constraints, read/write DbContext registration, and read-only context behavior without depending on production event-processing orchestration introduced in T009.
  - **Expected files:** tests/CmsSync.IntegrationTests/Infrastructure/SqlServerFixture.cs; test collection/settings files; tests/CmsSync.IntegrationTests/Persistence/MigrationTests.cs; SchemaMetadataTests.cs; DatabaseConstraintTests.cs; DbContextRegistrationTests.cs; ReadContextTests.cs.
  - **Requirements / criteria:** FR-028, FR-029, NFR-005, NFR-007, SEC-004; AC-044, AC-046, AC-047, AC-055.
  - **Tests to create:** Supported Testcontainers startup; clean database creation and migration application; exactly four tables; table and column metadata; separate required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc datetime2(7) columns; explicit collations; JSON checks; keys and foreign keys; filtered unique idempotency-owner constraint; revision uniqueness; rowversion mappings; production read/write DbContext registrations; no-tracking read behavior; CmsReadDbContext SaveChanges/SaveChangesAsync rejection; direct duplicate-key, invalid-JSON, foreign-key, filtered-owner, and revision-uniqueness violations; SQL read-login write denial where CI setup permits.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=SqlServer".
  - **Dependencies:** T006, T007.
  - **Completion criteria:** The supported fixture reliably produces a clean migrated SQL Server database; schema metadata and direct constraints match spec.md Section 13 and AC-044; both DbContexts resolve through production registrations; reads are no-tracking; CmsReadDbContext rejects saves; and no EF Core InMemory provider is referenced. This task does not claim complete per-event transaction orchestration, replay classification, retry or ambiguous-commit recovery, application-level delete/recreation, event-processing concurrency serialization, sp_getapplock behavior, or complete AC-053/AC-057 production flow.
  - **Suggested commit boundary:** SQL Server integration fixture, schema, constraints, and DbContext proof.

## 8. Transactional event processing and integration proof

- [ ] **T009 — Implement and prove transactional event processing on SQL Server**
  - **Objective:** Implement CmsEventBatchService, the application-owned event transaction port, SqlServerEventTransactionExecutor, and the selected per-entity SQL Server locking strategy; process items sequentially; atomically persist state and processing logs; handle idempotency ownership, exact duplicate classification, exact/derived replay, EventId reuse conflicts, retry and ambiguous commits safely; execute delete/recreation; update both active timestamps without regressing EntityEventHighWatermarkUtc; make delete decisions from that high watermark; and propagate cancellation without undoing prior commits.
  - **Expected files:** src/CmsSync.Application/EventIngestion/CmsEventBatchService.cs; application event-transaction port/contracts; src/CmsSync.Infrastructure/Persistence/SqlServerEventTransactionExecutor.cs; SQL Server per-entity lock helper; event-processing dependency registrations; tests/CmsSync.UnitTests/EventIngestion/CmsEventBatchServiceTests.cs; tests/CmsSync.IntegrationTests/EventIngestion/TransactionalEventProcessingTests.cs; IdempotencyReplayTests.cs; DeleteRecreationProcessingTests.cs; EventProcessingConcurrencyTests.cs.
  - **Requirements / criteria:** FR-003–FR-005, FR-009–FR-021, FR-028, NFR-002, NFR-004, NFR-005, NFR-007; AC-006–AC-008, AC-014–AC-034, AC-045, AC-053, AC-055, AC-057.
  - **Tests to create:** Sequential call order and mixed-outcome summary; one durable transaction per event; earlier committed items surviving a later failure; whole-request retry without reapplying committed items; exact external-identity replay; derived-key replay; EventId content conflict; replay BatchId/Sequence and owner reference; original invalid/conflict replay preservation; state/log atomicity; revision immutability through production processing; delete and publish/unpublish recreation through production processing; higher-version older-timestamp processing; bounded transient retry; simulated ambiguous-commit replay; cancellation after prior commit; concurrent ownership races and competing events from independent scopes; concurrent publish/delete races; and proof of the selected per-entity serialization strategy, including sp_getapplock behavior when that strategy is selected. AC-053 and AC-057 tests MUST include active Version 5 with both timestamps at 10:00, accepted Version 6 at 09:00, CurrentVersionOccurredAtUtc at 09:00, EntityEventHighWatermarkUtc still at 10:00, delete 09:30 stale, delete 10:00 under a new identity conflict, and delete after 10:00 applied.
  - **Validation commands:** dotnet test tests/CmsSync.UnitTests/CmsSync.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~BatchService"; dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=EventProcessing".
  - **Dependencies:** T008.
  - **Completion criteria:** Raw-array positions are awaited strictly in order; one durable transaction owns each event; every completed result has durable log evidence; state/log and both timestamp updates commit atomically; exact/derived replay and EventId conflicts are deterministic; bounded retry and ambiguous-commit recovery are safe; revision immutability, delete, and recreation pass through production processing; concurrent competitors serialize per entity with no double mutation or lost update; EntityEventHighWatermarkUtc never regresses; delete uses that high watermark; AC-053 and AC-057 pass against SQL Server; server failure or cancellation stops later processing while preserving prior commits; and whole-request retry is safe.
  - **Suggested commit boundary:** Production transactional event-processing flow and SQL Server integration proof.

## 9. Basic Authentication

- [ ] **T010 — Implement isolated CMS and consumer Basic schemes and policies**
  - **Objective:** Add configuration-backed CMS, normal-consumer, and administrator identities; fixed-time credential verification; startup validation; named challenges; and policy isolation.
  - **Expected files:** src/CmsSync.Infrastructure/Authentication/BasicAuthenticationHandler.cs; CredentialOptions.cs; CredentialOptionsValidator.cs; AuthenticationRegistration.cs; src/CmsSync.Api authentication/authorization composition; integration auth tests.
  - **Requirements / criteria:** FR-026, FR-027, SEC-001, SEC-002, SEC-003, NFR-008; AC-040–AC-043, AC-050, AC-051.
  - **Tests to create:** Missing/malformed/wrong credentials; valid three actors; CMS username boundaries; GUID password format; duplicate identities; correct realms; CMS/consumer cross-scheme 401; normal-consumer admin 403; fixed-time comparison component; no secret/header logging.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=Authentication".
  - **Dependencies:** T002, T009.
  - **Completion criteria:** Real middleware tests prove scheme/policy boundaries and 401/403 behavior; configuration fails closed; no real credential or decoded value appears in source, config, results, or logs.
  - **Suggested commit boundary:** Isolated Basic authentication and authorization.

## 10. Webhook API

- [ ] **T011 — Expose the CMS batch webhook and exact HTTP/JSON contract**
  - **Objective:** Implement POST /cms/events with a raw JSON array request (no `events` wrapper), request-size/media/envelope validation, case-sensitive wire `id`, trim-aware case-insensitive event-type normalization, CmsEvents policy, response `id`, 200 per-item response, summary counts, status precedence, and safe Problem Details; wire the T009 production batch/transaction flow and verify its transactional, replay, delete/recreation, and concurrency behavior through the real HTTP pipeline.
  - **Expected files:** src/CmsSync.Api/Controllers/CmsEventsController.cs or equivalent route module; src/CmsSync.Api/Contracts/CmsEvents/*; JSON/size middleware or filters; endpoint integration tests.
  - **Requirements / criteria:** FR-001–FR-021, FR-028, NFR-002–NFR-005, NFR-007, NFR-008, SEC-003; AC-001–AC-034, AC-045, AC-048, AC-051, AC-053–AC-055, AC-057.
  - **Tests to create:** Raw arrays with 1/50/0/51 items; top-level object/null/string/number; explicit rejection of an `{ "events": [...] }` wrapper; malformed JSON; duplicate names only within event/payload as individual invalid results; unknown event properties; wire `id` acceptance and `entityId` rejection; `publish`, `Publish`, `PUBLISH`, `unpublish`, `unPublish`, `UnPublish`, `UNPUBLISH`, `delete`, `Delete`, `DELETE`, and surrounding-whitespace normalization; unsupported types; publish/unpublish/delete applicability; 16 MiB and 256 KiB boundaries; mixed six-outcome 200; 415/400/413/500/503; ordered results with response `id`; exact summary; and no payload in results/logs. Through the real endpoint and production DI/SQL wiring, also prove one durable transaction per item; earlier-commit/later-failure survival and whole-request retry; exact and derived replay; EventId reuse conflict; state/log atomicity; revision immutability; delete and publish/unpublish recreation; concurrent duplicate/competing and publish/delete requests from independent clients; per-entity serialization; AC-053; and Version 5 at 10:00 → Version 6 at 09:00 with high watermark 10:00 → deletes at 09:30/10:00/after 10:00 for AC-057.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=Webhook".
  - **Dependencies:** T004, T005, T009, T010.
  - **Completion criteria:** The endpoint matches spec.md Section 10.1 byte-level raw-array, property-name, event-type normalization, response, and status tests; T009 transactional/replay/delete/recreation/concurrency behavior, including AC-053 and AC-057, passes through production parsing, authentication, DI, SQL, and HTTP wiring; and the endpoint contains no business-state decisions.
  - **Suggested commit boundary:** CMS webhook transport contract.

## 11. Read API

- [ ] **T012 — Implement role-filtered cursor list and detail queries**
  - **Objective:** Add deterministic no-tracking SQL projections for normal/admin visibility, ordinal cursor pagination, opaque payload response, and non-disclosing 404 behavior.
  - **Expected files:** src/CmsSync.Application/EntityQueries/*; src/CmsSync.Infrastructure/Persistence/CmsEntityQueries.cs; src/CmsSync.Api/Controllers/EntitiesController.cs; src/CmsSync.Api/Contracts/Entities/*; read API integration tests.
  - **Requirements / criteria:** FR-022, FR-023, FR-024, FR-029, NFR-007, NFR-008; AC-035–AC-038, AC-042, AC-046.
  - **Tests to create:** Four-state visibility matrix; admin state fields; hidden/deleted/unknown indistinguishable 404; default/min/max/invalid page size; case-distinct IDs across pages; no duplicates/gaps; no tracking; SQL-side filtering/projection; no-store.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=ReadApi".
  - **Dependencies:** T006, T008, T010.
  - **Completion criteria:** Query SQL applies role filter/order/limit before projection, the read context never saves/tracks, and endpoint behavior matches the visibility table.
  - **Suggested commit boundary:** Consumer read API.

## 12. Administrative disable API

- [ ] **T013 — Implement the administrator-only local override**
  - **Objective:** Add idempotent PUT administrative-state behavior that changes only the local flag/audit metadata and handles unknown/deleted/concurrent entities safely.
  - **Expected files:** src/CmsSync.Application/AdministrativeState/*; src/CmsSync.Infrastructure/Persistence/CmsAdministrativeStateService.cs; src/CmsSync.Api administrative-state contract/endpoint; integration tests.
  - **Requirements / criteria:** FR-021, FR-023, FR-025, FR-027, NFR-002; AC-033, AC-034, AC-036, AC-039, AC-040, AC-053.
  - **Tests to create:** true/false/repeat; normal consumer 403; administrator success; unknown/deleted 404; publish/unpublish preserves flag; delete/recreation resets it; rowversion/concurrent admin update; CMS fields/revisions/logs unchanged.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=AdministrativeState".
  - **Dependencies:** T008, T010, T012.
  - **Completion criteria:** Only AdministrativeDisabled and its local audit metadata can change; the operation never calls or models CMS propagation; all lifecycle/concurrency tests pass.
  - **Suggested commit boundary:** Local administrative-state API.

## 13. Observability and security hardening

- [ ] **T014 — Add safe operational telemetry, health, error handling, and cache/TLS protections**
  - **Objective:** Implement structured allowlisted logs, metrics, trace/BatchId correlation, global safe Problem Details, readiness/liveness, no-store, request-log suppression/redaction, HTTPS requirements, and configuration validation.
  - **Expected files:** src/CmsSync.Api/Program.cs; observability/exception/headers components; health-check registrations; appsettings*.json placeholders; tests/CmsSync.IntegrationTests/Observability/*; Security/*; Health/*.
  - **Requirements / criteria:** FR-002, FR-005, FR-030, NFR-008, SEC-001–SEC-004; AC-003–AC-005, AC-008, AC-038, AC-047–AC-051.
  - **Tests to create:** Captured-log sentinel leak tests; processing-log field inspection; metric counter/label tests where stable; correlation propagation; live without SQL; ready with/without SQL; safe 500/503 Problem Details; no-store; HTTPS non-development behavior; missing config startup failure.
  - **Validation commands:** dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --configuration Release --filter "Category=Observability|Category=Security|Category=Health".
  - **Dependencies:** T009–T013.
  - **Completion criteria:** Required signals exist, readiness reflects SQL dependencies, all leak tests are negative, errors expose no internals, and no sensitive/high-cardinality telemetry fields are introduced.
  - **Suggested commit boundary:** Operational and security hardening.

## 14. Docker and cross-platform setup

- [ ] **T015 — Add reproducible SQL Server/API local setup and Apple Silicon alternative**
  - **Objective:** Verify and pin an official supported SQL Server image, configure healthy Compose startup and dedicated migrations without committed SA credentials, and document remote SQL Server/Azure SQL for Apple Silicon.
  - **Expected files:** compose.yaml; Dockerfile; .dockerignore; migration/init script or container target if required; updates to .env.example; integration-test container configuration.
  - **Requirements / criteria:** NFR-006, SEC-001, SEC-004; AC-047, AC-056.
  - **Tests to create:** Compose configuration validation; clean-volume startup/migration/API-readiness smoke test; placeholder/secret scan; image-tag policy check.
  - **Validation commands:** docker compose config; docker compose up --build --wait; docker compose ps; dotnet test tests/CmsSync.IntegrationTests/CmsSync.IntegrationTests.csproj --filter "Category=SqlServer".
  - **Dependencies:** T007, T014.
  - **Completion criteria:** The committed image is verified and immutable/pinned, SQL becomes healthy before migration/API readiness, no password is committed, no LocalDB exists, and platform guidance does not claim unsupported Apple Silicon emulation.
  - **Suggested commit boundary:** Docker and cross-platform development setup.

## 15. CI

- [ ] **T016 — Create x86-64 CI for build, real SQL tests, formatting, quality, and secret/image checks**
  - **Objective:** Make the full contract reproducibly verifiable on a supported runner, including SQL Server relational tests and container pinning.
  - **Expected files:** .github/workflows/ci.yml or the selected CI provider equivalent; optional scripts limited to deterministic validation.
  - **Requirements / criteria:** NFR-001, NFR-005, NFR-006, SEC-001, SEC-003; AC-048, AC-052, AC-055, AC-056.
  - **Tests to create:** CI smoke proof that Testcontainers starts; secret scan; mutable-latest rejection; build/test/format gates; test result and coverage publishing.
  - **Validation commands:** dotnet restore LateralChallenge.sln; dotnet build LateralChallenge.sln --configuration Release --no-restore; dotnet test LateralChallenge.sln --configuration Release --no-build; dotnet format LateralChallenge.sln --verify-no-changes --no-restore; CI workflow syntax validation.
  - **Dependencies:** T015.
  - **Completion criteria:** A clean x86-64 pipeline passes all unit/integration/API tests, fails on warnings/test failures/secrets/mutable SQL latest, and publishes usable test evidence.
  - **Suggested commit boundary:** Continuous integration and quality gates.

## 16. README

- [ ] **T017 — Document setup, contracts, retry semantics, security, and platform limits**
  - **Objective:** Give developers/operators exact local and remote setup, secret injection, migration, raw-array API examples using wire `id` and case-insensitive event-type values, both active timestamp meanings, all outcome meanings, whole-request retry rules, visibility rules, health behavior, and the remaining timestamp-only delete limitation (no CMS version/sequence/incarnation identifier).
  - **Expected files:** README.md; optional updates only to .env.example when placeholders need clarification.
  - **Requirements / criteria:** FR-001–FR-030, NFR-001, NFR-003–NFR-008, SEC-001–SEC-004; AC-001–AC-057.
  - **Tests to create:** Documentation command smoke run; link/path check; raw-array example-request contract validation with wire `id`, casing examples, timestamp/high-watermark example, and placeholders only.
  - **Validation commands:** Execute documented restore/build/test/migration/Compose commands on the relevant supported path; search README and example config for credential-like values and mutable latest.
  - **Dependencies:** T016.
  - **Completion criteria:** A new developer can run the supported path without guessing; webhook examples contain no wrapper or `entityId`; type normalization and the two timestamp roles are unambiguous; Apple Silicon has a truthful remote option; retry/no-retry guidance is unambiguous; unresolved external questions and challenge assumptions remain visible.
  - **Suggested commit boundary:** Operational and API documentation.

## 17. Final review

- [ ] **T018 — Perform full contract, traceability, security, and diff verification**
  - **Objective:** Prove implementation matches spec.md and plan.md with no orphan requirement, untested acceptance criterion, secret, payload leak, excluded dependency, accidental file, wrapper/identifier/type-normalization drift, timestamp-column conflation, watermark regression, or contradictory ordering rule.
  - **Expected files:** Updates to specs/cms-event-ingestion/spec.md, plan.md, and tasks.md only when evidence requires status/clarification; test result artifacts are CI-owned and not committed unless repository policy requires them.
  - **Requirements / criteria:** FR-001–FR-030, NFR-001–NFR-008, SEC-001–SEC-004; AC-001–AC-057.
  - **Tests to create:** Only gap-closing regression tests discovered by the audit; do not weaken existing tests to make the suite pass.
  - **Validation commands:** dotnet test LateralChallenge.sln --configuration Release; dotnet build LateralChallenge.sln --configuration Release --no-restore; dotnet format LateralChallenge.sln --verify-no-changes --no-restore; docker compose config; git diff --check; git status --short; secret/dependency/container-tag scans.
  - **Dependencies:** T017.
  - **Completion criteria:** All commands pass; every AC through AC-057 has named evidence; specification/plan/tasks and README agree on raw array, wire `id`, type normalization, both timestamp columns, and delete/high-watermark ordering; only intended files/dependencies exist; no secrets/payload leaks are found; every completed checkbox points to evidence.
  - **Suggested commit boundary:** Final review corrections and evidence status.

## 18. Requirement traceability

| Requirement | Acceptance criteria | Implementation / verification tasks |
|---|---|---|
| FR-001 | AC-001, AC-002 | T011, T017, T018 |
| FR-002 | AC-002, AC-003, AC-004, AC-005 | T011, T014, T017, T018 |
| FR-003 | AC-006 | T009, T011, T017, T018 |
| FR-004 | AC-007, AC-008 | T009, T011, T017, T018 |
| FR-005 | AC-008 | T009, T011, T014, T017, T018 |
| FR-006 | AC-009, AC-010, AC-012 | T004, T005, T011, T017, T018 |
| FR-007 | AC-011, AC-012 | T004, T005, T011, T017, T018 |
| FR-008 | AC-009, AC-010, AC-013 | T004, T005, T011, T017, T018 |
| FR-009 | AC-010, AC-014 | T003, T004, T005, T009, T011, T017, T018 |
| FR-010 | AC-015, AC-016 | T004, T005, T009, T011, T017, T018 |
| FR-011 | AC-017 | T004, T005, T009, T011, T017, T018 |
| FR-012 | AC-008, AC-015, AC-017 | T004, T005, T009, T011, T017, T018 |
| FR-013 | AC-018 | T003, T005, T009, T011, T017, T018 |
| FR-014 | AC-019, AC-020, AC-057 | T003, T005, T006, T009, T011, T017, T018 |
| FR-015 | AC-021 | T003, T005, T009, T011, T017, T018 |
| FR-016 | AC-022, AC-023, AC-024, AC-025, AC-057 | T003, T005, T006, T009, T011, T017, T018 |
| FR-017 | AC-020 | T003, T005, T009, T011, T017, T018 |
| FR-018 | AC-026, AC-027, AC-028, AC-029, AC-057 | T003, T005, T006, T007, T009, T011, T017, T018 |
| FR-019 | AC-028, AC-029, AC-045 | T003, T005, T006, T009, T011, T017, T018 |
| FR-020 | AC-030, AC-031, AC-032 | T003, T005, T006, T009, T011, T017, T018 |
| FR-021 | AC-033, AC-034 | T003, T005, T006, T009, T011, T013, T017, T018 |
| FR-022 | AC-035 | T005, T012, T017, T018 |
| FR-023 | AC-036 | T005, T012, T013, T017, T018 |
| FR-024 | AC-037, AC-038 | T012, T017, T018 |
| FR-025 | AC-039, AC-040 | T013, T017, T018 |
| FR-026 | AC-041, AC-042, AC-043 | T010, T011, T012, T017, T018 |
| FR-027 | AC-040, AC-041, AC-042, AC-043 | T010, T011, T013, T017, T018 |
| FR-028 | AC-044, AC-045 | T006, T007, T008, T009, T011, T017, T018 |
| FR-029 | AC-046, AC-047 | T006, T007, T008, T012, T017, T018 |
| FR-030 | AC-048, AC-049 | T014, T016, T017, T018 |
| NFR-001 | AC-052 | T001, T002, T016, T017, T018 |
| NFR-002 | AC-053, AC-057 | T003, T005, T009, T011, T013, T017, T018 |
| NFR-003 | AC-005, AC-054 | T001, T004, T011, T017, T018 |
| NFR-004 | AC-008 | T009, T011, T017, T018 |
| NFR-005 | AC-055 | T002, T005, T008, T009, T011, T016, T017, T018 |
| NFR-006 | AC-056 | T001, T015, T016, T017, T018 |
| NFR-007 | AC-037, AC-044, AC-057 | T003, T004, T005, T006, T007, T008, T009, T011, T012, T017, T018 |
| NFR-008 | AC-038, AC-051 | T001, T010, T011, T012, T014, T017, T018 |
| SEC-001 | AC-050, AC-056 | T001, T010, T014, T015, T016, T017, T018 |
| SEC-002 | AC-050, AC-051 | T010, T014, T017, T018 |
| SEC-003 | AC-048, AC-051 | T001, T006, T010, T011, T014, T016, T017, T018 |
| SEC-004 | AC-047 | T006, T008, T014, T015, T017, T018 |

## 19. Acceptance-criterion traceability

| Criterion | Tasks | Criterion | Tasks |
|---|---|---|---|
| AC-001 | T011, T017, T018 | AC-029 | T003, T005, T009, T011, T018 |
| AC-002 | T011, T014, T018 | AC-030 | T003, T005, T009, T011, T018 |
| AC-003 | T011, T014, T018 | AC-031 | T003, T005, T009, T011, T018 |
| AC-004 | T011, T018 | AC-032 | T003, T005, T009, T011, T018 |
| AC-005 | T011, T014, T018 | AC-033 | T003, T005, T011, T013, T018 |
| AC-006 | T009, T011, T018 | AC-034 | T003, T005, T011, T013, T018 |
| AC-007 | T009, T011, T018 | AC-035 | T005, T012, T018 |
| AC-008 | T009, T011, T014, T018 | AC-036 | T012, T013, T018 |
| AC-009 | T004, T005, T011, T018 | AC-037 | T012, T018 |
| AC-010 | T004, T005, T011, T018 | AC-038 | T012, T014, T018 |
| AC-011 | T004, T005, T011, T018 | AC-039 | T013, T018 |
| AC-012 | T004, T005, T011, T018 | AC-040 | T010, T013, T018 |
| AC-013 | T004, T005, T011, T018 | AC-041 | T010, T011, T018 |
| AC-014 | T003, T005, T009, T011, T018 | AC-042 | T010, T012, T018 |
| AC-015 | T004, T005, T009, T011, T018 | AC-043 | T010, T011, T018 |
| AC-016 | T004, T005, T009, T011, T018 | AC-044 | T006, T007, T008, T018 |
| AC-017 | T004, T005, T009, T011, T018 | AC-045 | T006, T009, T011, T018 |
| AC-018 | T003, T005, T009, T011, T018 | AC-046 | T006, T008, T012, T018 |
| AC-019 | T003, T005, T009, T011, T018 | AC-047 | T006, T008, T014, T015, T018 |
| AC-020 | T003, T005, T009, T011, T018 | AC-048 | T011, T014, T016, T018 |
| AC-021 | T003, T005, T009, T011, T018 | AC-049 | T014, T018 |
| AC-022 | T003, T005, T009, T011, T018 | AC-050 | T010, T014, T018 |
| AC-023 | T003, T005, T009, T011, T018 | AC-051 | T001, T010, T014, T018 |
| AC-024 | T003, T005, T009, T011, T018 | AC-052 | T001, T002, T016, T018 |
| AC-025 | T003, T005, T009, T011, T018 | AC-053 | T003, T005, T009, T011, T013, T017, T018 |
| AC-026 | T003, T005, T009, T011, T018 | AC-054 | T004, T005, T011, T018 |
| AC-027 | T003, T005, T009, T011, T018 | AC-055 | T002, T005, T008, T009, T011, T016, T018 |
| AC-028 | T003, T005, T006, T009, T011, T018 | AC-056 | T001, T015, T016, T017, T018 |
| AC-057 | T003, T005, T006, T007, T009, T011, T017, T018 | — | — |

## 20. Major-plan-phase traceability

Every major plan.md Section 16 phase maps directly and in order to Sections 1–17 of this checklist:

    Foundations T001
      → Scaffold T002
      → Pure model T003–T004
      → Unit tests T005
      → EF model T006
      → Migration T007
      → SQL integration infrastructure/schema T008
      → Transactional event processing/proof T009
      → Authentication T010
      → Webhook T011
      → Reads T012
      → Administration T013
      → Hardening T014
      → Docker T015
      → CI T016
      → README T017
      → Final review T018

## 21. Traceability conclusion

- All FR-001 through FR-030 requirements map to at least one acceptance criterion and multiple implementation/verification tasks.
- All NFR-001 through NFR-008 and SEC-001 through SEC-004 requirements map to acceptance evidence and tasks.
- All AC-001 through AC-057 map to one or more concrete test/verification tasks.
- All 17 implementation phases in plan.md map to an ordered checklist section.
- No requirement or acceptance criterion is orphaned at specification time.

## 22. Completion evidence

- **Targeted tests:** Not run; Phase 2.2 contains no test code.
- **Build/regression:** Not run; Phase 2.2 contains no solution or production code.
- **Contract evidence:** spec.md requirements, transition tables, and AC-001–AC-057.
- **Traceability evidence:** Sections 18–21 of this file.
- **Remaining risks:** The external contract questions in spec.md Section 20, especially timestamp-only delete ordering without a CMS version/sequence/incarnation identifier and CMS identity semantics. Active-entity watermark rollback is not an unresolved risk because EntityEventHighWatermarkUtc is monotonic by contract.
