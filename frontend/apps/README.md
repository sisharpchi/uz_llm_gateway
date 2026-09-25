# Production web applications

`dashboard/` is the customer P0 app; `admin/` is an access-controlled operator
shell whose privileged controls arrive in ADMIN-001. These are npm workspaces
rooted at `frontend/apps/`. The pre-existing `frontend/` root mock prototype is
left untouched and is not part of the production build.

Run `npm ci --prefix frontend/apps`, then `npm run build --prefix frontend/apps`
and `npm run test:e2e --prefix frontend/apps`. During development, start the
Management API on `http://localhost:5063`, then run
`npm run dev --workspace @uzllm/dashboard --prefix frontend/apps` (port 5174)
or `npm run dev --workspace @uzllm/admin --prefix frontend/apps` (port 5175).
The Vite development proxy keeps browser sessions same-origin. Production must
serve each app and `/management/v1` through the same trusted HTTPS origin;
cookies use `Secure` and `SameSite=Strict`.

The dashboard reads real Management API data. It never simulates a payment
credit, stores a gateway key in browser storage, or displays a secret after the
creation dialog closes. Top-up quotes and wallet money fields are decimal
strings to avoid JavaScript precision loss. Configure the backend merchant
credentials, fee policy and operator-published FX snapshot before accepting
payments. Registration currently requires an externally delivered email
verification token; do not treat the UI as a substitute for that delivery.
