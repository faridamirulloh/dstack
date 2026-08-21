---
name: technical-writing
description: Write and revise technical docs, RFCs, PR descriptions, commit messages, and agent-facing documentation. Use for concise technical prose, information architecture, progressive disclosure, context pointers, or instructions and skills intended for coding agents.
---

# Technical Writing

Choose the mode from the audience.

## General mode

1. Identify reader, task, decision, and document type.
2. Put the outcome or decision first. Separate tutorials, how-to steps, reference facts, and explanation when mixing them would confuse the reader.
3. Use concrete nouns and verbs, short sections, examples where ambiguity remains, and links to canonical detail instead of duplication.
4. Preserve technical precision while removing throat-clearing, repetition, vague claims, and unsupported certainty.
5. For PR descriptions and commit messages, state why, observable change, risk, and verification; do not invent checks that were not run.

## Agent-doc mode

Use for AGENTS files, prompts, skills, runbooks, and context intended for coding agents.

- Point to canonical code, schemas, commands, and examples rather than copying volatile detail.
- Put routing and invariants near the entrypoint; move optional depth into linked references.
- Use progressive disclosure: enough context to act, then precise pointers for exceptions.
- Write testable instructions with clear authority boundaries and completion evidence.
- Avoid persona filler, duplicated global rules, assumed tools, and long catalogs that do not change action.

See [agent documentation](references/agent-documentation.md) and [skill mechanics](references/skill-mechanics.md) for specialized guidance.
