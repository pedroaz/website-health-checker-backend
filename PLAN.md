# Website Health Checker Showcase — implementation plan

## Summary

Populate the sibling `website-health-checker-frontend` and `website-health-checker-backend` repositories with a working local showcase: Next.js App Router and TypeScript dashboard, .NET 10 HTTP API and background checker, PostgreSQL persistence, Mailpit email capture and Docker Compose. Include backend unit/integration tests, Playwright browser tests, and OpenHands/Codex testing-agent instructions. Each repository has root AGENTS.md; this file is the canonical plan, linked from the frontend README.

## Application behavior and contracts

- Create a monitor with a website URL and recipient email, running its first check immediately.
- Check active monitors every five minutes; offer Check now, pause, resume and delete.
- Show latest health and response time; details display the latest 50 checks with email outcomes.
- HTTP 200–299 is healthy. Follow at most five redirects. Other responses, connection failures and the ten-second overall timeout are unhealthy.
- Send one email after every completed check, captured by Mailpit. External delivery is deferred.
- Save monitor configuration, due times and history in PostgreSQL. Keep email failure independent of website health.
- Validate HTTP/HTTPS URLs and email addresses; reject URL credentials and private-network destinations, including redirect targets. Allow only the exact demo-target origin through a Development-only configuration exception. Pin connections to the validated DNS addresses.
- Expose OpenAPI-described GET/POST `/api/monitors`, GET/DELETE `/api/monitors/{id}`, PATCH `/api/monitors/{id}` for paused state, POST `/api/monitors/{id}/checks`, GET `/api/monitors/{id}/checks`, and liveness/readiness endpoints.
- Use consistent validation problems, UTC timestamps and saved manual-check responses. Prevent overlapping checks for the same monitor.

## Implementation and containers

- Keep one ASP.NET Core application, EF Core/Npgsql persistence, a hosted scheduler, and separate checker/mailer services. Run one backend instance.
- Persist due times. After restart, perform one overdue check rather than replaying missed intervals. Pause stops scheduled checks; manual checks remain available. Resume makes the monitor due now.
- Commit EF migrations and apply them before listening for requests. Readiness checks PostgreSQL.
- Build Next.js into a standalone container; forward browser API requests through Next.js using runtime internal Compose addressing.
- Canonical compose.yaml lives in the backend root and builds the sibling frontend.
- Use multistage Dockerfiles, health checks, named volumes, .env.example files, dependency lockfiles and explicit toolchain/image versions.
- Publish localhost dashboard/API/Mailpit ports 3000/8080/8025. Keep PostgreSQL and SMTP internal. Core startup is `docker compose up --build`; document shutdown, intentional data reset and test commands.
- Optional demo profile provides deterministic healthy/failing/redirect/slow endpoints, plus redirect-loop and private-redirect scenarios.
- UI includes accessible labelled forms, visible keyboard focus, loading/empty/error states, mobile layout and periodic refresh.

## Verification and agent workflow

- Unit tests cover health classification, URL/address validation and scheduling. Integration tests exercise notification outcomes independently of health.
- Integration tests use real PostgreSQL and Mailpit for API validation, persistence, pause/resume, check execution, scheduling and email delivery. Controlled substitutes are limited to testing failure/locking behavior in-process.
- Playwright covers monitor creation, history, manual checks, failing/timeout cases, pause/resume/delete, captured email and mobile layout.
- A unique Compose project with isolated volumes and no published ports owns each complete test run. It never resets demonstration data. Use deterministic targets, condition-based polling and no fixed sleeps in browser tests.
- Containerized backend/browser runners emit TRX and HTML reports; browser failures retain screenshots and traces.
- Add planner → generator → healer instructions following Playwright's agent model. The installed Playwright CLI supports Codex generation; include its official generated definitions and meaningful seed test. Document OpenHands' host-dependent MCP setup without claiming native generated-agent support.
- Repairs preserve intended assertions and report product defects rather than weakening tests. No OpenAI account/key is needed for ordinary automated tests; an agent host is needed to invoke AI-assisted workflows.
- Verify a clean Compose startup, URL → check → persisted history → captured email, persistence across actual API/PostgreSQL restart, and passing automated tests.

## Delivery order

1. Scaffold repositories, root instructions, containers, database migrations and startup documentation.
2. Implement backend checking, scheduler and result-email flow.
3. Connect responsive dashboard and monitor details.
4. Add deterministic scenarios, automated tests, agent definitions/instructions and verify the complete stack.

## Defaults and deliberate limits

Local, single-user and unauthenticated. No hosted CI, deployment, release, or OpenAI integration in this iteration. No distributed scheduling or durable mail retries. If interrupted during delivery, a check may retain Pending email status; do not claim exactly-once notification semantics. Keep the implementation understandable for a showcase.

Document future production requirements (authentication, tenant isolation, quotas, robust jobs, durable notification retries/outbox, retention, monitoring, backups, secret management, TLS and network isolation), without implementing that infrastructure now.

## Agent setup extension (September 2026)

The subsequent agent-development setup explicitly adds GitHub Actions CI, Claude Haiku configuration, repository MCP connections and lifecycle skills in both repositories. This supersedes the original no-hosted-CI scope above. See `docs/AGENT_SETUP.md` for local/cloud provisioning, credentials, verification and the remaining account-side Jira/Slack steps. Deployment and automatic merging remain outside this extension.
