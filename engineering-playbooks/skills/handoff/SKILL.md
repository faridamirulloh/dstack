---
name: handoff
description: Compact the current work into a redacted handoff document for another coding agent or session. Use when the user asks for a handoff, continuation brief, or session transfer artifact.
---

# Handoff

Write a Markdown handoff at the requested path, or in the operating system's temporary directory when no path is given.

Include:

- objective and current status;
- decisions and constraints;
- completed changes with canonical file or URL pointers;
- verification actually performed and its results;
- unresolved risks, blockers, and exact next actions;
- suggested installed playbooks, using their exact dollar-prefixed names, when useful.

Do not duplicate specs, plans, ADRs, issues, commits, or diffs. Link them. Redact secrets, tokens, personal data, and unnecessary environment details. Tailor the next steps to any focus the user supplied.
