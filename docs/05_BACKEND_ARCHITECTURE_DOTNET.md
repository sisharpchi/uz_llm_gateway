# Backend Architecture — .NET

## 1. Technology baseline

Recommended:

- .NET 10 LTS
- ASP.NET Core
- EF Core
- PostgreSQL / Npgsql
- Redis
- OpenTelemetry
- Serilog
- FluentValidation
- Background worker / Hangfire or Quartz where appropriate
- Docker

---

## 2. Solution structure

```text
src/
  UZLLM.Gateway.Api/
  UZLLM.Management.Api/
  UZLLM.Worker/

  BuildingBlocks/
    UZLLM.SharedKernel/
    UZLLM.Observability/
    UZLLM.Security/
    UZLLM.Persistence/

  Modules/
    Identity/
      Identity.Domain/
      Identity.Application/
      Identity.Infrastructure/

    Organizations/
    Projects/
    ApiKeys/
    Catalog/
    Routing/
    Providers/
    Usage/
    Billing/
    Payments/
    Notifications/
    Audit/

  ProviderAdapters/
    UZLLM.Provider.OpenAI/
    UZLLM.Provider.Anthropic/
    UZLLM.Provider.Google/
    UZLLM.Provider.DeepSeek/

tests/
  Unit/
  Integration/
  Contract/
  Load/
```

This is a **modular monolith architecture**, not a traditional layered monolith where every module can touch everything.

---

## 3. Module boundaries

### Identity
Users, credentials, sessions.

### Organizations
Membership, roles.

### Projects
Workspace/project isolation and settings.

### ApiKeys
Gateway credentials, limits, rotation.

### Catalog
Models, capabilities, provider mappings, price versions.

### Routing
Candidate selection, routing policy, health.

### Providers
Shared provider abstractions.

### Usage
Request metadata and metering.

### Billing
Wallet, ledger, reservation, settlement.

### Payments
Payme/CLICK lifecycle.

### Notifications
Telegram/webhook/email later.

### Audit
Security-sensitive action trail.

---

## 4. Core provider contracts

```csharp
public interface ILlmProviderAdapter
{
    string ProviderCode { get; }

    Task<ProviderResponse> CompleteAsync(
        ProviderRequest request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ProviderStreamChunk> StreamAsync(
        ProviderRequest request,
        ProviderExecutionContext context,
        CancellationToken cancellationToken);
}
```

Do not expose OpenAI/Anthropic SDK types outside adapter modules.

---

## 5. Router contract

```csharp
public interface IRouteSelector
{
    Task<RouteDecision> SelectAsync(
        RoutingContext context,
        CancellationToken cancellationToken);
}
```

`RouteDecision` should contain:
- canonical model;
- provider;
- provider model ID;
- credential ID;
- estimated price;
- reason/score;
- fallback candidates.

---

## 6. Billing contracts

```csharp
public interface IUsageReservationService
{
    Task<Reservation> ReserveAsync(
        ReservationRequest request,
        CancellationToken cancellationToken);

    Task<SettlementResult> SettleAsync(
        SettlementRequest request,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        Guid reservationId,
        string reason,
        CancellationToken cancellationToken);
}
```

Financial operations belong to Billing, not Gateway controllers.

---

## 7. Gateway middleware/pipeline

Recommended ordering:

```text
1. Correlation / trace
2. Request size protection
3. API key authentication
4. Project/org resolution
5. API-key status/expiry check
6. Rate limit / concurrency
7. Request schema validation
8. Permission + model capability validation
9. Budget/balance check
10. Routing decision
11. Cost reservation
12. Provider execution
13. Usage extraction
14. Settlement/release
15. Usage/audit/metrics emission
```

Avoid hidden middleware magic for domain-critical steps. Explicit pipeline/application service is easier to test.

---

## 8. Data access rules

- EF Core for transactional business data.
- Dapper/raw SQL is acceptable for high-volume analytics reads if needed.
- Use explicit transactions for financial state.
- Use optimistic concurrency or row locking where wallet contention requires it.
- No repository abstraction merely to hide EF Core; use repositories only where they express aggregate boundaries.

---

## 9. Outbox pattern

Use Outbox for events that must follow committed changes.

Example:

```text
Payment completed
    |
DB transaction:
  payment = Completed
  wallet ledger += credit
  outbox += PaymentCompleted
    |
commit
    |
Worker publishes/handles outbox
    |
Telegram receipt / analytics
```

This prevents “money committed but event lost”.

---

## 10. Idempotency

Required for:
- payment callbacks;
- payment intent creation where provider supports retries;
- settlement;
- user-facing POST endpoints where duplicate browser submission matters.

Store:
- idempotency key;
- operation scope;
- request hash if useful;
- result/reference;
- expiration.

---

## 11. Testing strategy

### Unit
- route scoring;
- pricing;
- reservation/settlement;
- budget windows;
- fee rules.

### Integration
- PostgreSQL transactions;
- Redis limits;
- payment callback idempotency;
- API-key authentication.

### Contract tests
Each provider adapter must be validated against normalized expectations.

### Load tests
Critical:
- SSE streaming;
- thousands of concurrent open connections;
- Redis limiter;
- wallet reservation contention.

### Chaos/failure tests
- provider timeout;
- 429;
- mid-stream disconnect;
- DB timeout;
- Redis failure;
- worker duplicate delivery.

---

## 12. Module and transaction rules

Start with one assembly per business module, with `Domain`, `Application`,
`Infrastructure`, and `Contracts` folders. Public contracts and host
registration are the only intended cross-module seam. Implementations remain
internal. Module-owned EF Core contexts share one PostgreSQL database; an
explicit transaction coordinator may enlist contexts in a single financial
transaction.

The deployment migrator owns schema changes. API hosts never auto-migrate.
Workers claim PostgreSQL-backed jobs through leases and process an outbox at
least once; consumers must deduplicate.

Provider contracts must expose a normalized request, result, stream event,
usage evidence, provider request ID, and error with an execution disposition:
`NotDispatched`, `RejectedBeforeExecution`, `ExecutionStarted`, or `Unknown`.
Only the Gateway selects a retry/fallback policy.
