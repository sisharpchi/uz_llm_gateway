# UZLLM Gateway

UZLLM Gateway is a planned Uzbekistan-first, multi-provider LLM gateway with an
OpenAI-compatible public API, prepaid USD credits funded in UZS, local payment
providers, and tenant-scoped usage visibility.

## Status

This repository contains a buildable .NET modular-monolith baseline,
versioned PostgreSQL migrations, and separate Gateway, Management, and Worker
hosts. The Gateway supports an OpenAI-compatible models/chat surface through
one OpenAI adapter, prepaid admission, streaming, and usage finalization.
Payment integrations, same-model provider failover, and dashboard read models
remain backlog tasks.

## Local development

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

## Read first

- [Design pack index](docs/00_README.md)
- [Architecture decisions](docs/15_ARCHITECTURE_DECISIONS.md)
- [Implementation backlog](docs/16_IMPLEMENTATION_BACKLOG.md)
- [MVP roadmap](docs/14_MVP_AND_DELIVERY_ROADMAP.md)
