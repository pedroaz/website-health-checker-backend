# Contributing to Website Health Checker

This repository is a small local showcase focused on demonstrating website health checking with ASP.NET Core, PostgreSQL, and Next.js. We welcome contributions that enhance the learning value and functionality within the scope of this demonstration project.

## Getting Started

### Prerequisites

- Git
- Docker with Compose v2.24.4 or newer
- .NET 10 SDK
- Python 3
- Node.js 24
- GitHub CLI

### Development Setup

1. **Clone both repositories** side by side:
   ```bash
   mkdir openhands-demo
   cd openhands-demo
   git clone https://github.com/pedroaz/website-health-checker-backend.git
   git clone https://github.com/pedroaz/website-health-checker-frontend.git
   ```

2. **Install dependencies** from the backend directory:
   ```bash
   bash scripts/agent/setup.sh
   ```

3. **Start the demo stack**:
   ```bash
   docker compose --profile demo up --build -d --wait
   ```

   Access the dashboard at http://localhost:3000 and Mailpit at http://localhost:8025.

4. **Stop when finished**:
   ```bash
   docker compose --profile demo down
   ```

   To reset demo data: `docker compose --profile demo down -v`

## Development Workflow

### Before You Start

- Read [README.md](README.md) for API behavior and HTTP contract
- Read [PLAN.md](PLAN.md) for the approved implementation strategy
- Read [AGENTS.md](AGENTS.md) for agent development conventions
- Check git status to preserve unrelated work

### Creating a Feature Branch

Use a descriptive branch name:
```bash
git checkout -b feature/your-feature-name
```

For agent-assisted work managed by Jira, use:
```bash
git checkout -b jira/KEY-short-description
```

To launch a ticket workflow from Jira, use `/work-ticket KAN-123` in Claude, which automates the lifecycle: intake → feature branch → implementation → verification → independent review → pull request.

### Code Standards

**Database & Persistence**
- Use EF Core migrations for all schema changes: `dotnet ef migrations add NAME --project src/HealthChecker`
- Never silently erase database volumes; document any breaking changes
- Store all timestamps in UTC
- Commit migrations and lockfiles

**Logging & Security**
- Do not log URLs, recipient email addresses, passwords, or email bodies
- Do not implicitly add authentication, OpenAI integration, distributed queues, or deployment automation to the core application (agents may use these for development, but the application remains simple)
- Maintain private-network rejection and DNS connection pinning

**Architectural Constraints**
- The scheduler is intentionally single-instance; ensure manual checks, scheduled checks, pause, and deletion do not overlap unsafely
- Preserve the ten-second overall HTTP deadline and five-redirect limit
- Validate DNS on every hop and pin connections to validated addresses
- Reject connections to private, local, and reserved addresses
- Preserve the Development-only demo origin exception: only `http://demo-target:8080` with `AllowDemoTarget=true` bypasses private-network rejection
- Keep health check status independent from email delivery outcome (health is determined by the check result; email failure does not change it)

**HTTP Contract**
- Maintain forward compatibility with the frontend client
- Validate all input at system boundaries (user input, external APIs)
- Follow the existing API routes defined in [README.md](README.md)

## Testing

All changes require tests. Fast tests run locally; full tests use the isolated Compose suite.

### Fast Tests (Unit Tests)
```bash
dotnet test tests/HealthChecker.Tests --filter 'Category!=Integration'
```

### Full Suite (Integration + Browser Tests)
```bash
./scripts/test.sh
```

This runs backend unit tests, persistence validation, and Playwright browser tests against a temporary Compose stack with real PostgreSQL and Mailpit.

### Code Formatting
```bash
dotnet format WebsiteHealthChecker.slnx --verify-no-changes
```

### Test Guidelines
- Tests must preserve behavioral assertions; a product defect is a defect, not a test failure to hide
- Capture failure evidence and fix the owning code
- Never use public websites as test dependencies
- Never reset the demonstration stack to clean tests

## Submitting Changes

### Before Pushing

1. Build the solution: `dotnet build`
2. Run all tests locally: `./scripts/test.sh`
3. Verify formatting: `dotnet format WebsiteHealthChecker.slnx --verify-no-changes`
4. Verify the running container stack is healthy: confirm the dashboard loads, checks execute, and results appear in Mailpit
5. Ensure git status is clean and only your changes are staged

### Commit Messages

Write clear, descriptive commit messages that explain the *why*:
```
Add health check timeout handling

Implement exponential backoff for slow endpoints to ensure
checks complete within the ten-second deadline.
```

### Pull Requests

- Create a pull request against the `main` branch
- Link any related Jira issues in the PR description
- Ensure all CI checks pass
- Request review from maintainers
- A PR is ready for review; it does not mean the issue is complete

For agent-assisted work, pull request creation is handled automatically and represents the implementation snapshot—human review and merge are still required.

## Cross-Repository Coordination

This repository's frontend lives in the sibling `../website-health-checker-frontend` directory:
- **Frontend**: Next.js, Playwright browser tests, UI routing
- **Backend**: ASP.NET Core, PostgreSQL, API, checking/scheduling/email

For changes affecting both:
1. Update the HTTP contract in both repositories (document in the PR description)
2. Run both test suites: backend `./scripts/test.sh` and frontend Playwright tests
3. Record both branch names and commit SHAs in your PR
4. Ensure frontend `planner → generator → healer` agents are coordinated for browser testing
5. Both repositories have independent agent setup; ensure both are configured with Jira and GitHub MCP credentials

## Agent Development

This repository uses an integrated agent setup for automated development tasks:

- **Agent Lifecycle**: intake → feature branch → implementation → verification → independent review → PR
- **Skills**: `/work-ticket KAN-123` launches the complete ticket workflow; `/verify-change`, `/review-change`, and `/finish-ticket` are available separately
- **Model**: All repository agents use Claude Haiku only; no fallback model
- **Local Verification**: 
  - Prose-only changes: `python3 scripts/agent/agent.py verify docs`
  - Code/schema changes: `python3 scripts/agent/agent.py verify full` (requires Docker and the sibling frontend repository)
  - Full gate check: `python3 scripts/agent/agent.py gate --scope full`

Both the backend and frontend repositories require independent agent setup. See [docs/AGENT_SETUP.md](docs/AGENT_SETUP.md) for local and cloud setup, MCP authentication, and CI verification.

## Questions?

- Review [README.md](README.md) for API behavior and demo targets
- Check [PLAN.md](PLAN.md) for approved implementation strategy
- See [docs/AGENT_SETUP.md](docs/AGENT_SETUP.md) for Claude/agent questions
- File an issue on GitHub with concrete requirements and acceptance criteria

Thank you for contributing to the Website Health Checker project!
