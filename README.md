# UZLLM Gateway

UZLLM Gateway is a planned Uzbekistan-first, multi-provider LLM gateway with an
OpenAI-compatible public API, prepaid USD credits funded in UZS, local payment
providers, and tenant-scoped usage visibility.

## Status

This repository now contains the initial .NET solution, module boundaries,
version-controlled foundation schema migration, PostgreSQL/Redis access, and
local development dependencies. It intentionally contains no business capability
or public API endpoint yet.

## Local development

1. Copy `.env.example` to `.env` and choose a local PostgreSQL password.
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

## Read first

- [Design pack index](docs/00_README.md)
- [Architecture decisions](docs/15_ARCHITECTURE_DECISIONS.md)
- [Implementation backlog](docs/16_IMPLEMENTATION_BACKLOG.md)
- [MVP roadmap](docs/14_MVP_AND_DELIVERY_ROADMAP.md)
