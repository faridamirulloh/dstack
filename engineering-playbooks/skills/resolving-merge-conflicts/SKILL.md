---
name: resolving-merge-conflicts
description: Resolve an explicitly authorized in-progress Git merge or rebase while preserving the intent of both sides. Use for merge markers, rebase conflicts, or conflict-resolution review.
---

# Resolving Merge Conflicts

Git operations require the user's explicit request and must obey repository instructions.

1. Inspect the authorized merge or rebase state and conflicting files.
2. Trace the primary source and intent of both sides from code, tests, messages, and linked issues available in scope.
3. Resolve each hunk by preserving both intents when compatible. When they conflict, choose the result matching the stated merge goal and report the tradeoff. Do not invent new behavior.
4. Run only the project checks the user authorized, in the narrowest useful order, and fix only failures caused by the resolution.
5. Stage, continue, commit, abort, or push only when the user explicitly authorized that specific Git action. Otherwise stop with resolved files and exact next commands for the user.
