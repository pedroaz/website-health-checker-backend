---
name: verify-change
description: Health Checker verify change lifecycle step
---

Choose docs only for prose-only changes; changes to agents, skills, configuration, tests or code require full. Run python3 scripts/agent/agent.py verify docs or python3 scripts/agent/agent.py verify full. Full checks require Docker and the sibling repository. If unavailable locally/cloud, report blocked and rely on the actual GitHub CI result before declaring verification complete. Read artifacts/agent/verification.json and the log. Report executed commands and exit codes; inspection is not execution. Never claim tests passed from reading scripts. Never skip, delete or weaken a check to obtain green results.
