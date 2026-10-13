# Repository examples and blocker handling

These are representative workflows, not implementation authorization or a frozen status snapshot. Re-read the live register before acting.

## API-key permissions: APIKEYS-004

Request: “Use $implement-task for APIKEYS-004; I approve selecting this P1 task while OPS-006 is blocked.”

Check DX-002 evidence and FR-027, then inspect:

- `src/Modules/ApiKeys/Contracts/ApiKeyContracts.cs`, `Application/ApiKeyServices.cs`, and `Infrastructure/PostgreSqlApiKeyStore.cs` for tenant authorization and persistence patterns.
- Gateway admission/routing call sites and `tests/Contract/UZLLM.Gateway.ContractTests/GatewayExecutionTests.cs` for reservation/dispatch observability.
- `tests/Unit/UZLLM.ApiKeys.Tests/ApiKeyServicesTests.cs` and `tests/Integration/UZLLM.Persistence.IntegrationTests/ApiKeyPersistenceIntegrationTests.cs` for service fixtures, audit assertions, and database round trips.

Implement configured model/provider permissions across the required admission and Management paths. Demonstrate that denied routes cause **neither reservation nor dispatch**, cross-tenant updates are denied, and changes/revocation apply at admission. Add a versioned migration only if storage requires it; regenerate API/client artifacts if the wire contract changes. Do not also implement PROJECTS-002 or lifecycle screens from FRONTEND-006.

Focused checks, followed by the full baseline in SKILL.md:

```powershell
dotnet test tests/Unit/UZLLM.ApiKeys.Tests/UZLLM.ApiKeys.Tests.csproj --configuration Release
dotnet test tests/Contract/UZLLM.Gateway.ContractTests/UZLLM.Gateway.ContractTests.csproj --configuration Release
dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ApiKey
```

The last filter is illustrative: include any additional Gateway/Management test classes introduced by the task. Update only APIKEYS-004 and required evidence, create the task-only commit, then stop for approval.

## Customer lifecycle UI: FRONTEND-006

Request: “Use $implement-task for FRONTEND-006.”

Verify APIKEYS-004, LIMITS-002, and PROJECTS-002 first, along with any priority exception already authorized. If these are incomplete, stop with their exact IDs; do not build fake endpoints or silently implement the prerequisites.

When ready, inspect `frontend/apps/dashboard/src/`, the shared API client, and `frontend/apps/dashboard/tests/tenant-and-secret.spec.ts`. Existing Playwright specs use accessible locators and mocked Management routes for focused UX cases; the real-stack spec uses a separate acceptance configuration. Cover show-once secrets, edits/revocation, permission failures, and tenant switching against the actual API as required. Preserve the root mockup.

Build both workspaces using `npm run build --prefix frontend/apps`. Run relevant Playwright specs using `npm run test:e2e --prefix frontend/apps -- <spec-path>` (paths follow the dashboard config); use `playwright.acceptance.config.ts`/the real-stack harness for actual backend acceptance. Include screenshots, then commit only this task and stop. Do not start FRONTEND-007 automatically.

## Next task while staging is unavailable

Request: “Use $implement-task to implement the next ready task.”

If the live register still has OPS-006 blocked by unavailable distinct staging hosts, OPS-007 depends on OPS-006 and LAUNCH-003 depends on OPS-007. Do not relabel these ready or replace the two-host evidence with a local Aspire run. Identify the blocking dependency chain and request the missing environment or explicit approval to select an independently ready P1 task. Do not implement P1 while that selection approval is pending. Previous P1 completions alone do not authorize a new exception.

## Blocker decisions

| Condition | Required action and stopping point |
|---|---|
| Missing dependency or unresolved authority conflict | Explain the exact ID/requirement and needed decision. Do not invent a dependency correction or start prerequisite work. |
| Selected task lacks credentials, hosts, policy, or Docker required for acceptance | Keep completed task-local preparation reviewable; record which criterion/check cannot be met, what input unlocks it, and retain `Blocked` or `In Progress`. Do not substitute mocks for required real evidence. |
| Unrelated baseline failure | Record the command and evidence separating it from the task; do not repair unrelated code or mark required validation passed. Stop before completion. |
| Existing unrelated dirty/staged files | Preserve them. Commit only separable task paths/hunks. If the same file/index contains inseparable changes, stop and report the overlap without resetting/stashing the user's work. |
| Acceptance needs a production operation or external action not authorized | Prepare local code, tests, and concrete review evidence first. Ask only for the missing authorization; do not perform the dependent operation. |
| P2 feature request without approved implementation scope | Produce only authorized specification work; ask for feature approval and a bounded implementation task before coding the feature. |
| Commit/hook failure | Report the failure, fix task-local causes if possible, and retry normally. Preserve unrelated index entries; do not bypass hooks, amend another commit, or claim `Completed` without a commit. |

When a selected task becomes blocked, update only its row with concise blocker evidence and an unblock condition. If safe, commit the coherent task-local partial implementation/evidence as one explicitly incomplete task commit; never mark it `Completed`. If no task was selected, or a blocker prevents safe attribution/commit, do not force a backlog edit or empty commit. Report any uncommitted task edits precisely. Stop and ask for the specific input or decision; do not choose another task on your own.

## Final handoff shape

Use actual values, omitting fields with no effect:

> APIKEYS-004 — Completed. Per-key route permissions now reject disallowed requests before reservation/dispatch; tenant-scoped updates take effect at admission. Validation: focused regressions and repository Release baseline passed; API artifacts regenerated and drift checks passed. Migration/configuration: describe actual effects. Commit: actual short hash. Evidence: links to tests/docs. Approve continuing to the next task?

Immediately after an approval request, cite the exact stopping requirement from `docs/19_NEXT_PHASE_ROADMAP.md` and link that file. For a blocked handoff, replace the completion claim with the unmet acceptance criterion, checks actually run, partial commit or uncommitted state, and precise unblock request.
