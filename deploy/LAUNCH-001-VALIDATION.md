# LAUNCH-001 local validation — 2026-09-28

Revision under test: working tree for `LAUNCH-001` before its commit.
This is repository evidence, **not** live merchant acceptance.

| Check | Result |
|---|---|
| `docker build -f deploy/Dockerfile.edge -t uzllm-edge:ops002 .` | PASS; production Nginx image includes default-deny CLICK and disabled real-IP trust files. |
| `node --test deploy/tests/payment-edge.test.mjs` | PASS; 14 assertions/subtests, zero failures. It launches real Nginx and a disposable HTTPS Management protocol stand-in, then runs `nginx -t` under default, allowlisted, denied, and trusted-proxy configurations. |
| Edge behaviors | Exact TLS POST routing, unmodified Payme/CLICK bodies, 32,768-byte acceptance/32,769-byte and chunked oversize rejection, HTTP redirect, unknown-path denial, no upstream POST replay after a disconnect, default CLICK denial, forged forwarding-header denial, and explicit trusted-proxy acceptance. |
| `dotnet test tests/Integration/UZLLM.Persistence.IntegrationTests/UZLLM.Persistence.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~PaymentIntegrationTests` | PASS; 13 PostgreSQL-backed payment tests, zero failures. These exercise the **real** Payme/CLICK handlers, callback replay, one-credit ledger effects and reversal/debt rules. |
| `dotnet restore UZLLM.slnx` / `dotnet build UZLLM.slnx --configuration Release --no-restore` / `dotnet test UZLLM.slnx --configuration Release --no-build` | PASS; build zero warnings/errors; 453 tests passed, zero failed/skipped. |
| `docker compose -f deploy/compose.production.yml config --quiet` with documented dummy required variables | PASS; both policy files resolve to read-only edge mounts in `deploy/nginx/`. |

`deploy/validate-edge.sh` remains the Linux CI Nginx syntax check. Its host
OpenSSL invocation did not work from this Windows Git Bash environment; the
cross-platform Node fixture independently executed `nginx -t` in four real
containers. The CI workflow runs **both** checks on Linux.

CLICK's public Shop API signature does not bind the reversal `error` field.
The edge therefore ships with `deny all;`; real merchant-verified source
addresses, trusted-load-balancer configuration (if used), a callback handshake,
and live settlement verification are external paid-launch prerequisites.
