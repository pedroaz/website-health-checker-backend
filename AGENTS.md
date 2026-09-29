# Website Health Checker backend

This repository is a small local showcase, not Call Nina and not a production service. Its sibling `../website-health-checker-frontend` owns Next.js and Playwright. This repository owns .NET 10, PostgreSQL schema/migrations, checking/scheduling/email, canonical Compose, deterministic demo endpoints, and backend tests.

- Inspect Git status before editing and preserve unrelated work. Both repositories began empty. Do not publish, push or deploy unless requested.
- Read README.md and PLAN.md before changing behavior. Keep the HTTP contract and frontend callers aligned.
- Use EF Core migrations, never silently erase database volumes. Store UTC timestamps. Do not log URLs, recipient addresses, passwords or email bodies.
- Keep healthy/unhealthy independent from email outcome. Preserve ten-second overall HTTP deadline, five-redirect limit, validated DNS connection pinning, private-network rejection, and the exact Development-only demo origin exception.
- The scheduler is intentionally single-instance. Manual checks, scheduled checks, pause and deletion must not overlap unsafely. Do not add queues, auth, OpenAI, CI or deployment implicitly.
- Automated tests are required here. Fast: `dotnet test tests/HealthChecker.Tests --filter 'Category!=Integration'`. Full stack: `./scripts/test.sh`. Build: `dotnet build`. Formatting: `dotnet format WebsiteHealthChecker.slnx --verify-no-changes`.
- Test against the isolated Compose project created by the runner, with real PostgreSQL and Mailpit and deterministic demo targets. Never reset the user's demonstration stack to clean tests. Do not use public websites as test dependencies.
- Tests must preserve behavioral assertions. A product defect is a product defect; do not skip assertions or inflate timeouts to hide one. Capture failure evidence and fix the owning code.
- Frontend Playwright agents are defined in the sibling `.codex/agents/`. Coordinate contract changes across both repositories. Use the frontend's planner → generator → healer workflow for browser testing.
- Verify the final container stack and relevant tests before reporting success. Record any unverified behavior accurately.

## Agent lifecycle

Agent setup and CI are now part of this repository. Read `docs/AGENT_SETUP.md`. Claude reads `CLAUDE.md` and `.claude/skills`; native OpenHands should read `.claude/skills/work-ticket/SKILL.md` explicitly and follow the same flow. Use Haiku only with no automatic upgrades. Ticket work uses feature branches and PRs, never direct main pushes, merges or deployments. Verification evidence must match the current files. A PR is ready for review, not Done.
