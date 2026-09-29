---
name: review-change
description: Health Checker review change lifecycle step
---

Read the diff and acceptance criteria. Use the reviewer subagent (Haiku) for an independent read-only review, supplying the changed paths, criteria and verification result. Review correctness, regression risk, security and unnecessary scope. Fix actionable findings and rerun verification after changes. If no subagents are available, do an explicit separate review pass and disclose that limitation. Review is not approval to merge.
