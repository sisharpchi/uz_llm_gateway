# Production web applications

For the complete disposable local stack, use the repository's Aspire AppHost:
`dotnet run --project src/UZLLM.AppHost` from the repository root. It runs
`npm ci` once for these workspaces, then starts both Vite apps with the
Management proxy target supplied by Aspire. Find their dynamic URLs in the
Aspire dashboard. Docker Desktop, Node/npm, .NET 10, and a trusted exportable
HTTPS development certificate are required. Run `dotnet dev-certs https --trust`
once if needed. See the root README for the full local-stack behavior.

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
payments. Registration verification and password-recovery tokens are delivered
by the configured Worker SMTP transport. Local preview still needs a mail
catcher or test SMTP service; registration responses never contain tokens.
