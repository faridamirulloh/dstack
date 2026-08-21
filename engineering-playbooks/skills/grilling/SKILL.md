---
name: grilling
description: Interview the user rigorously about a plan, decision, or idea. Use for grill requests, assumption checks, design pressure-testing, or when the user wants questions rather than implementation.
---

# Grilling

Map the discussion as a decision tree. A question is ready only when its prerequisite decisions and discoverable facts are settled.

1. Inspect available project facts instead of asking the user to look them up. Optional subagents may investigate independent facts when available.
2. Ask the current frontier in one compact numbered round. For each decision, include a recommended answer and the tradeoff behind it.
3. Wait for the user's answers. Recompute the frontier; do not ask downstream questions whose prerequisites remain open.
4. Challenge contradictions, hidden assumptions, missing failure cases, ownership, migration, and verification.
5. Finish when every material branch is decided, explicitly deferred, or marked unknown with an owner.

Summarize the settled decisions, open questions, and consequences. Do not implement or create documents unless the user separately asks.
