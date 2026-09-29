# Local and OpenHands agent setup

## Quick start

Keep both repositories beside each other. Install Git, Python 3, Node 24, .NET SDK 10, Docker with Compose, GitHub CLI and Claude Code. Then, from either repository:

```sh
bash scripts/agent/setup.sh
gh auth login
python3 scripts/agent/agent.py doctor
bash scripts/agent/claude.sh
```

Setup installs locked dependencies and Chromium, and clones a missing sibling. It never resets or updates an existing checkout. On Linux, install browser OS dependencies with `npx playwright install --with-deps chromium` from the frontend if needed. Both repositories must contain this agent setup. Fetch/pull explicitly when updating an existing clean sibling.

In Claude, accept project trust and use `/mcp` to connect `jira`. GitHub uses the existing `gh` login through a headers helper; no token belongs in `.mcp.json`. Frontend also includes Playwright's test MCP and its planner/generator/healer agents. Run `/work-ticket KAN-123` for a ticket. Start with a small documentation issue containing repository URL, concrete requirements and acceptance criteria.

The lifecycle is intake → feature branch → implementation → verification → independent Haiku review → PR → human review. `/verify-change`, `/review-change`, `/finish-ticket` and `/handoff-task` are also available separately. Native OpenHands can read the same skill files explicitly; it does not automatically become Claude Code because the selected model is Claude.

## Models and costs

The launcher and project default pin `claude-haiku-4-5-20251001`. All repository subagents use Haiku; model switching and non-Haiku agent calls are blocked by project hooks. No fallback model is configured. Use the launcher; an external host or administrator can override project settings, so verify the actual model in each cloud run. These files do not enforce account-wide billing limits.

`bash scripts/agent/run-ticket.sh KAN-123` is a bounded noninteractive entry point: one local run at a time, 30 turns, $2 Claude API-equivalent budget and 30-minute timeout. It may stop for permissions or authentication; interactive Claude is the default for initial connection. Subscription billing and OpenHands compute/hosting costs are separate from this CLI budget. Do not enable permission bypass to fix an authentication problem. Retry at most twice, then report the blocker without upgrading models.

## MCP authentication locally and in cloud

| Connection | Local developer | OpenHands cloud / Claude ACP |
| --- | --- | --- |
| GitHub | `gh auth login`; helper reads its token | Inject scoped `GITHUB_TOKEN` or `GH_TOKEN`, or configure an authenticated `gh` in that sandbox |
| Jira MCP | `/mcp` → jira → OAuth | Authenticate that runtime, or inject Jira email/API token when the Atlassian admin permits API-token MCP access |
| Playwright | Frontend `npm ci` and Chromium | Same dependencies; cloud must support browser OS requirements |
| Slack | Optional approved webhook | Inject its own `SLACK_WEBHOOK_URL`; native Slack installation is separate |

Never copy a developer's home directory or OAuth cache into cloud. The optional ignored `.env.agent.local` is sourced by the local launcher. Cloud secret injection is separate from Git-tracked configuration. `mcp_headers.py capture`, run by setup, saves only required injected MCP credentials in ignored `.claude/mcp-auth.local.json` with mode 0600. This supports project helper environments that scrub credential variables. Delete that file on teardown; replace it on rotation. Do not run the headers helper directly in a visible terminal: its output is an Authorization header intended for the MCP client.

Atlassian tokens require tenant admin support. `JIRA_EMAIL` plus `JIRA_API_TOKEN` uses Basic authentication; `ATLASSIAN_MCP_AUTH` accepts an explicitly supplied complete Authorization value for an approved service account. Missing credentials fall back to interactive MCP OAuth. Native OpenHands Jira authentication does not establish Claude's MCP session.

## Cloud setup

1. Connect the GitHub repository in OpenHands and choose the intended Claude Code/ACP harness. A successful native Haiku run does not prove ACP is selected.
2. Supply Node 24, .NET 10, Python, Git and Claude Code in the runtime. `.openhands/setup.sh` calls the same bootstrap as local. Ensure the external harness actually invokes repository setup; otherwise run it in its provisioning step.
3. Configure the model as the pinned Haiku version with no fallback in the host, too. Start Claude through `scripts/agent/claude.sh` when the host permits a custom command. ACP owns its MCP connections; OpenHands MCP settings are not automatically forwarded to Claude.
4. Inject the runtime's credentials and verify `/mcp` connectivity. GitHub native integration being connected does not by itself prove the Claude subprocess has a usable token.
5. Docker is required for full local checks. If unavailable in the cloud sandbox, run fast checks and open a PR explicitly marked verification pending; GitHub Actions must pass before human merge. This is an explicit exception to the local full gate: do not invent local passing evidence or mark the issue complete.

## Verification and CI

```sh
python3 scripts/agent/agent.py verify fast
python3 scripts/agent/agent.py verify full
python3 scripts/agent/agent.py gate --scope full
```

Full verification runs both repos' fast checks plus the existing isolated Compose suite, including integration/browser tests and persistence checks. Logs and JSON evidence live under ignored `artifacts/agent/`. File changes invalidate evidence. Use `verify docs` only for prose-only changes, never configuration or skill changes. The Stop hook prompts once for missing evidence and allows a blocked report rather than an infinite loop; it is not a substitute for CI or branch protection.

GitHub Actions runs the full flow on pushes and PRs. It checks out the triggering revision and companion main. For paired changes, manually dispatch with `companion_ref` set to the companion commit/branch. Record both SHAs in the PR; one repository's green build alone does not certify an unmerged change in the other. Reports upload even on failure. Repository branch protection and required checks must be configured separately by an administrator; this change does not enable them.

## Jira and Slack lifecycle

The existing native `openhands` label trigger is the proven launch path. To make assignment launch work, configure Jira Automation: issue assigned → assignee equals the actual OpenHands service-account ID → label does not contain `openhands` → add that label while preserving existing labels. Limit to the test project. This rule is a webhook/event trigger, not an MCP feature. Adding an assignee display name alone is not a trigger. Do not remove/re-add labels for retries without checking for an existing run and PR.

Keep native OpenHands responsible for its existing result comment to avoid duplicate comments. Additional Jira reads/updates by Claude require MCP authentication. Resolve review transition IDs from Jira; do not hardcode them. PR creation means Ready for Review, not Done. A separate GitHub/Jira automation can move to Done after merge; enable it only after validating issue-key matching. Deployment and release remain human controlled in this minimum setup.

Slack is optional for the first test. Install the native OpenHands Slack integration for interactive launch, or use a narrowly scoped incoming webhook for completion notifications. Those are separate capabilities. This repository does not install an app into your Slack workspace or silently post messages.

## Acceptance test

1. Run doctor and authenticate MCPs in an interactive local session. Ask Claude to identify the GitHub repository and read a test Jira issue without changing either.
2. Create one small Jira issue with repo and acceptance criteria. Assign it after the assignment automation is configured (otherwise add `openhands` once).
3. Verify exactly one cloud run, actual Haiku usage, feature branch, passing CI, one PR and one Jira result comment.
4. Request a small PR correction and verify the existing PR is updated; check the host's supported follow-up trigger rather than assuming comments restart agents.
5. Review/merge manually. Verify the optional merge automation changes Jira status only after merge. Validate Slack separately if installed.

## References

- [Claude MCP](https://code.claude.com/docs/en/mcp)
- [Claude models](https://code.claude.com/docs/en/model-config)
- [Claude hooks](https://code.claude.com/docs/en/hooks)
- [OpenHands repository setup](https://docs.openhands.dev/openhands/usage/customization/repository)
- [OpenHands ACP](https://docs.openhands.dev/sdk/guides/agent-acp)
- [Atlassian MCP token authentication](https://support.atlassian.com/atlassian-ai-gateway/docs/configure-authentication-via-api-token/)
- [Playwright agents](https://playwright.dev/docs/test-agents)
