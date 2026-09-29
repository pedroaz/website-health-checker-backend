# Website Health Checker — backend

A local, single-user showcase: add a website, check its health, inspect history, and capture a result email in Mailpit. Built with ASP.NET Core 10, EF Core, PostgreSQL, Next.js and Playwright. No OpenAI integration yet.

## Prerequisites

Before starting, ensure you have the following:

- **Docker with Compose v2.24.4 or newer** — required to run the application and tests
- **Both repositories checked out side by side** — this backend and the sibling frontend must be in the same parent directory:
  ```text
  parent-directory/
    website-health-checker-backend/     ← this repo
    website-health-checker-frontend/    ← required sibling repo
  ```

To clone the frontend if you haven't already:
```sh
cd ..
git clone <frontend-repo-url> website-health-checker-frontend
cd website-health-checker-backend
```

## Startup

From the backend repository directory, start the application stack:

**Core application** (monitors external websites):
```sh
docker compose up --build -d --wait
```

**With demo targets** (includes predictable test endpoints for live demonstrations):
```sh
docker compose --profile demo up --build -d --wait
```

After startup completes, access:
- **Dashboard**: [http://localhost:3000](http://localhost:3000)
- **Mailpit inbox** (captured emails): [http://localhost:8025](http://localhost:8025)
- **OpenAPI documentation**: [http://localhost:8080/openapi/v1.json](http://localhost:8080/openapi/v1.json)

PostgreSQL and SMTP services run internally and are not accessible from outside the container network.

### Configuration

`.env.example` lists optional overrides for ports and database password. To customize:
```sh
cp .env.example .env
# Edit .env with your preferred settings
```

Default settings work without configuration. Common overrides:
- `WEB_PORT=3000` — dashboard port
- `API_PORT=8080` — backend API port
- `MAILPIT_PORT=8025` — email inbox port
- `POSTGRES_PASSWORD=local-demo-password` — database password

If port 8025 is already in use, set `MAILPIT_PORT=8026` in `.env`; Compose automatically updates the dashboard inbox link.

### Stopping and cleanup

Stop the application:
```sh
docker compose --profile demo down
```

Named volumes preserve websites, monitoring history, and emails between restarts. **To erase all demo data and start fresh**:
```sh
docker compose --profile demo down -v
```

## Verification

Verify the application and demo targets are working correctly. Run these commands from the backend repository directory.

**Run the full test suite** (full stack integration tests — Docker required only):
```sh
./scripts/test.sh
```

This test runner:
- Creates an isolated Compose project with its own PostgreSQL and Mailpit instances
- Runs backend unit and integration tests
- Runs browser tests with Chromium (from the frontend repository)
- Verifies data persistence by restarting PostgreSQL and the API
- Cleans up only its own test project (leaves demo data untouched)
- Reports: backend results in `artifacts/backend/backend.trx`; browser results in `../website-health-checker-frontend/playwright-report/index.html`

**Run fast local unit tests** (requires .NET 10 SDK):
```sh
dotnet test tests/HealthChecker.Tests --filter ‘Category!=Integration’
```

**Verify code formatting** (requires .NET 10 SDK):
```sh
dotnet format WebsiteHealthChecker.slnx --verify-no-changes
```

### Testing notes

- All tests run against isolated demo databases; the demo data stack is never modified.
- A failed test suite exits with nonzero status; test artifacts and logs are preserved in `artifacts/` for inspection.
- No tests make requests to public websites.
- Test failure logs are saved to `artifacts/compose-test.log`.

## Demo Targets

When running with the `--profile demo` flag, predictable demo endpoints are available for testing and demonstration:

**Base URL**: `http://demo-target:8080/`

Available endpoints:
- `/healthy` — returns 200 (healthy check example)
- `/failing` — returns 503 (unhealthy check example)
- `/redirect` — redirects to `/healthy`
- `/slow` — exceeds the 10-second timeout
- `/loop` — triggers a redirect limit violation (>5 redirects)
- `/redirect-private` — blocked (private network redirect)

Add monitors for these targets in the dashboard with any valid email (e.g., `demo@example.test`). The exact `http://demo-target:8080` origin is the only private-network exception, available only in Development mode with `AllowDemoTarget=true`. This is a local showcase, not a public hosting configuration.

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

## Develop

Containers pin the .NET SDK to 10.0.401 and runtime to 10.0.12. Local `global.json` accepts installed .NET 10 SDK feature bands. Packages are locked. Run `dotnet restore --locked-mode` and `dotnet tool restore`; create schema changes with `dotnet ef migrations add NAME --project src/HealthChecker`. Commit migrations and lockfiles. Migrations run before the API starts listening. Never use `EnsureCreated` as a replacement.

The main code lives in `src/HealthChecker`: routes in `Program.cs`, persistence in `Data.cs`, outbound validation/checking in `WebsiteChecker.cs`, scheduling and email in `CheckService.cs`. See [PLAN.md](PLAN.md) for the approved implementation plan and [AGENTS.md](AGENTS.md) for agent conventions.

## What production would require

This deliberately omits authentication/authorization, tenant isolation, rate limits and quotas, distributed job leases, durable notification retries/outbox, bounded retention, operational alerting, backups and recovery drills, secret management, HTTPS termination, network-enforced egress restrictions and service hardening. A production checker needs careful SSRF review and network isolation in addition to application validation. Add those based on deployment needs, not as hidden complexity in this demo. Hosted CI, deployment, releases and OpenAI integration are deferred.
