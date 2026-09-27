# Repository Guidelines

## Project Structure & Module Organization

This repository contains the UZLLM Gateway design pack and modular .NET system.
Read `docs/00_README.md` first, then the numbered documents in order. Preserve
numeric prefixes when adding documents.

`src/` contains separate Gateway API, Management API, and Worker hosts,
business modules, and OpenAI/Anthropic/Gemini/DeepSeek provider adapters.
Modules use Domain, Application, Infrastructure, and Contracts boundaries;
financial transactions share a PostgreSQL context. Tests live in
`tests/Unit`, `tests/Integration`, `tests/Contract`, and `tests/Load`.
`frontend/apps/dashboard` and `frontend/apps/admin` contain React/TypeScript
applications. The separate root `frontend/` mockup is not the deployed
dashboard; preserve it unless a task targets it.

For implementation work, treat Functional Requirements as capability/priority
authority; Domain and Billing as invariant authority; API Contracts as wire
authority; and `docs/16_IMPLEMENTATION_BACKLOG.md` as the task order. Read
`docs/17_POST_IMPLEMENTATION_AUDIT.md` and `docs/18_PRODUCTION_READINESS.md`
before treating completed history as launch-ready.

## Build, Test, and Development Commands

Copy `.env.example` to `.env`, set local migrator and runtime PostgreSQL
passwords, and start dependencies with `docker compose up -d`. Apply schemas
only through `dotnet run --project src/UZLLM.Migrator` using a supplied
`ConnectionStrings__Postgres` migrator connection. Validate the baseline with
`dotnet restore UZLLM.slnx`, `dotnet build UZLLM.slnx --configuration Release
--no-restore`, and `dotnet test UZLLM.slnx --configuration Release --no-build`.
CI runs these commands plus `git diff --check`. No host auto-applies migrations.

## Coding Style & Naming Conventions

Follow the architecture contracts in the design pack. Keep modules isolated:
provider SDK types must remain inside `ProviderAdapters`, and cross-module
access should occur through explicit contracts. Use C# PascalCase for types and
public members, camelCase for locals and parameters, and `Async` for asynchronous
methods. Name modules by business capability (for example, `Billing` or
`ApiKeys`), not generic technical layers. Use TypeScript strict mode and
PascalCase React component filenames.

## Testing Guidelines

Place fast behavior tests in `tests/Unit`; infrastructure-backed tests in
`tests/Integration`; API compatibility checks in `tests/Contract`; and
throughput scenarios in `tests/Load`. Name tests after observable behavior,
such as `ReserveAsync_rejects_insufficient_wallet_balance`. Add regression
coverage for routing, billing, authentication, and payment changes. Preserve
tests for concurrent reservation, settlement idempotency, payment callback
replay, and tenant isolation.

## Commit & Pull Request Guidelines

Git history uses short, imperative area-scoped subjects. Continue that form, e.g.
`docs: clarify payment reconciliation` or `billing: prevent duplicate settlement`.
Keep each commit focused. PRs should describe the change, link relevant issues
or design documents, identify configuration or migration effects, and include
screenshots for dashboard changes. Never commit `.env` files, provider keys, or
payment credentials.
