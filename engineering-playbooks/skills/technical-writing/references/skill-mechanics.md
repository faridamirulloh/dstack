# Codex Skill Mechanics

A skill has one entrypoint, `SKILL.md`, with YAML frontmatter containing a folder-matching `name` and a routing-oriented `description`.

Use `agents/openai.yaml` for UI metadata and invocation policy:

- Automatic routers may set `policy.allow_implicit_invocation: true`.
- Explicit-only leaves set `policy.allow_implicit_invocation: false` while keeping a useful description for direct discovery.

Keep the entrypoint focused on when to use the skill, its default workflow, branches, authority boundaries, and completion evidence. Move long rubrics, templates, examples, and source-specific procedures into linked references. Every link should resolve relative to the file that contains it.

Use the exact dollar-prefixed name, such as `$engineering-router`, for another installed skill. Do not assume a fixed model, unavailable tool, external account, or permission. Optional subagents may help with independent work, but the skill must still work without them.
