# UZLLM Gateway

UZLLM Gateway is an Uzbekistan-first, multi-provider LLM gateway with an
OpenAI-compatible public API, prepaid USD credits funded in UZS, local payment
providers, and tenant-scoped usage visibility.

## Status

This repository contains a buildable .NET modular monolith with versioned
PostgreSQL migrations and separate Gateway, Management, and Worker hosts. The
Gateway supports OpenAI-compatible models/chat with OpenAI, Anthropic, Gemini
and DeepSeek adapters, prepaid admission, streaming, usage finalization, and
same-model/opt-in cross-model fallback. Payme/CLICK, usage reads, BYOK,
customer dashboard, and operator console are implemented. The
[post-implementation audit](docs/17_POST_IMPLEMENTATION_AUDIT.md) identifies
corrective work; [production readiness](docs/18_PRODUCTION_READINESS.md)
separates repository evidence from live merchant/provider and infrastructure
prerequisites. This is not yet a paid-launch claim.

Usage analytics now uses durable hourly read models with a five-second Worker
poll, live partial-hour reads, explicit backlog/freshness and gross/refund/net
charge fields. See [rollup semantics and operations](docs/21_USAGE_ROLLUPS.md).
Activity and Analytics can download bounded, tenant-authorized CSV with the
same filters, UTC windows and unknown/lag semantics as the API. See
[export limits, schema and spreadsheet handling](docs/22_USAGE_EXPORT.md).

Management can configure bounded per-project/per-key RPM and concurrency while
preserving shared Redis counters and PostgreSQL financial budgets. See
[tenant limits, failure semantics and operator ceilings](docs/24_TENANT_LIMITS.md).

Project routing defaults, hard model/provider allowlists and metadata-only
environment tags use the existing project settings storage. Existing budget
policies remain authoritative. See [precedence and safety](docs/25_PROJECT_DEFAULTS.md).

## Local development

### One-command disposable stack (Aspire)

With Docker Desktop running, Node/npm and .NET 10 installed, trust an exportable
local certificate once with `dotnet dev-certs https --trust`, then run from the
repository root:

```powershell
dotnet run --project src/UZLLM.AppHost
```

In Visual Studio, open `UZLLM.slnx`, right-click `UZLLM.AppHost` and choose
**Set as Startup Project**, then select its `https` launch profile and press
F5. The Aspire dashboard opens at `https://localhost:17214`; its resource
links use dynamic ports. Do not start a second AppHost instance while one is
already running. First launch can take several minutes to build, migrate the
disposable database, and run `npm ci`; wait for Gateway, Management, Worker,
dashboard, and admin to become healthy in the Aspire dashboard.

The dashboard URL printed by Aspire lists dynamic URLs for the customer
dashboard, admin app, Gateway, Management API, Mailpit, and HTTPS provider
fixture. The AppHost starts PostgreSQL/Redis, explicitly runs the Migrator and
disposable synthetic catalog seed, then starts runtime hosts and both Vite
workspaces. It generates local-only credentials each run; no `.env` or live
merchant/provider secrets are used. Registration verification mail appears in
Mailpit. The database is ephemeral; this is not two-node, live-payment, or
paid-launch evidence. Stop with Ctrl+C or:

```powershell
aspire stop --apphost src/UZLLM.AppHost/UZLLM.AppHost.csproj
```

The standalone instructions below remain useful when testing individual hosts.

### Standalone hosts

1. Copy `.env.example` to `.env` and choose local PostgreSQL passwords and
   distinct base64-encoded API-key and provider-secret keys. Export the key
   variables into the host environment when running `dotnet` directly; the
   .NET hosts do not automatically load `.env`.
2. Start dependencies with `docker compose up -d`.
3. Restore and validate with:

   ```powershell
   dotnet restore UZLLM.slnx
   dotnet build UZLLM.slnx --configuration Release --no-restore
   dotnet test UZLLM.slnx --configuration Release --no-build
   ```

4. Apply foundation database schemas only through the migration host. Set the
   migrator connection string using the credentials from `.env`, then run:

   ```powershell
   $env:ConnectionStrings__Postgres = "Host=localhost;Port=5432;Database=uzllm;Username=uzllm_migrator;Password=<migrator-password>"
   dotnet run --project src/UZLLM.Migrator
   ```

The Gateway API, Management API, and Worker are deliberate separate hosts; no
host auto-applies migrations. Runtime hosts must use `uzllm_runtime`, which has
schema usage but no DDL permission. See the implementation backlog for the
active task and dependency order.

To run the Gateway, configure runtime PostgreSQL/Redis connection strings,
`APIKEYS__FINGERPRINTKEY`, `PROVIDERSECRETS__ACTIVEKEYVERSION`, and
`PROVIDERSECRETS__KEYS__v1`, then use `dotnet run --project
src/UZLLM.Gateway.Api`. A usable managed inference route also requires an
active catalog provider/model/price, platform credential, effective `default`
fee policy, and funded organization wallet. See
[`gateway.http`](src/UZLLM.Gateway.Api/gateway.http) for request examples.
Customers can inspect effective managed model prices and copy a model-specific
chat request in the dashboard's **Models** page; see the
[customer quickstart](docs/20_CUSTOMER_QUICKSTART.md).

Versioned Gateway and Management OpenAPI artifacts, serializer-derived examples
and typed client generation are documented in [API contracts](docs/api/README.md).
Run `npm run api:check --prefix frontend/apps` to detect contract/client drift.

Opt-in payload retention additionally requires a separate 32-byte base64
`PAYLOADSECRETS__KEYS__v1` and `PAYLOADSECRETS__ACTIVEKEYVERSION=v1` on Gateway
and Management. Without opt-in, no request or response bodies are persisted.

## Read first

- [Design pack index](docs/00_README.md)
- [Architecture decisions](docs/15_ARCHITECTURE_DECISIONS.md)
- [Implementation backlog](docs/16_IMPLEMENTATION_BACKLOG.md)
- [Current next-phase roadmap](docs/19_NEXT_PHASE_ROADMAP.md)
- [Historical MVP roadmap](docs/14_MVP_AND_DELIVERY_ROADMAP.md)
- [Production deployment and recovery](deploy/RUNBOOK.md)
