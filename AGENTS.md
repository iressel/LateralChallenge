# Repository Instructions

## Source of truth

- Read `specs/cms-event-ingestion/spec.md`, `specs/cms-event-ingestion/plan.md`, and `specs/cms-event-ingestion/tasks.md` before working.
- Treat `spec.md` as the normative behavior source, `plan.md` as the architectural plan, and `tasks.md` as the execution checklist.
- Do not silently change an accepted contract. When an authorized contract change is legitimate, update its requirements, acceptance criteria, plan, tasks, and traceability together.

## Task boundaries

- Execute only the task explicitly authorized by the user and stop at its boundary.
- Do not proceed to the next task automatically.
- Do not commit, push, configure remotes, create GitHub resources, or create pull requests without explicit authorization.

## Architecture

- Preserve the Domain -> Application -> Infrastructure/API dependency direction.
- Keep Domain independent from ASP.NET Core and Entity Framework Core.
- Keep controllers and endpoints limited to transport concerns.
- Do not introduce MediatR, AutoMapper, generic repositories, microservices, EF Core InMemory, LocalDB, alternate production databases, or in-memory webhook queues.
- Do not create speculative abstractions.

## Event correctness

- Preserve the raw-array webhook input and the external wire property `id`.
- Normalize supported event-type values case-insensitively to the accepted canonical values.
- Preserve immutable version payloads and the Version X to X+1 unpublish behavior.
- Keep CMS publication state separate from administrative disable.
- Keep `CurrentVersionOccurredAtUtc` separate from the monotonic `EntityEventHighWatermarkUtc`.
- Compare delete events against `EntityEventHighWatermarkUtc`.
- Never allow stale data to overwrite newer version state.
- Keep event processing idempotent and concurrency-safe.

## SQL Server and Entity Framework Core

- Use SQL Server as the only production database.
- Keep migrations owned exclusively by `CmsWriteDbContext`.
- Use no-tracking projections for reads and never use the read context for writes.
- Do not use EF Core InMemory as relational proof.
- Never log payloads or SQL parameters containing confidential content.

## Security

- Never commit secrets.
- Never log passwords, Authorization headers, decoded Basic credentials, raw webhook bodies, or raw payloads.
- Do not allow CMS credentials to authorize consumer endpoints or consumer credentials to authorize the CMS webhook.
- Require HTTPS outside local development.

## Code quality

- Use American English for code, documentation, contracts, and commit messages.
- Write nullable-safe code, propagate `CancellationToken`, and prefer asynchronous I/O.
- Do not weaken warnings or tests.
- Add only justified dependencies and do not use preview features.
- Keep changes small and reviewable.

## Validation

- At the applicable task boundary, run the relevant subset of restore, build, tests, format verification, `git diff --check`, and security or secret inspection.
- Report files changed, commands run, exact results, assumptions, unresolved risks, and whether the task completion criteria were met.
