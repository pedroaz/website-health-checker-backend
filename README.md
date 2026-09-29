# Website Health Checker — backend

A local, single-user showcase: add a website, check its health, inspect history, and capture a result email in Mailpit. Built with ASP.NET Core 10, EF Core, PostgreSQL, Next.js and Playwright. No OpenAI integration yet.

## Start

Install Docker with Compose v2.24.4 or newer. Keep both repositories beside one another:

```text
openhands-demo/
  website-health-checker-backend/
  website-health-checker-frontend/
```

From this repository:

```sh
# Core application (public HTTP/HTTPS targets)
docker compose up --build -d --wait
# Include predictable websites for a live demo
docker compose --profile demo up --build -d --wait
```

Open [dashboard](http://localhost:3000), [Mailpit inbox](http://localhost:8025), or [OpenAPI JSON](http://localhost:8080/openapi/v1.json). PostgreSQL and SMTP are internal only. `.env.example` lists optional port/password overrides; copy to `.env` if needed. Defaults intentionally work without configuration. If port 8025 is occupied, set `MAILPIT_PORT=8026` in `.env`; Compose also updates the dashboard inbox link. Stop with `docker compose --profile demo down`. Named volumes preserve websites, history and emails. **To intentionally erase local demo data**, use `docker compose --profile demo down -v`.

With the demo profile, add `http://demo-target:8080/healthy` and any valid email, e.g. `demo@example.test`. Other endpoints: `/failing` (503), `/redirect` (to healthy), `/slow` (timeout), `/loop` (redirect limit), `/redirect-private` (blocked redirect). The exact `http://demo-target:8080` origin is the only private-network exception, and only with Development + `AllowDemoTarget=true`. This is a local showcase, not a public hosting configuration.

## Behavior

Creation runs a first check before returning. Active monitors run every five minutes; the scheduler polls every five seconds. Resuming makes a monitor due immediately. Paused monitors allow manual checks. A check uses GET and measures time to response headers, accepts 200–299, follows up to five redirects, and has a ten-second overall deadline. DNS is validated on every hop and connections are pinned to the validated addresses. Bodies are not downloaded. The checker rejects credentials and local/private/reserved addresses. TLS validation is enabled.

Each completed check is saved before email delivery. Email status is Pending, Sent, or Failed independently of website health. SMTP has a bounded timeout. No delivery retry is performed. If the process stops between saving a result and recording email delivery, Pending may remain; delivery is not exactly-once. Do not scale the API beyond one replica. Checks, pause and deletion use in-process locks. Missed scheduled intervals collapse into one check after restart.

Deletion cascades history. Details return the newest 50 checks; older history remains in PostgreSQL. UTC timestamps are shown in the viewer’s local timezone. Readiness requires PostgreSQL. Mailpit failure does not turn healthy websites unhealthy.

## API

| Method | Path | Behavior |
| --- | --- | --- |
| GET / POST | `/api/monitors` | List latest results / create with `{ "url": "…", "email": "…" }` |
| GET / DELETE | `/api/monitors/{id}` | Details / delete monitor and history |
| PATCH | `/api/monitors/{id}` | `{ "paused": true }` or false |
| POST | `/api/monitors/{id}/checks` | Run now; return the saved result; 409 while locked |
| GET | `/api/monitors/{id}/checks` | Latest 50 checks |
| GET | `/health/live`, `/health/ready` | Process liveness / database readiness |
| GET | `/openapi/v1.json` | Generated OpenAPI description |

Invalid URLs/emails produce HTTP 400 validation problem details. Unknown monitor IDs return 404. UI requests are forwarded by Next.js to this API, avoiding browser CORS configuration.

## Verify

```sh
# Full isolated suite; Docker is the only prerequisite
./scripts/test.sh
# Fast local unit tests (.NET 10 SDK)
dotnet test tests/HealthChecker.Tests --filter 'Category!=Integration'
# Formatting validation
dotnet format WebsiteHealthChecker.slnx --verify-no-changes
```

The full runner creates a uniquely named Compose project without published ports, uses its own PostgreSQL/Mailpit volumes, runs backend tests and Chromium browser tests, restarts PostgreSQL/API to prove persistence, and removes only that test project. Backend reports: `artifacts/backend/backend.trx`. Browser report: `../website-health-checker-frontend/playwright-report/index.html`; failure screenshots/traces: frontend `test-results/`. A failed suite exits nonzero; artifacts and `artifacts/compose-test.log` remain. No tests contact public websites.

## Develop

Containers pin the .NET SDK to 10.0.401 and runtime to 10.0.12. Local `global.json` accepts installed .NET 10 SDK feature bands. Packages are locked. Run `dotnet restore --locked-mode` and `dotnet tool restore`; create schema changes with `dotnet ef migrations add NAME --project src/HealthChecker`. Commit migrations and lockfiles. Migrations run before the API starts listening. Never use `EnsureCreated` as a replacement.

The main code lives in `src/HealthChecker`: routes in `Program.cs`, persistence in `Data.cs`, outbound validation/checking in `WebsiteChecker.cs`, scheduling and email in `CheckService.cs`. See [PLAN.md](PLAN.md) for the approved implementation plan and [AGENTS.md](AGENTS.md) for agent conventions.

## What production would require

This deliberately omits authentication/authorization, tenant isolation, rate limits and quotas, distributed job leases, durable notification retries/outbox, bounded retention, operational alerting, backups and recovery drills, secret management, HTTPS termination, network-enforced egress restrictions and service hardening. A production checker needs careful SSRF review and network isolation in addition to application validation. Add those based on deployment needs, not as hidden complexity in this demo. Hosted CI, deployment, releases and OpenAI integration are deferred.

## Agent development

See [local and OpenHands agent setup](docs/AGENT_SETUP.md) for Claude Haiku, MCP connections, lifecycle skills and CI verification.
