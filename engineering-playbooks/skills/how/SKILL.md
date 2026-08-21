---
name: how
description: Explain how a codebase, subsystem, request path, or change works through evidence-backed exploration. Use for code walkthroughs, "how does this work", "where does this belong", mechanics before modification, and architectural critique.
---

# How

Trace behavior, do not summarize filenames.

## Workflow

1. Define the question and the observable entrypoint.
2. Follow control flow, data transformations, state ownership, side effects, error paths, and external boundaries end to end.
3. Search callers and implementations to distinguish the common path from special cases.
4. Verify claims against code, tests, configuration, and documentation. Label inference.
5. Explain the mental model first, then walk one representative path with file and symbol pointers.

For broad systems, optional subagents may explore independent areas using [the explorer prompt](references/explorer-prompt.md); synthesize their evidence before answering. Use [the critique rubric](references/critique-rubric.md) when the user asks whether the current structure is good, not merely how it works.

Stay read-only unless the user also requested changes. Use `$research` when the real question is historical rationale or external evidence, and `$architect` when the requested outcome is a new design.
