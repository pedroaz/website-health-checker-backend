---
name: finish-ticket
description: Health Checker finish ticket lifecycle step
---

First run python3 scripts/agent/agent.py gate --scope full (use docs only for prose-only changes). Stop on a failed gate. Inspect git diff and stage only intended paths. Commit on the feature branch, push it, and use gh pr list before gh pr create to avoid duplicates. Include the Jira key, acceptance criteria, checks actually run, limitations and companion PR if relevant. Never push main, merge or deploy during a ticket run. Use Jira MCP to add one concise PR-linked result comment if authenticated; avoid duplicating a native OpenHands callback. Move to the existing review status only when authorized and its actual transition is available. A PR is not Done. If credentials are unavailable, report the missing integration explicitly. Slack is optional: use the approved webhook only for a meaningful outcome, never secrets. Report PR URL and remaining human review.

If Docker is unavailable in a cloud sandbox, explicitly mark local full verification blocked, run fast checks, and open a draft PR for CI verification. This exception does not create passing local evidence. Keep the ticket in review and report the actual CI result before declaring verification complete.
