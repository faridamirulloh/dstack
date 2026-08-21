# Third-party notices

This plugin vendors and adapts MIT-licensed instructions from these pinned snapshots:

- `mattpocock/skills` at `0ab1b63a410a03d3627979a109c8695de27af954`, copyright 2026 Matt Pocock. See [third_party/LICENSE-mattpocock-skills](third_party/LICENSE-mattpocock-skills).
- `cursor/plugins` pstack at `51a96e0dd838404da19ba83dc70aa21eef71f868`, copyright 2026 Lauren Tan. See [third_party/LICENSE-cursor-pstack](third_party/LICENSE-cursor-pstack).

## Consolidation

The plugin preserves overlapping guidance through modes and references instead of duplicate entrypoints:

- `ask-matt`, `poteto-mode`, and `figure-it-out` informed `engineering-router`.
- pstack `architect` and Matt `codebase-design` became `architect`.
- Matt `code-review` and pstack `blast-radius` became `code-review`.
- Matt `research` and pstack `why` became `research`.
- pstack `technical-writing` and Matt `writing-for-agents` became `technical-writing`.
- Matt `domain-modeling` and pstack `principle-model-the-domain` became `domain-modeling`.
- Both upstream `teach` skills became `teach`.

Cursor-only metadata, fixed model choices, unavailable tool names, and assumptions about external review or Git workflows were removed. Invocation examples use Codex's dollar-prefixed skill syntax, and file-producing modes retain explicit authorization boundaries.
