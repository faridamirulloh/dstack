---
name: teach
description: Teach a codebase, subsystem, or change in plain language, or maintain an explicit persistent learning workspace. Use for code walkthroughs, conceptual explanations, lessons, learning records, glossaries, missions, and curated references.
---

# Teach

Choose the mode from the user's requested outcome.

## Explanation mode

Default for "teach me" or "explain this codebase". Stay read-only.

1. Ask or infer the learner's goal and current level.
2. Use `$how` to trace mechanics and `$research` for rationale or external background.
3. Start with the mental model, then walk one concrete path end to end.
4. Define terms at first use, connect abstractions to actual files or symbols, and distinguish facts from simplifications.
5. Check understanding with a compact example or question when useful.

## Learning-workspace mode

Use only when the user explicitly asks to create or maintain persistent learning files.

Maintain only the requested artifacts: mission, glossary, resources, lessons, and learning record. Reuse the formats in [references](references/) and link lessons to evidence in the codebase. Update records with what was learned, remaining gaps, and the next concrete exercise; do not create a workspace for a one-off explanation.

## Output

Prefer a layered explanation: orientation, traced example, key concepts, common failure modes, and next questions. Avoid a file-by-file inventory without a story.
