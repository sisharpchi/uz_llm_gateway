# OPS-006 two-node load qualification (blocked)

Status on 2026-10-10: **Blocked** for final acceptance. The available workstation
has one Docker Desktop engine and no two-host staging deployment. The previous
`DRILL-2026-09-26.md` established a useful single-host 10 RPS rehearsal, but
cannot prove distinct-node behavior. Do not represent synthetic unit fixtures
or two containers on one host as the production reliability gate.

## Repeatable evidence procedure

1. Deploy Gateway A and B on **separate hosts** behind the real TLS edge, with
   shared isolated PostgreSQL/Redis and a deterministic HTTPS upstream. Use
   `tests/Load/UZLLM.LoadSeed` only on a disposable `uzllm_load_*` database;
   keep its printed API key out of artifacts. Verify both `/health/ready` paths
   directly. Set key/project limits to at least 600 RPM and 320 concurrent,
   provider timeout and lease above the fixture's 30-second stream, and wait
   for Redis epoch recovery before measuring.
2. Point both Gateways' `Observability__OtlpEndpoint` at the private collector's
   HTTP OTLP endpoint (for example `http://collector.internal:4318`) and set
   `Observability__OtlpProtocol=http/protobuf`; the application defaults to
   gRPC, whereas this collector config only enables OTLP/HTTP. Use
   `deploy/load/collector.yml` (pinned tested image:
   `otel/opentelemetry-collector-contrib:0.162.0`). Set
   `OTEL_METRIC_EXPORT_INTERVAL=1000` on both Gateway processes. Keep OTLP and
   Prometheus ports on staging-private addresses. Confirm each process has a
   distinct `service_instance_id` label on `uzllm_gateway_active_streams`.
3. Configure a read-only `PGSERVICE` (or protected libpq environment) for the
   isolated database; never pass its password as a command argument. Capture a
   before snapshot:

   ```sh
   psql -X -v ON_ERROR_STOP=1 -v org_id="$UZLLM_LOAD_ORG_ID" -At \
     -f deploy/load/financial-snapshot.sql > artifacts/ops006/before.json
   ```

4. Start `node deploy/load/collect-metrics.mjs "$UZLLM_METRICS_URL" 100
   "$UZLLM_INSTANCE_A" "$UZLLM_INSTANCE_B" artifacts/ops006/telemetry.json`
   and `UZLLM_LOAD_DB_NAME="$ACTUAL_LOAD_DB" node
   deploy/load/collect-financial.mjs 100 2 "$UZLLM_LOAD_ORG_ID"
   artifacts/ops006/financial-samples.json` concurrently, immediately before
   k6. Set `UZLLM_LOAD_URL`, `UZLLM_LOAD_API_KEY`,
   `UZLLM_LOAD_MODEL=fixture-model`, and a fresh eight-hex-digit
   `UZLLM_LOAD_RUN_ID`. Run pinned k6 1.4.0:

   ```sh
   k6 run --summary-export artifacts/ops006/k6-summary.json deploy/load/streaming.js
   ```

   Wait for both samplers to finish. Any missed PostgreSQL sample invalidates
   the financial evidence. The fixed k6 scenario schedules 10
   new streams/second for 60 seconds; its ~30-second drain lowers k6's
   whole-process reported RPS, so count, dropped iterations and scenario
   configuration—not that averaged rate—prove the schedule. k6's
   `http_req_waiting` is client TTFB, not isolated Gateway overhead.
5. Once every stream has drained, capture `after.json` using the same SQL.
   Create `topology.json` from the deployment inventory and collector labels:

   ```json
   {"gatewayA":{"host":"node-a.internal","instanceId":"A"},
    "gatewayB":{"host":"node-b.internal","instanceId":"B"}}
   ```

   Then run:

   ```sh
   UZLLM_LOAD_ENVIRONMENT=staging node deploy/load/qualify-report.mjs \
     artifacts/ops006/k6-summary.json artifacts/ops006/telemetry.json \
     artifacts/ops006/financial-samples.json \
     artifacts/ops006/before.json artifacts/ops006/after.json \
     artifacts/ops006/topology.json artifacts/ops006/report.json
   ```

   The report records revision/environment, request count, client latency,
   collector-backed Gateway TTFT and peak active streams per process, errors,
   wallet/hold/ledger/settlement deltas. A nonzero exit or `passed: false` is
   a failed qualification, never a waiver. Inspect physical host identity and
   trace-derived **Gateway-only overhead** p50/p95/p99 separately against
   NFR-010's 25/100/250 ms targets; client TTFB is not that measurement.
   Attach collector/trace, SQL, k6 and topology evidence to the release record.
   Scrub the API key and DSN from all shared logs. `OPS-007` fault/restore is
   separate and remains unstarted.

## Repository checks performed

- `node --test deploy/load/qualify-report.test.mjs`: 10 passed, 0 failed.
  The same analyzer tests are wired into CI; no CI-hosted run has yet been
  observed for this change.
- `grafana/k6:1.4.0 inspect deploy/load/streaming.js`: parsed fixed 10/s,
  60-second schedule and thresholds.
- `otel/opentelemetry-collector-contrib:0.162.0 validate
  --config=/etc/otelcol-contrib/config.yaml`: passed.
- `dotnet restore UZLLM.slnx`, Release build and `dotnet test UZLLM.slnx
  --configuration Release --no-build`: passed; 551 tests, including 175
  PostgreSQL/Redis integration tests.
- `git diff --check`: passed.

No real two-host load report exists yet. The task remains blocked until the
documented staging evidence is executed and reviewed. The collector and k6
procedures follow the [official OpenTelemetry collector Docker guide](https://opentelemetry.io/docs/collector/install/docker/),
[Prometheus exporter reference](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/exporter/prometheusexporter/README.md),
and [k6 timing/summary reference](https://grafana.com/docs/k6/latest/using-k6/metrics/reference/).
