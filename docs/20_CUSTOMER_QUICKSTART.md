# Customer Quickstart

1. Register, verify your email and sign in to the dashboard. Create an
   organization and active project.
2. Top up the organization wallet through a configured Payme or CLICK merchant.
   Wait for **Credited**; a pending checkout does not fund inference.
3. Under **API keys**, select that project, create a key and save the show-once
   secret. Never paste it into the dashboard or commit it to Git.
4. Under **Models**, select the same project. Choose a **Published** model and
   copy its chat request. The page uses the current provider price versions and
   managed `default` fee policy. Published does not guarantee live provider
   health, quota or credential availability.

For a terminal, set `UZLLM_GATEWAY_URL` to your deployed Gateway origin (local
HTTP profile: `http://localhost:5214`), `UZLLM_API_KEY` to the issued project
key, and `UZLLM_MODEL` to an ID returned by the model endpoint. The dashboard
code sample uses `VITE_GATEWAY_BASE_URL` when supplied at build
time, otherwise the local Gateway origin in development and the same origin
as the dashboard behind a production edge. For example:

```bash
curl -sS "$UZLLM_GATEWAY_URL/v1/models" \
  -H "Authorization: Bearer $UZLLM_API_KEY"

curl -sS "$UZLLM_GATEWAY_URL/v1/chat/completions" \
  -H "Authorization: Bearer $UZLLM_API_KEY" \
  -H 'Content-Type: application/json' \
  -d "{\"model\":\"$UZLLM_MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello from UZLLM\"}],\"max_completion_tokens\":100,\"stream\":false}"
```

For SSE, set `"stream":true` and use `curl -N`. The Gateway is OpenAI-style,
but only documented fields are accepted. Requests require a funded wallet,
active catalog mapping, effective pricing and an available provider credential.
The published per-million input/output rates include managed markup; a fixed
fee is charged per request. Actual charge uses the selected provider and
measured tokens, so the wallet may reserve a higher bound before the call and
release unused funds afterward. BYOK has a separate fee policy.

Local operators must first configure PostgreSQL/Redis, apply migrations via
`UZLLM.Migrator`, publish an active provider/model/mapping/price and `default`
fee policy, and configure a platform credential; see the root `README.md`.
