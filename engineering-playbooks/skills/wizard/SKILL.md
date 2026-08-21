---
name: wizard
description: Generate an interactive Bash wizard for manual steps only a human can perform, such as dashboard setup, credentials, CI secrets, migration, or cutover. Do not use when Codex can safely perform the steps directly.
---

# Wizard

Use [template.sh](template.sh) as the fixed interaction library; change only the stages below its marker.

1. Read the relevant repository configuration and map each manual stage, captured value, destination, secrecy, and irreversible action.
2. Verify current third-party instructions from authoritative documentation when needed. Never invent dashboard labels or URLs.
3. Show the ordered stages for confirmation because external and irreversible actions require user control.
4. Copy the template to the requested scratch or scripts path. Add the minimum stage definitions using its existing helpers for prompts, secrets, environment updates, and confirmation gates.
5. Statically trace every captured value to its destination. Run syntax or shell checks only when authorized. Do not execute the wizard, open browsers, write secrets, call external services, commit, or publish on the user's behalf.
6. Report the path, prerequisites, sensitive values handled, irreversible gates, and the exact run command.

Keep the wizard ephemeral unless the user explicitly wants a maintained setup path.
