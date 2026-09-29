---
name: handoff-task
description: Health Checker handoff task lifecycle step
---

Record Jira key, repository URLs, both branch names and commit IDs, acceptance criteria, work completed, remaining work, verification scope and actual outcomes. Do not include tokens or copy local credentials. The receiving local/cloud runner must authenticate independently, check out these exact commits and rerun verification; ignored evidence files are not transferred. Do not assume local MCP sessions or OAuth credentials exist in a cloud sandbox.
