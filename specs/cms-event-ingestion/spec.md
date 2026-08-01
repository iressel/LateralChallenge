# Feature Specification: CMS Event Ingestion and Entity Visibility

**Status:** Draft for implementation\
**Phase:** 2.2 — Task-order and execution-boundary correction only\
**Target:** Stable .NET 10, ASP.NET Core, Entity Framework Core 10, Microsoft SQL Server

## 1. Purpose

This specification defines a secure API that receives CMS publication events, persists the latest entity state and immutable version history, applies local administrative visibility controls, and exposes the resulting entities to authenticated consumers.

The central correctness goal is deterministic processing under duplicate delivery, out-of-order delivery, concurrent delivery, partial batch failure, deletion, and recreation. A response must describe each submitted item without allowing one deterministic item failure to roll back successful items.

## 2. Normative language and decision classification

The words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are normative.

Each requirement is classified as one of:

- **Challenge behavior:** externally observable behavior required by the challenge or the settled Phase 2 corrections.
- **Chosen implementation decision:** a binding design choice for this implementation where the external contract is silent.
- **Future option:** explicitly non-binding and out of scope for the initial implementation.

Challenge implementation, including T001 and later tasks, MAY proceed under the deterministic assumptions in Section 18. An unresolved external-contract question does not block challenge implementation when this specification already defines an applicable assumption. Implementation MUST pause only if newly discovered information invalidates an adopted API contract, ordering rule, identity rule, security rule, or persistence invariant. The unresolved questions in Section 20 MUST remain visible and prevent any claim of production-integration readiness until they are confirmed.

## 3. Verified current state

The LATERALCHALLENGE workspace contains no solution, projects, source code, tests, configuration, Docker files, Git metadata, or prior repository specification convention. This document, plan.md, and tasks.md are the first repository artifacts.

## 4. Scope

### 4.1 In scope

- A CMS-only batch webhook for publish, unpublish, and delete events.
- Deterministic validation, ordering, state transitions, idempotency, and conflict detection.
- SQL Server persistence for active state, immutable revisions, deletion tombstones, and processing logs.
- Consumer read endpoints with role-sensitive visibility.
- An administrator-only local disable operation that never changes CMS data.
- Isolated Basic Authentication schemes for the CMS and consumers.
- Operational health, structured observability, safe retry behavior, and relational integration testing.
- A later Docker Compose path for supported x86-64 development hosts and remote SQL Server guidance for Apple Silicon.

### 4.2 Out of scope

- Editing CMS-owned entity identifiers, versions, timestamps, payloads, or publication state through the consumer API.
- Propagating the local administrative disable flag to the CMS.
- MediatR, AutoMapper, generic repositories, microservices, an in-memory background queue, or asynchronous acknowledgement.
- Event reordering within a batch, cross-event batch transactions, or rollback of earlier successful items.
- SQL Server LocalDB, non-SQL Server production providers, or EF Core InMemory as proof of relational correctness.
- A UI, CMS implementation, message broker, webhook registration workflow, or consumer credential-management UI.
- Retaining payload-bearing revisions after a CMS delete.
- A separate ingestion-batch table, processed-event table, or ingestion-attempt table in the initial design.
- Production-grade credential rotation, a secrets-vault integration, a read replica, or a CMS-provided incarnation protocol. These are future options.

## 5. Terminology

| Term | Definition |
|---|---|
| Entity | A CMS-owned resource whose webhook wire identifier is `id` and whose internal domain/application name is `EntityId`. Identifiers are compared exactly and case-sensitively. |
| Versioned event | A publish or unpublish event containing a positive `version` and a `payload`. |
| Delete event | An unversioned event containing an `id` and CMS timestamp but no `version` or `payload`. |
| CMS timestamp | The event occurrence time supplied by the CMS, parsed with an explicit UTC offset and normalized to UTC. |
| Latest version | The greatest accepted Version in the current generation. |
| CurrentVersionOccurredAtUtc | The timestamp belonging to the latest accepted Version. It may move backward when a higher Version has an older timestamp and is used only for same-version publish/unpublish ordering. |
| EntityEventHighWatermarkUtc | The greatest accepted publish/unpublish timestamp in the active generation. It is monotonic, never moves backward, and is the active-entity timestamp used for delete ordering. |
| CMS publication status | Published or Unpublished, controlled exclusively by CMS events. |
| Administrative disable | A local boolean override controlled exclusively by an administrator. |
| Visible | Eligible to be returned to a normal consumer: published and not administratively disabled. |
| Generation | A local monotonically increasing incarnation number. The first active generation is 1; recreation after deletion increments it. |
| Tombstone | Payload-free metadata recording a deletion watermark and the last deleted generation. |
| Exact duplicate | An event whose durable idempotency identity and normalized content match an already completed non-invalid/non-conflict processing record. |
| Equivalent | A different event identity that requests the already represented state and requires no state change. |
| Stale | An event ordered before the current version/timestamp or at/before the applicable tombstone watermark. |
| Conflict | A deterministic incompatibility, such as immutable payload disagreement or eventId reuse with different content. |
| Valid batch | Syntactically valid JSON whose top-level value is a raw array containing 1 through 50 items. Individual events may still be invalid. |

## 6. Actors and trust boundaries

| Actor | Identity and permissions |
|---|---|
| CMS service | Authenticates only with the CMS Basic scheme and may invoke only the CMS event webhook. |
| Normal consumer | Authenticates only with the consumer Basic scheme and may read only visible active entities. |
| Administrator | Authenticates with the consumer Basic scheme, may read every active entity, and may change only the local administrative disable flag. |
| Operator | Supplies secrets and connection strings out of source control and provisions a read-only SQL Server login in production. |

The webhook, consumer API, database connections, logs, and deployment configuration are separate trust boundaries. Authentication succeeds only within the endpoint's configured scheme.

## 7. Functional requirements

### 7.1 Batch HTTP contract

- **FR-001 — Challenge behavior:** POST /cms/events MUST accept a raw JSON array containing 1 through 50 items, with no wrapper object. Each array position defines a stable zero-based sequence number for the response and processing log. A top-level object, null, string, number, empty array, or array with more than 50 items is an invalid envelope.
- **FR-002 — Challenge behavior:** Malformed JSON or an invalid top-level envelope MUST return 400; a request exceeding 16 MiB MUST return 413; and an authenticated request with a non-JSON media type MUST return 415. No event in such a request may be processed or logged as a completed event.
- **FR-003 — Challenge behavior:** A valid batch whose processing completes durably MUST return 200, even when individual outcomes include applied, duplicate, equivalent, stale, invalid, or conflict. The body MUST contain a generated BatchId, ordered per-item results, and summary counts for every outcome.
- **FR-004 — Challenge behavior:** Items MUST be processed sequentially in request order using asynchronous I/O. Each item MUST have its own durable SQL Server transaction and processing log. A later item failure MUST NOT roll back a committed earlier item.
- **FR-005 — Challenge behavior:** If durable processing cannot complete, the API MUST use 503 for a recognized transient dependency failure or 500 for an unexpected server failure. Clients SHOULD retry the entire request; they MUST NOT retry deterministic invalid or conflict results without correcting the source event.

### 7.2 Event envelope and validation

- **FR-006 — Challenge behavior:** Each event item MUST be a JSON object. Required known properties MUST be validated strictly for presence, JSON type, format, range, and event-type applicability. Unsupported event types MUST produce an individual invalid result rather than invalidate the batch.
- **FR-007 — Challenge behavior:** Unknown event-envelope properties MUST be ignored. Arbitrary properties inside `payload` MUST be preserved. Legitimate payload content MUST NOT be sanitized, redacted, renamed, or otherwise semantically changed.
- **FR-008 — Challenge behavior:** Webhook property names are case-sensitive. The external entity property MUST be `id`; the service maps it internally to `EntityId` and MUST NOT expose `entityId` as the webhook request property. The string `type` is trimmed and matched case-insensitively to `publish`, `unpublish`, or `delete`, then represented internally by that lowercase canonical value. Unsupported normalized values produce an individual invalid result. Every event requires `id` and `timestamp`; publish and unpublish require `version` and a JSON-object `payload`; delete MUST NOT contain `version` or `payload`.
- **FR-009 — Challenge behavior:** A Version MUST be a positive 64-bit integer. The first observed version need not be 1, and missing intermediate versions MUST be accepted.

### 7.3 Idempotency

- **FR-010 — Challenge behavior:** EventId is optional. When present, a non-empty EventId is the preferred durable idempotency identity. Reuse of the same external EventId with different normalized event content MUST be a conflict.
- **FR-011 — Challenge behavior:** When `eventId` is absent and the required identity fields are valid, the system MUST derive a deterministic SHA-256 event key from canonical normalized event type, exact internal `EntityId` mapped from wire `id`, Version or an unversioned sentinel, UTC Timestamp, and canonical Payload or a no-payload sentinel.
- **FR-012 — Challenge behavior:** A completed event identity and its original outcome MUST be durable. Exact redelivery MUST NOT repeat business-state mutation. A replay of an original invalid/conflict result MUST preserve that invalid/conflict outcome and code; other exact replays use duplicate. Processing that fails before its event transaction commits MUST leave no completed identity and MAY be retried.

### 7.4 State transitions

- **FR-013 — Chosen implementation decision:** With no active entity and no tombstone, a valid publish or unpublish MUST create generation 1 using its Version, Payload, status, and Timestamp, setting both CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc to the incoming timestamp. An unpublish may therefore create an active but non-visible entity.
- **FR-014 — Challenge behavior:** For an active entity, a lower incoming Version MUST be stale. A higher Version MUST become the latest version even if versions are skipped or its timestamp is older than the prior version timestamp. On acceptance, CurrentVersionOccurredAtUtc MUST become the incoming timestamp and EntityEventHighWatermarkUtc MUST become the maximum of its previous value and the incoming timestamp.
- **FR-015 — Challenge behavior:** Payload content is immutable for each EntityId, Generation, and Version. The same Version with different canonical payload content MUST be a conflict and MUST NOT overwrite the stored payload.
- **FR-016 — Challenge behavior:** For the same Version and identical payload, Timestamp MUST be compared with CurrentVersionOccurredAtUtc: an earlier Timestamp is stale; at the same Timestamp, a different publication status conflicts unless the event is an exact duplicate and the same status is duplicate or equivalent; a later Timestamp MAY transition publish/unpublish status, MUST set CurrentVersionOccurredAtUtc to the incoming timestamp, and MUST set EntityEventHighWatermarkUtc to the maximum of its previous value and the incoming timestamp.
- **FR-017 — Challenge behavior:** An unpublish at Version X+1 MUST store X+1 and its payload as the latest immutable revision, mark the current entity unpublished, and make it visible to administrators but not normal consumers.
- **FR-018 — Challenge behavior:** Delete ordering for an active entity MUST compare its Timestamp only with EntityEventHighWatermarkUtc, never with CurrentVersionOccurredAtUtc. A delete earlier than EntityEventHighWatermarkUtc is stale; an equal delete conflicts unless an exact replay was already resolved by idempotency; and a later delete is applied.
- **FR-019 — Challenge behavior:** Applying delete MUST hard-delete the active CmsEntities row and every payload-bearing CmsEntityRevisions row for that entity, including all generations, and MUST retain or advance one payload-free CmsDeletionTombstones row.
- **FR-020 — Challenge behavior:** A versioned event at or before the tombstone Timestamp MUST be stale. A valid publish or unpublish after the tombstone Timestamp MUST begin the next local Generation, may use any positive Version, and MUST start with AdministrativeDisabled equal to false and both CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc equal to the incoming timestamp.
- **FR-021 — Challenge behavior:** CMS publish and unpublish events MUST NOT clear AdministrativeDisabled. CMS delete removes that local override with the entity; recreation starts with the override false.

### 7.5 Read and administrative APIs

- **FR-022 — Challenge behavior:** A normal consumer MUST see only active entities that are both published and not administratively disabled.
- **FR-023 — Challenge behavior:** An administrator MUST be able to see all active entities, including unpublished and administratively disabled entities. Deleted entities MUST never appear.
- **FR-024 — Chosen implementation decision:** GET /api/entities MUST provide deterministic cursor pagination ordered by case-sensitive EntityId, with a default page size of 20 and maximum of 100. GET /api/entities/{entityId} MUST return the role-visible entity or 404 without disclosing hidden existence to normal consumers.
- **FR-025 — Challenge behavior:** PUT /api/entities/{entityId}/administrative-state with a required boolean Disabled value MUST be idempotent and administrator-only. It MUST change only AdministrativeDisabled, MUST NOT alter CMS fields or propagate to the CMS, and MUST return 404 for a deleted or unknown entity.

### 7.6 Authentication and authorization

- **FR-026 — Challenge behavior:** The application MUST define isolated CMS and consumer Basic Authentication schemes plus separate CmsEvents, ConsumerAccess, and AdministratorAccess policies. The consumer scheme MUST distinguish normal-consumer and administrator roles.
- **FR-027 — Challenge behavior:** Missing, malformed, or invalid credentials for the endpoint's scheme MUST return 401 with that scheme's Basic challenge. A valid normal consumer attempting an administrator operation MUST receive 403. A credential valid only in the other scheme MUST fail with 401.

### 7.7 Persistence, read boundary, and operations

- **FR-028 — Challenge behavior:** Persistence MUST use CmsEntities, CmsEntityRevisions, CmsDeletionTombstones, and CmsEventProcessingLogs as specified in Section 13. A separate CmsIngestionBatches table is not required because BatchId and Sequence in the event log plus the synchronous API response satisfy the identified behavior.
- **FR-029 — Challenge behavior:** CmsWriteDbContext MUST own all writes and migrations. CmsReadDbContext MUST expose only no-tracking projections through a read application abstraction, throw on SaveChanges as a development safeguard, and use the ReadDatabase connection string.
- **FR-030 — Challenge behavior:** The service MUST emit structured, payload-free processing logs and outcome metrics, propagate or create correlation identifiers, expose /health/live, and expose /health/ready with SQL Server connectivity checks.

## 8. Non-functional requirements

- **NFR-001 — Settled technology:** The later solution MUST target stable .NET 10 and EF Core 10, use ASP.NET Core and Microsoft.EntityFrameworkCore.SqlServer, and follow a lightweight Domain → Application → Infrastructure/API dependency model. It MUST use a traditional .sln file.
- **NFR-002 — Correctness:** The pure event decision model MUST be deterministic and independent of ASP.NET Core and EF Core. Cross-instance processing MUST serialize competing updates to the same entity and enforce unique idempotency identities in SQL Server.
- **NFR-003 — Capacity:** The request limit is 16 MiB, the batch limit is 50 events, and each raw Payload is limited to 256 KiB in UTF-8. Limits are chosen implementation safeguards and MUST be configurable no higher than an operator-approved ceiling.
- **NFR-004 — Async and cancellation:** Database and HTTP work MUST use asynchronous APIs and accept CancellationToken. Cancellation MUST NOT undo previously committed item transactions.
- **NFR-005 — Verification:** Unit tests MUST exhaustively cover the state-transition tables. Relational integration tests MUST exercise SQL Server, migrations, constraints, authentication middleware, authorization, serialization, and production dependency-injection wiring. EF Core InMemory MUST NOT substitute for these tests.
- **NFR-006 — Containers and platforms:** Local container setup MUST use an official SQL Server Linux image with an explicitly verified and pinned tag or digest, never a mutable latest tag in final CI. Docker Compose is for compatible x86-64 hosts; Apple Silicon documentation MUST use remote SQL Server or Azure SQL rather than claiming emulation support. SQL Server Testcontainers MAY run only where supported.
- **NFR-007 — Data representation:** EntityId and EventId comparisons MUST be case-sensitive and ordinal through an explicit SQL Server collation. Versions and generations MUST use bigint-compatible values; event and tombstone timestamps, CurrentVersionOccurredAtUtc, and EntityEventHighWatermarkUtc MUST be UTC datetime2(7); the two active-entity timestamps MUST be separate required columns; payload columns MUST be nvarchar(max) guarded as JSON objects; hashes MUST use 32-byte binary values; concurrency MUST use rowversion where specified.
- **NFR-008 — API safety:** Entity responses and authentication failures MUST prevent shared caching with Cache-Control: no-store. Error bodies MUST use Problem Details and MUST not expose stack traces or database details.

## 9. Security requirements

- **SEC-001:** The CMS service identity, normal consumer, and administrator MUST have distinct usernames and random GUID-format passwords supplied through environment variables or .NET user-secrets. No real credential may be committed. The CMS username MUST contain 10 through 20 characters.
- **SEC-002:** Credential comparison MUST use a fixed-time comparison over a consistent byte representation. Decoded credentials MUST have bounded length and be discarded after authentication. HTTPS MUST be required outside local development.
- **SEC-003:** Passwords, decoded Basic credentials, Authorization headers, raw payloads, and unsanitized exception details MUST never be written to application logs or CmsEventProcessingLogs.
- **SEC-004:** Production MUST use separate WriteDatabase and ReadDatabase connection strings and a truly read-only SQL Server login for the read connection. The write login MUST have only application and migration permissions appropriate to its deployment workflow. Local development MAY use the same instance and login for both.

## 10. API behavioral contract

### 10.1 CMS webhook

**Endpoint:** POST /cms/events\
**Authentication policy:** CmsEvents\
**Accepted media types:** application/json and application/*+json\
**Request limit:** 16 MiB\
**Batch size:** 1–50 events

Conceptual request:

    [
      {
        "eventId": "optional-external-id",
        "type": "Publish",
        "id": "entity-123",
        "version": 7,
        "timestamp": "2026-07-31T12:34:56.1234567Z",
        "payload": { "arbitrary": "content" }
      },
      {
        "type": "delete",
        "id": "entity-456",
        "timestamp": "2026-07-31T12:35:00Z"
      },
      {
        "type": "unPublish",
        "id": "entity-789",
        "version": 4,
        "timestamp": "2026-07-31T12:36:00Z",
        "payload": { "arbitrary": "content" }
      }
    ]

The top-level value is the raw array itself; there is no object wrapper and no `events` property. A top-level object, null, string, number, empty array, or array containing more than 50 items is an invalid envelope and returns 400 without item processing. Malformed JSON also returns 400.

Property names are case-sensitive. The webhook wire property for the entity identifier is exactly `id`; `entityId` is not an alias. Validated `id` is mapped to the internal C# and domain/application name `EntityId`. Wire `id` and optional `eventId` MUST be JSON strings of 1–200 characters, MUST NOT contain control characters, and MUST NOT have leading or trailing whitespace. `timestamp` MUST be an ISO 8601 string with Z or an explicit offset and no more than seven fractional-second digits; it is normalized to UTC.

The `type` value may have leading/trailing whitespace, which is trimmed. It is then matched case-insensitively and normalized internally to exactly `publish`, `unpublish`, or `delete`. This includes `publish`, `Publish`, `PUBLISH`, `unpublish`, `unPublish`, `UnPublish`, `UNPUBLISH`, `delete`, `Delete`, and `DELETE`. Property-name casing is unaffected by value normalization.

Duplicate property names anywhere inside one event, including its `payload`, make that item invalid while the valid raw-array batch still returns 200. The array envelope has no properties to duplicate.

This rule is a chosen implementation decision contingent on a clear streaming validation implementation. Unknown properties remain allowed.

For a valid batch, the conceptual response contains:

    {
      "batchId": "server-generated-guid",
      "results": [
        {
          "sequence": 0,
          "eventId": "optional-external-id",
          "id": "entity-123",
          "outcome": "applied",
          "code": "VERSION_ADVANCED"
        }
      ],
      "summary": {
        "total": 1,
        "applied": 1,
        "duplicate": 0,
        "equivalent": 0,
        "stale": 0,
        "invalid": 0,
        "conflict": 0
      }
    }

Results MUST preserve request order. Response results use `id` for consistency with the submitted event. Code is a stable machine-readable token; Message MAY contain a safe human-readable explanation. Invalid items MAY omit `id` or `eventId` when those fields could not be validated. No result contains payload data or secrets.

Outcome semantics:

| Outcome | Meaning | Client action |
|---|---|---|
| applied | Durable business state or watermark changed. | None. |
| duplicate | The exact durable event identity and normalized content previously completed with a non-invalid/non-conflict outcome. | Treat as acknowledged; no mutation is repeated. |
| equivalent | A different identity requested the already represented state; no change was needed. | Treat as success. |
| stale | Ordering rules place the event before retained state. | Do not retry unchanged. |
| invalid | The individual event violates its structural or value contract. | Correct the source event. |
| conflict | The event contradicts immutable state or reuses EventId with different content. | Investigate and correct the source event. |

HTTP status precedence is:

1. Request-size enforcement.
2. Endpoint authentication and authorization.
3. Media-type and JSON/envelope validation.
4. Per-item processing.

Accordingly, an unauthenticated request does not gain validation details. Once authenticated, a non-JSON body returns 415 and malformed JSON or an invalid envelope returns 400.

When an event transaction cannot commit, processing stops and the endpoint returns 500 or 503 Problem Details rather than a misleading partial 200 response. Earlier commits remain durable. Retrying the whole request is safe: completed items resolve through idempotency or current-state equivalence, while the interrupted item is attempted again.

### 10.2 Consumer reads

**Endpoints:**

- GET /api/entities?pageSize={1..100}&afterEntityId={cursor}
- GET /api/entities/{entityId}

**Authentication policy:** ConsumerAccess

The list response MUST contain items in ascending ordinal EntityId order, the requested page size, and a next cursor only when another page may exist. Cursor comparison MUST use the same case-sensitive ordering as SQL Server. Offset pagination is not used.

Normal consumers receive only visible entities. Administrators receive all active entities and each response reports CmsPublicationStatus and AdministrativeDisabled. A normal consumer receives 404 for an entity hidden by either status, exactly as for an unknown/deleted identifier.

### 10.3 Administrative state

**Endpoint:** PUT /api/entities/{entityId}/administrative-state\
**Authentication policy:** AdministratorAccess\
**Body:** a JSON object with required boolean property Disabled

Unknown body properties are ignored for forward compatibility. Missing, null, or non-boolean Disabled is 400. Repeating the current boolean value is a successful idempotent operation. The response reports the entity identifier and current local administrative state, never mutates CMS-owned fields, and is marked no-store.

## 11. Event validation and canonical identity

Validation occurs in this order:

1. Parse the JSON document while detecting duplicate property names.
2. Validate that the top-level value is a raw array of 1 through 50 items.
3. For each item in sequence, validate object shape, case-sensitive property names, and known property applicability.
4. Map wire `id` to internal EntityId; trim and case-insensitively normalize `type`; normalize Timestamp to UTC; and validate Version and size bounds.
5. Build a canonical payload representation for equality and hashing without replacing the stored first-observed payload.
6. Build normalized event content and an idempotency key when possible.

Canonical payload equality is defined as follows:

- Object member order and insignificant JSON whitespace do not affect equality.
- Object property names and decoded string values use ordinal comparison.
- Array order is significant.
- JSON value types are significant.
- String escape spelling is not significant after decoding.
- A valid number's original numeric token, excluding surrounding whitespace, is significant; for example, 1 and 1.0 are different content.
- Duplicate object property names are invalid rather than canonicalized.

The canonicalizer sorts object member names ordinally and emits an unambiguous length-prefixed UTF-8 representation for hashing. It does not remove, rename, or rewrite data in the persisted payload. Golden unit tests MUST freeze this contract.

Normalized event content excludes unknown event-envelope properties and EventId itself. It contains the canonical lowercase event type and exact internal EntityId mapped from wire `id`. The external idempotency key is namespaced separately from the derived key. The content hash includes every normalized known field. This permits the same EventId to be compared for incompatible reuse and ensures accepted casing/outer-whitespace variations of `type` resolve consistently.

Content-derived idempotency has explicit limitations:

- Two semantically related deliveries with different timestamps are different identities.
- Numeric spellings such as 1 and 1.0 are different payload content.
- An event first delivered with EventId and later without it uses different identities, although state rules should normally make the latter equivalent, stale, or conflict.
- An invalid event without enough valid known fields cannot receive the normal derived identity. Repeating it is business-state idempotent because invalid items never mutate entity state, but it may produce another invalid processing-log row.
- Timestamp-only deletes cannot distinguish a late event from a reused entity incarnation as reliably as a CMS sequence or incarnation identifier.

## 12. State-transition rules

### 12.1 Rule precedence

The processor MUST apply rules in this order:

1. Individual validation.
2. Existing idempotency identity comparison.
3. Tombstone watermark gate for versioned events.
4. Current entity/version/payload/CurrentVersionOccurredAtUtc transition while preserving the monotonic EntityEventHighWatermarkUtc invariant.
5. Atomic persistence of state changes and processing log.

An exact replay never reapplies state. If the identity owner recorded invalid or conflict, the replay preserves that original outcome and code; otherwise it returns duplicate. Reusing EventId with a different normalized content hash returns conflict even if the proposed state would otherwise be valid.

### 12.2 Publish and unpublish with an active entity

The following table applies after the event is later than any retained tombstone:

| Incoming relationship | Payload relationship | Timestamp relationship to CurrentVersionOccurredAtUtc | Status relationship | Outcome and action |
|---|---|---|---|---|
| Version lower | Any | Any | Any | stale; no state change |
| Version higher | Any valid payload | Any | Any | applied; insert immutable revision, replace current latest version/payload/status, set CurrentVersionOccurredAtUtc to incoming Timestamp, set EntityEventHighWatermarkUtc to max(previous high watermark, incoming Timestamp), preserve AdministrativeDisabled |
| Version same | Different canonical payload | Any | Any | conflict; no state change |
| Version same | Identical | Earlier | Any | stale; no state change |
| Version same | Identical | Equal | Same | duplicate if identity matches; otherwise equivalent |
| Version same | Identical | Equal | Different | conflict unless already resolved as exact duplicate |
| Version same | Identical | Later | Same | applied; advance CurrentVersionOccurredAtUtc and set EntityEventHighWatermarkUtc to max(previous high watermark, incoming Timestamp) |
| Version same | Identical | Later | Different | applied; change CMS publication status, advance CurrentVersionOccurredAtUtc, and set EntityEventHighWatermarkUtc to max(previous high watermark, incoming Timestamp) |

Version ordering is primary. Consequently, a higher version is accepted even if its CMS Timestamp is earlier than the prior version's timestamp. Its timestamp becomes CurrentVersionOccurredAtUtc for that new current version, while EntityEventHighWatermarkUtc remains the maximum timestamp accepted in the active generation and never rolls back.

### 12.3 Delete

| Existing state | Delete Timestamp relationship | Outcome and action |
|---|---|---|
| Active entity | Earlier than EntityEventHighWatermarkUtc | stale; retain entity and revisions |
| Active entity | Equal to EntityEventHighWatermarkUtc | conflict because delete and versioned active state are incompatible, unless exact replay was already resolved by idempotency; retain entity and revisions |
| Active entity | Later than EntityEventHighWatermarkUtc | applied; hard-delete entity and all revisions, upsert tombstone for the deleted generation using the delete Timestamp |
| No active entity, tombstone exists | Earlier than tombstone | stale |
| No active entity, tombstone exists | Equal to tombstone | duplicate if identity matches; otherwise equivalent delete |
| No active entity, tombstone exists | Later than tombstone | applied; advance deletion watermark without changing last deleted generation |
| No active entity, no tombstone | Any valid timestamp | applied; create a generation-0 tombstone watermark |

Delete never contains Version or Payload. Tombstones contain no payload.

### 12.4 Recreation after delete

| Incoming event | Timestamp relationship to tombstone | Outcome and action |
|---|---|---|
| Publish or unpublish | Earlier or equal | stale |
| Publish | Later | applied; create last-deleted-generation + 1 with any positive Version, published, AdministrativeDisabled false, and both active timestamps equal to incoming Timestamp |
| Unpublish | Later | applied; create last-deleted-generation + 1 with any positive Version, unpublished, AdministrativeDisabled false, and both active timestamps equal to incoming Timestamp |
| Delete | Earlier/equal/later | Apply the delete table; no active generation is created |

The tombstone remains as historical metadata after recreation. Its watermark applies to events from before that recreation boundary; every accepted event in the new generation must remain later than the retained deletion timestamp.

### 12.5 Administrative state and visibility

| Active state | Normal consumer | Administrator |
|---|---|---|
| Published, AdministrativeDisabled false | Visible | Visible |
| Published, AdministrativeDisabled true | Hidden | Visible |
| Unpublished, AdministrativeDisabled false | Hidden | Visible |
| Unpublished, AdministrativeDisabled true | Hidden | Visible |
| Deleted | Not found | Not found |

Publish and unpublish preserve the local flag. Delete removes it. Recreation initializes it to false.

## 13. Persistence requirements

### 13.1 CmsEntities

Current active state only:

- EntityId, case-sensitive primary key, maximum 200 characters.
- Generation, positive bigint.
- LatestVersion, positive bigint.
- Payload, nvarchar(max), required JSON object.
- PayloadHash, binary(32), representing canonical content.
- CmsPublicationStatus.
- CurrentVersionOccurredAtUtc, required datetime2(7), the timestamp of LatestVersion and the comparison point for same-version publication transitions.
- EntityEventHighWatermarkUtc, required datetime2(7), the monotonic maximum accepted publish/unpublish timestamp in the active generation and the comparison point for delete.
- AdministrativeDisabled, bit.
- AdministrativeStateChangedAtUtc and AdministrativeStateChangedBy, nullable metadata.
- CreatedAtUtc and UpdatedAtUtc.
- RowVersion, SQL Server rowversion concurrency token.

### 13.2 CmsEntityRevisions

Immutable payload-bearing history for active entities:

- EntityId, Generation, and Version as a unique composite key.
- FirstObservedPayload and PayloadHash.
- FirstObservedAtUtc.
- No update path for payload fields after insertion.
- All rows for EntityId are hard-deleted when delete is applied.

Publication transitions of the same version belong to current state and the processing log; they MUST NOT overwrite the revision payload.

### 13.3 CmsDeletionTombstones

Payload-free stale-event barrier:

- EntityId primary key.
- LastDeletedGeneration, where 0 represents delete observed before any active generation.
- DeletedAtUtc watermark.
- LastDeleteEventKey or safe hash metadata when available.
- CreatedAtUtc and UpdatedAtUtc.
- RowVersion concurrency token.
- No Payload or payload-bearing diagnostic field.

### 13.4 CmsEventProcessingLogs

One durable record for every individually completed processing attempt:

- Internal processing-log identifier.
- BatchId and zero-based Sequence, with a unique pair.
- Namespaced IdempotencyKey when derivable.
- OwnsIdempotencyKey, indicating the first durable result for that key, with a filtered unique constraint allowing only one owner per IdempotencyKey.
- ReplayOfProcessingLogId, nullable self-reference for exact replays and EventId-reuse conflicts.
- ExternalEventId when supplied and valid.
- EventContentHash and PayloadHash when derivable.
- EventType in canonical lowercase form, EntityId mapped from wire `id`, Version when applicable, and EventOccurredAtUtc when valid.
- Processing Outcome and stable validation/conflict Code.
- Generation and resulting Version when applicable.
- ProcessedAtUtc, correlation identifier, and authenticated CMS subject identifier.
- No raw payload, password, decoded credential, Authorization header, or unsanitized exception detail.

Invalid items without a derivable idempotency identity still receive a unique internal processing-log row. Every replay also receives a payload-free row for its current BatchId and Sequence, refers to the identity owner, and does not claim the key. This single table therefore represents both the durable identity owner and subsequent attempts without adding separate processed-event or ingestion-attempt tables.

### 13.5 No CmsIngestionBatches table

There is no requirement to retrieve batches, resume a batch as a unit, or atomically persist a batch summary. BatchId and Sequence on CmsEventProcessingLogs represent the relationship required for audit and correlation, and the synchronous response supplies summary counts. Therefore an additional CmsIngestionBatches table would duplicate data without establishing a new correctness invariant and is excluded from the initial schema.

## 14. Read and write contexts

- CmsWriteDbContext owns CmsEntities, CmsEntityRevisions, CmsDeletionTombstones, CmsEventProcessingLogs, all mappings, and all migrations.
- CmsReadDbContext maps only the active-state read model needed by consumer queries.
- Every read query uses no-tracking projections; entity graphs and payload revision collections are not loaded.
- The application read abstraction exposes query methods/DTOs, not CmsReadDbContext or mutable EF entities.
- CmsReadDbContext SaveChanges and SaveChangesAsync throw NotSupportedException as a development safeguard.
- ConnectionStrings:WriteDatabase and ConnectionStrings:ReadDatabase are mandatory configuration keys.
- Local development may point both keys to the same SQL Server/login.
- Production enforces read-only behavior with a SQL login granted SELECT only to the required objects. The throwing SaveChanges override is not the security boundary.
- Pagination orders and filters in SQL before projection and uses the same explicit case-sensitive collation as identity keys.

## 15. Transaction and concurrency invariants

Each event is processed inside one SQL Server transaction. The implementation MUST:

- Use a SQL Server isolation/locking strategy that serializes concurrent operations for the same EntityId across application instances.
- Check the durable idempotency identity and content hash inside the transaction.
- Read current entity including both active timestamps, tombstone, and same-version revision in that transaction.
- Apply exactly one pure decision result.
- Prevent EntityEventHighWatermarkUtc regression on every accepted publish/unpublish and compare delete only against that high watermark.
- Persist the state mutation and processing log atomically.
- Rely on the filtered unique identity-owner constraint and revision constraints, translate expected races into deterministic outcomes, and use bounded retries for deadlock/transient cases.
- Commit before the item is counted in a 200 response.

A recommended implementation is a transaction-owned SQL Server application lock scoped to a stable hash of EntityId, together with the unique IdempotencyKey index and rowversion tokens. The implementation phase may choose an equally strong SQL Server strategy, but tests must demonstrate concurrent correctness.

## 16. Observability and operational behavior

Application logs use structured fields such as BatchId, Sequence, EventType, a safe EntityId hash or bounded identifier, Outcome, Code, elapsed time, and correlation/trace identifiers. They never contain raw payloads or authentication material.

Required metrics include:

- Batch and event counts.
- Per-outcome counts.
- Event processing and batch latency.
- Validation and conflict code counts.
- SQL transient retry, deadlock, and failure counts.
- Authentication failure counts without attempted usernames or secrets as high-cardinality labels.

/health/live reports process liveness without database access. /health/ready checks required write and read SQL Server connectivity with short timeouts and no sensitive detail. Readiness failure returns a non-success status suitable for orchestration.

## 17. Acceptance criteria

- **AC-001 [FR-001]:** Given an authenticated raw JSON array with 1 or 50 event objects, when POST /cms/events is called, then the array is accepted directly without an object wrapper and each array position retains its zero-based processing/result sequence.
- **AC-002 [FR-001, FR-002]:** Given a top-level JSON object, null, string, number, empty array, or array with 51 items, when the webhook is called, then it returns 400 and no item processing log or entity mutation is committed.
- **AC-003 [FR-002]:** Given malformed JSON, when the authenticated webhook is called with JSON media type, then it returns 400 Problem Details and commits no event work.
- **AC-004 [FR-002]:** Given an authenticated request with a non-JSON media type, when the webhook is called, then it returns 415 without item processing.
- **AC-005 [FR-002, NFR-003]:** Given a body exceeding 16 MiB, when the webhook is called, then it returns 413 without parsing or processing events.
- **AC-006 [FR-003]:** Given a valid batch containing any mixture of the six deterministic outcomes, when all item transactions complete, then HTTP 200 contains ordered results and exact summary counts whose total equals the input count.
- **AC-007 [FR-004]:** Given an earlier applied item and a later deterministic invalid/conflict item, when the batch completes, then the earlier item remains committed and the valid batch returns 200 with both outcomes.
- **AC-008 [FR-004, FR-005, FR-012, NFR-004]:** Given an earlier committed item and a later transient database failure, when processing stops, then the endpoint returns 503, the earlier commit remains, and whole-request retry does not reapply it.
- **AC-009 [FR-006, FR-008]:** Given `publish`, `Publish`, `PUBLISH`, `unpublish`, `unPublish`, `UnPublish`, `UNPUBLISH`, `delete`, `Delete`, `DELETE`, or a supported token surrounded by whitespace, when its otherwise valid item is processed, then it resolves to the corresponding lowercase canonical type; given an unsupported value after trim/case-insensitive normalization, then only that item is logged invalid with a stable code and the batch returns 200.
- **AC-010 [FR-006, FR-008, FR-009]:** Given missing, null, wrongly typed, out-of-range, inapplicable, or malformed known fields—including absent wire `id` or use of `entityId` instead of `id`—when processed, then the item is invalid and does not mutate entity state.
- **AC-011 [FR-007]:** Given additional unknown event-envelope properties and a payload with arbitrary nested properties, when a valid event is applied and read, then unknown event-envelope properties are ignored and all payload properties remain semantically intact.
- **AC-012 [FR-006, FR-007]:** Given duplicate names within one event or payload, when parsed as an item in an otherwise valid raw-array batch, then that item is invalid and the completed batch returns 200.
- **AC-013 [FR-008]:** Given publish/unpublish with wire `id`, Version, and object Payload or delete with wire `id` and without Version/Payload, when other fields are valid, then field applicability passes; the inverse combinations are invalid, and successful response items use wire property `id`.
- **AC-014 [FR-009]:** Given no prior entity and Version 7, or current Version 7 followed by Version 10, when valid events arrive, then both are accepted without requiring versions 1, 8, or 9.
- **AC-015 [FR-010, FR-012]:** Given a completed event with EventId, when the same EventId and normalized content are redelivered, then the result is duplicate and no entity state is reapplied.
- **AC-016 [FR-010]:** Given a completed external EventId, when it is reused with any different normalized known content, then the result is conflict and no entity state changes.
- **AC-017 [FR-011, FR-012]:** Given a valid event without EventId, when an exact normalized redelivery occurs, then the same SHA-256 key is found, the result is duplicate, and no state is reapplied.
- **AC-018 [FR-013]:** Given no active entity or tombstone, when valid publish or unpublish with any positive Version arrives, then generation 1 and one immutable revision are created with the corresponding publication status and both CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc equal the incoming timestamp.
- **AC-019 [FR-014]:** Given an active Version X, when an otherwise valid lower Version arrives, then it is stale and state is unchanged.
- **AC-020 [FR-014, FR-017]:** Given active Version X, when valid unpublish X+1 arrives with its payload, then X+1 becomes latest, its revision is stored, CurrentVersionOccurredAtUtc becomes the incoming timestamp, EntityEventHighWatermarkUtc becomes the maximum of its prior value and that timestamp, AdministrativeDisabled is preserved, and only administrators can see it.
- **AC-021 [FR-015]:** Given an observed EntityId/Generation/Version, when the same Version arrives with different canonical payload content, then it is conflict and the original revision/current payload remain unchanged.
- **AC-022 [FR-016]:** Given the same Version and payload with a timestamp earlier than CurrentVersionOccurredAtUtc, when processed, then it is stale.
- **AC-023 [FR-016]:** Given the same Version, payload, status, and timestamp under a different identity, when processed, then it is equivalent; under the same identity it is duplicate.
- **AC-024 [FR-016]:** Given the same Version and payload but different status at the same timestamp, when processed, then it is conflict unless already identified as an exact duplicate.
- **AC-025 [FR-016]:** Given the same Version and payload at a timestamp later than CurrentVersionOccurredAtUtc, when publish/unpublish status is same or different, then it is applied, CurrentVersionOccurredAtUtc advances to the incoming timestamp, EntityEventHighWatermarkUtc remains the maximum accepted active-generation timestamp, and only the CMS status may otherwise change.
- **AC-026 [FR-018]:** Given an active entity, when a delete earlier than EntityEventHighWatermarkUtc arrives, then it is stale and retains the entity and revisions regardless of CurrentVersionOccurredAtUtc.
- **AC-027 [FR-018]:** Given an active entity, when a delete exactly at EntityEventHighWatermarkUtc arrives under a new identity, then it conflicts and retains the entity and revisions; an exact replay is resolved first by idempotency.
- **AC-028 [FR-018, FR-019]:** Given an active entity, when a delete later than EntityEventHighWatermarkUtc arrives, then it hard-deletes current state and every revision and creates/advances a payload-free tombstone using the delete timestamp.
- **AC-029 [FR-018, FR-019]:** Given no active entity, when a first delete arrives, then a generation-0 tombstone is created; later deletes advance it, while an equal compatible delete is duplicate/equivalent and an earlier delete is stale.
- **AC-030 [FR-020]:** Given a tombstone, when publish or unpublish arrives at or before its Timestamp, then the event is stale regardless of Version.
- **AC-031 [FR-020]:** Given a tombstone for deleted generation G, when a later valid publish of any positive Version arrives, then generation G+1 is created as published with AdministrativeDisabled false and both active timestamps equal the incoming timestamp.
- **AC-032 [FR-020]:** Given a tombstone for deleted generation G, when a later valid unpublish of any positive Version arrives, then generation G+1 is created as unpublished with AdministrativeDisabled false and both active timestamps equal the incoming timestamp.
- **AC-033 [FR-021]:** Given AdministrativeDisabled true, when later publish or unpublish is applied in the same generation, then the local flag remains true.
- **AC-034 [FR-021]:** Given AdministrativeDisabled true, when a later delete and valid recreation occur, then the old override is removed and the recreated entity starts false.
- **AC-035 [FR-022]:** Given all four active combinations of CMS status and local flag, when a normal consumer lists/gets entities, then only published plus not-disabled entities are returned.
- **AC-036 [FR-023]:** Given unpublished or administratively disabled active entities, when an administrator lists/gets entities, then they are returned with both state indicators; deleted entities return 404.
- **AC-037 [FR-024, NFR-007]:** Given more entities than the page size including case-distinct identifiers, when pages are traversed using the returned cursor, then each active role-visible entity appears once in deterministic ordinal order.
- **AC-038 [FR-024, NFR-008]:** Given a hidden, deleted, or unknown entity, when a normal consumer requests it, then each produces indistinguishable 404 Problem Details with no-store headers.
- **AC-039 [FR-025]:** Given an administrator and active entity, when Disabled is set true, false, or repeated, then only the local flag changes and the idempotent response reports the resulting value.
- **AC-040 [FR-025, FR-027]:** Given a normal consumer, when the administrative endpoint is called, then it returns 403; given an unknown/deleted entity and an administrator, it returns 404.
- **AC-041 [FR-026, FR-027]:** Given valid CMS credentials, when the webhook is called, then authentication succeeds; missing, malformed, or incorrect CMS credentials return 401 with the CMS Basic challenge.
- **AC-042 [FR-026, FR-027]:** Given valid normal-consumer or administrator credentials, when consumer reads are called, then both authenticate and receive their role-specific visibility.
- **AC-043 [FR-026, FR-027]:** Given credentials valid only for the CMS scheme on a consumer endpoint, or consumer credentials on the webhook, when called, then the endpoint returns 401 rather than accepting the other scheme.
- **AC-044 [FR-028, NFR-007]:** Given migrations applied to SQL Server, when schema metadata is inspected, then the four required tables, keys, unique constraints, JSON checks, case-sensitive collation, UTC-compatible types, hashes, and rowversion columns match Section 13, and CmsEntities contains separate required CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc datetime2(7) columns.
- **AC-045 [FR-019, FR-028]:** Given a delete with entity revisions and prior logs, when committed, then entity/revisions are gone while the tombstone and payload-free processing logs remain.
- **AC-046 [FR-029]:** Given a consumer query, when executed, then SQL filtering/order/projection occur no-tracking and no write-context entity is exposed to the read application boundary.
- **AC-047 [FR-029, SEC-004]:** Given CmsReadDbContext SaveChanges in development, then it throws; given a production read login, an attempted SQL write is denied by SQL Server.
- **AC-048 [FR-030, SEC-003]:** Given applied, rejected, duplicate, and failed processing, when logs and metrics are inspected, then correlation, safe metadata, outcomes, codes, counts, and latency exist without raw payloads, credentials, headers, or unsanitized exceptions.
- **AC-049 [FR-030]:** Given a running process, when liveness is requested it does not require SQL; when readiness is requested with unavailable required SQL connectivity it reports unhealthy without sensitive details.
- **AC-050 [SEC-001, SEC-002]:** Given startup configuration, when credentials are absent, shared, malformed, the CMS username is outside 10–20 characters, or a password is not GUID format, then startup validation fails safely; valid credentials compare in fixed time.
- **AC-051 [SEC-002, SEC-003, NFR-008]:** Given non-development HTTP or any authentication failure, when the request is handled, then HTTPS is required, the response is no-store, and authentication material is absent from logs.
- **AC-052 [NFR-001]:** Given the later scaffold, when inspected and built, then it uses .NET/EF Core 10, SQL Server provider, a traditional .sln, and only Domain, Application, Infrastructure, API, unit-test, and integration-test projects with the specified dependency direction.
- **AC-053 [NFR-002]:** Given concurrent duplicate or competing events across independent scopes, including higher-version older-timestamp and delete races, when processed against SQL Server, then one deterministic serial order is observed, unique identities/revisions hold, EntityEventHighWatermarkUtc never regresses, delete uses the serialized high watermark, and no lost update or double mutation occurs.
- **AC-054 [NFR-003]:** Given a 256 KiB payload it is accepted; given a payload larger than 256 KiB it is individually invalid without storing payload content in the processing log.
- **AC-055 [NFR-005]:** Given the test suite, when run, then pure transition tests and SQL Server-backed integration tests exercise real mappings, migrations, constraints, authentication, authorization, raw-array serialization with wire `id`, case-insensitive event-type normalization, and dependency injection without EF Core InMemory.
- **AC-056 [NFR-006, SEC-001]:** Given final local/CI container configuration, when inspected, then the verified official SQL Server image is pinned, no latest/SA password is committed, x86-64 Compose and supported Testcontainers paths are documented, and Apple Silicon uses remote SQL Server/Azure SQL guidance.
- **AC-057 [FR-014, FR-016, FR-018, NFR-002, NFR-007]:** Given active Version 5 with CurrentVersionOccurredAtUtc and EntityEventHighWatermarkUtc both 10:00, when Version 6 is accepted at 09:00, then CurrentVersionOccurredAtUtc becomes 09:00 and EntityEventHighWatermarkUtc remains 10:00; a subsequent delete at 09:30 is stale and retains entity/revisions, a delete at 10:00 under a new identity conflicts and retains them, and a delete after 10:00 applies and removes them.

## 18. Explicit assumptions

The assumptions in this section are normative defaults for challenge implementation. T001 and every later challenge task MAY proceed using them without waiting for answers to Section 20. A question becomes an implementation gate only when newly discovered information invalidates an adopted API contract, ordering rule, identity rule, security rule, or persistence invariant. These defaults support challenge completion but do not establish production-integration readiness; that claim remains blocked until the applicable external questions are confirmed.

- **A-001:** The CMS sends English event-type values that, after optional surrounding whitespace is trimmed, match publish, unpublish, or delete case-insensitively; the service owns canonical lowercase normalization.
- **A-002:** Entity identifiers are case-sensitive, are never intentionally padded with whitespace, and fit within 200 characters.
- **A-003:** Payload is always a JSON object and 256 KiB is sufficient for the challenge.
- **A-004:** A batch maximum of 50 and request maximum of 16 MiB are acceptable operational safeguards.
- **A-005:** Timestamps contain an explicit offset and at most seven fractional digits, allowing exact datetime2(7) persistence.
- **A-006:** The consumer API may use role-sensitive responses on the same routes; a normal consumer must not be able to infer a hidden entity through status-code differences.
- **A-007:** There is one configured identity per actor type for the challenge: CMS service, normal consumer, and administrator.
- **A-008:** Retention of CmsEventProcessingLogs and tombstones is indefinite for the challenge unless an external retention requirement is provided.
- **A-009:** Returning CMS-managed Payload in consumer responses is permitted.
- **A-010:** Administrators need to see CMS-unpublished active entities, not merely administratively disabled published entities.

## 19. Required behavior, chosen decisions, and future improvements

### 19.1 Required challenge behavior

The requirements explicitly labeled Challenge behavior, the corrected HTTP 200 batch semantics, version and deletion tables, separate CMS/local state, four-table persistence scope, isolated authentication, and SQL Server-only verification are required.

### 19.2 Chosen implementation decisions

- Case-sensitive 200-character identifiers.
- Case-insensitive, trim-aware event-type values normalized to canonical lowercase, while JSON property names remain case-sensitive.
- Raw-array webhook input with external `id` mapped internally to EntityId.
- Separate CurrentVersionOccurredAtUtc and monotonic EntityEventHighWatermarkUtc active-state timestamps.
- Payload equality rules including numeric-token significance.
- 50-event, 16 MiB request, and 256 KiB payload limits.
- Cursor pagination with default 20 and maximum 100.
- Generation 0 for delete-before-first-observation.
- No CmsIngestionBatches table.
- A SQL Server application-lock strategy is recommended but may be replaced only by an equally strong tested strategy.

### 19.3 Optional future improvements

- CMS-signed requests or mutual TLS instead of Basic Authentication.
- Multiple/rotating credentials backed by a managed secrets provider.
- CMS-provided immutable EventId on every event.
- CMS-provided per-entity sequence, generation, or incarnation identifier, especially for delete ordering.
- Persisted batch resources, asynchronous ingestion, dead-letter workflows, or event-log retention jobs if operational requirements emerge.
- Read replicas with an explicit consistency service-level objective.

## 20. Unresolved external contract questions

These questions do not prevent T001 or any later challenge implementation task from proceeding under Section 18 assumptions, but MUST be resolved before claiming production-integration readiness:

1. Can the CMS guarantee a globally unique, immutable EventId and define its retention/reuse scope?
2. Can delete events provide a per-entity sequence, version, generation, or incarnation identifier? Timestamp-only ordering is inherently imperfect.
3. What timestamp precision does the CMS guarantee, and will it remain within the supported seven fractional digits?
4. Can EntityId be reused by the CMS, and are identifiers case-sensitive in the authoritative system?
5. Can the same version legitimately transition publish/unpublish more than once, and does every transition carry the identical full payload?
6. Does the CMS retry an entire batch on 500/503 and treat HTTP 200 per-item deterministic outcomes as acknowledged?
7. Are the proposed batch, request, identifier, and payload limits compatible with real CMS traffic?
8. What retention, privacy, backup, and legal requirements apply to payloads, tombstones, and processing logs?
9. Is payload exposure to both consumer roles permitted, or must the read contract project a narrower schema?
10. Is a production read replica planned, and if so, what post-write consistency lag is acceptable?

## 21. Success criteria

The feature is complete only when every acceptance criterion through AC-057 is backed by a task and passing evidence, the state machine remains pure and deterministic, SQL Server integration tests prove relational/concurrency behavior including high-watermark non-regression, authentication schemes cannot cross-authorize, no secrets or payloads leak into processing logs, and the final diff contains no unplanned architecture or contract changes.
