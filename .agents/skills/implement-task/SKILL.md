---
name: implement-task
description: Implement exactly one ready UZLLM Gateway backlog task, validate its acceptance criteria, update its status, and create a task-only local commit before stopping for user approval. Use for backlog implementation requests in this repository.
---

# Implement one backlog task

Deliver one dependency-safe task in this repository; resolve the actual checkout root at runtime. Invocation authorizes the selected task's implementation, validation, status update, and local commit. Stop after the commit for the user's review and approval to continue. Do not push, create a PR, deploy, activate paid traffic, or begin another task unless separately authorized.

## Establish scope and readiness

1. Read applicable `AGENTS.md` files. Record `git status --short`, staged changes, current branch, and HEAD before editing. Preserve all pre-existing work, including untracked files and staged hunks. Do not reset, stash, clean, or overwrite it. If overlapping edits prevent an attributable task diff, stop and explain the overlap.
2. Read `docs/00_README.md` first, then review numbered design documents in order; inspect task-relevant sections in depth. In particular read `16_IMPLEMENTATION_BACKLOG.md`, `17_POST_IMPLEMENTATION_AUDIT.md`, `18_PRODUCTION_READINESS.md`, and `19_NEXT_PHASE_ROADMAP.md`. Use the **next-phase register** in document 16 for new work; preserve the historical table. Re-read current rows rather than relying on an example's status.
3. If the user names an ID, check that row; otherwise select the first eligible unfinished row in register order. Ready means dependencies are complete with credible evidence, acceptance is concrete, required inputs/environments are available, and selection obeys priority/approval rules. `Planned` is not itself proof of readiness; `Blocked` is not a ready task. Resume `In Progress` only when its existing work can safely be attributed to this task.
4. Do not silently bypass an unfinished P0 gate to implement P1. A user-named P1 task explicitly chosen despite the P0 block, or an existing explicit session approval, can authorize that exception; record it in the handoff. Merely asking for the next ready task does not. P2 rows authorize specification work only; feature implementation requires explicit approval and a scoped implementation task. Missing/inconsistent dependency IDs or conflicting authorities require clarification, not invented completion.
5. State the selected ID, acceptance criteria, intended files, dependencies, and validation plan in a brief progress update. Do not ask again when authorization already exists. Read [repository examples and blockers](references/examples.md) when choosing tests or handling a selection/implementation blocker.

## Implement the accepted scope

Inspect the closest source and tests before editing. Use `rg` to trace contracts, implementations, callers, persistence, and existing behavior. Implement all acceptance criteria for this one row; necessary tests, generated contracts, migration files, and documentation belong to the same task. Avoid opportunistic refactors, dependency upgrades, blanket formatting, or unrelated fixes.

Apply document authority explicitly:

- Accepted ADRs (`docs/15_ARCHITECTURE_DECISIONS.md`) own architecture; Functional Requirements (`02`) own capabilities and priorities.
- Domain (`06`) and Billing (`09`) own invariants; Database (`07`) owns persistence; API Contracts (`11`) and current `docs/api/` artifacts own wire behavior. NFR (`03`) and Security (`10`) own operational guarantees. Backlog (`16`) orders implementation.
- Keep separate Gateway API, Management API, and Worker hosts. Business modules use `Domain`, `Application`, `Infrastructure`, and `Contracts` folders in one module assembly. Cross-module calls use explicit contracts; do not widen the frozen architecture-test exceptions. Provider SDK types stay inside `src/ProviderAdapters/`.
- Preserve the scoped `FoundationDbContext`/`ITransactionCoordinator` financial boundary and append-only ledger. Keep reservation/settlement/payment replay behavior and tenant isolation intact. Never hold a database transaction across an upstream provider call. Unknown usage is not zero cost; never automatically fall back after partial output.
- Schema changes belong in versioned persistence migrations and the explicit `src/UZLLM.Migrator` path. No API/Worker startup migrations. Do not apply migrations to an unknown or shared database. Test fixtures may use their existing disposable-database migration helpers.
- Product frontends are `frontend/apps/dashboard` and `frontend/apps/admin`, with shared `frontend/packages/api-client`. The separate root `frontend/` mockup is outside scope unless explicitly targeted. Follow strict TypeScript, PascalCase React filenames, and existing C# naming/Async conventions.

## Validate with observable evidence

Add or adapt regression tests for changed behavior, especially routing, billing, authentication, and payments. Match nearby xUnit `[Fact]`/`[Theory]` tests and observable behavior names. Place pure behavior in `tests/Unit`, database/Redis behavior in `tests/Integration`, wire/provider compatibility in `tests/Contract`, and throughput scenarios in `tests/Load`. Preserve concurrent reservation, settlement idempotency, callback replay, and tenant-isolation coverage.

Run focused tests during development, then the required repository baseline from the checkout root, sequentially and checking each exit code:

```powershell
dotnet restore UZLLM.slnx
dotnet build UZLLM.slnx --configuration Release --no-restore
dotnet test UZLLM.slnx --configuration Release --no-build
git diff --check
```

Integration fixtures use Testcontainers PostgreSQL/Redis and require Docker. For manual local dependencies, follow repository setup: preserve an existing `.env`; otherwise copy `.env.example`, supply local migrator/runtime passwords, and use `docker compose up -d`. Use a supplied migrator `ConnectionStrings__Postgres` with `dotnet run --project src/UZLLM.Migrator`. Never invent real credentials, print secrets, or commit `.env`.

Select additional checks according to the task's acceptance criteria and current `.github/workflows/ci.yml`:

- Frontend: `npm ci --prefix frontend/apps`, `npm run build --prefix frontend/apps`, and relevant dashboard Playwright specs through the workspace's `test:e2e` script. Capture dashboard screenshots for review. Mocked browser routes alone do not prove a real API flow; run the appropriate acceptance configuration/harness when required.
- Wire/DTO/security-header changes: follow `docs/api/README.md`; regenerate using `npm run api:generate --prefix frontend/apps`, review generated diffs, then run `api:check`, `api:test`, and `api:types` with the same prefix. Include only relevant generated artifacts.
- Edge/load/reliability changes: use the task's validation document and actual CI scripts. Qualifying OPS-006 requires separate staging hosts; two containers on one machine cannot satisfy that gate.

Report exact commands, outcomes, environment, and material limitations. Skipped, unavailable, failed, or mocked checks are not passing acceptance evidence. Fix task-caused failures; do not mask failures, disable tests, or broaden scope to repair an unrelated baseline.

## Update status, commit, and stop

Update only the selected row in `docs/16_IMPLEMENTATION_BACKLOG.md` and documentation/evidence needed by its acceptance criteria. Use the register's existing `Completed`, `In Progress`, or `Blocked` style. Preserve IDs, dependencies, other rows, and historical statuses. Add numbered design documents only when needed and preserve numeric prefixes.

`Completed` requires implementation, acceptance evidence, required tests, matching docs, and the single-task commit. Set it in the proposed commit only after required validation passes, then verify the commit exists. Record external launch blockers separately when the task's acceptance explicitly permits them; missing evidence required by this task prevents completion. Backlog completion never means a production gate has passed.

Before committing, review the full task diff for scope, generated artifacts, secrets, and whitespace. Stage explicit task paths, or only task hunks in shared files; never use blanket `git add .` or `git add -A`. Inspect `git diff --cached` and `git diff --cached --check`. If unrelated changes were already staged, preserve them and use a path-scoped commit only when it excludes all unrelated hunks; otherwise stop at the staging blocker. Do not amend existing commits.

Create one local commit with a short imperative area-scoped subject, for example `api-keys: enforce per-key route permissions`, and include the task ID in the commit body for traceability. Verify `git show --stat --oneline HEAD` and final `git status --short` against the initial snapshot. If the commit fails, fix a task-local cause and retry without disabling hooks; do not claim completion without a commit or disturb other staged work.

The final handoff must identify the task ID/status, behavior delivered, validation results and limitations, migration/configuration effects, commit hash, and remaining blockers. Link relevant evidence/files and screenshots when applicable. Ask for approval to continue and **stop**; do not select or start the next task. This stop is required by `docs/19_NEXT_PHASE_ROADMAP.md`: “Do not begin the next task without user approval.” Link that rule when presenting the approval request; an existing approval for this task is not approval for another.

If blocked before completion, follow the blocker handling in the reference, leave truthful status/evidence, and stop instead of taking a different task.
