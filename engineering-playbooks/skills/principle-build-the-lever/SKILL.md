---
name: principle-build-the-lever
description: Apply when repeated or high-risk work benefits from a deterministic codemod, script, generator, query, or check. Reuse existing tools first, and create an artifact only within the user's authorized scope.
---

# Build the Lever

For repetitive or difficult-to-verify work, prefer one deterministic mechanism over many hand edits.

1. Look for an existing project helper, standard-library feature, native tool, or installed dependency before writing anything.
2. Learn the recipe on the smallest representative unit.
3. Build the smallest safe, rerunnable lever when it costs less than repeated manual work or materially improves proof.
4. Verify it against the representative unit, then apply it in independently checkable batches.
5. Keep delegate instructions and write targets isolated if optional subagents are used.

A lever may be an existing command, an ephemeral query, or a new file already covered by the user's requested code changes. Do not create a persistent artifact merely because this principle was cited. Save or commit it only when requested and authorized.

Use [the laziness protocol](../principle-laziness-protocol/SKILL.md) to keep it small and [prove it works](../principle-prove-it-works/SKILL.md) to verify the real result.
