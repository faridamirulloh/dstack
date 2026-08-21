---
name: no-comments
description: Remove misleading, redundant, stale, or workaround comments by fixing the code that made them necessary. Use for explicit comment cleanup or requests to encode constraints in types, tests, runtime checks, or lint rules. This skill edits code; `$code-review` remains read-only.
---

# No Comments

Work only in the files or diff the user placed in scope.

1. Inventory comments and suppressions. Keep legal notices, public API documentation, generated markers, protocol citations, and explanations of constraints outside the code's control.
2. Flag comments that narrate syntax, describe stale behavior, defend dead code, hide a workaround, or state an enforceable invariant in prose.
3. Trace ambiguous comments with `$how` or `$research` before removing them. Optional subagents may perform an independent review, but no named agent is required.
4. Delete trivial narration. For a workaround comment, make the smallest root-cause code fix in scope, then remove the comment.
5. Offer to encode claimed constraints with the cheapest type, test, runtime check, or existing lint mechanism. Do not widen scope without approval.
6. Run only authorized verification and report deletions, retained comments with reasons, code fixes, encoded constraints, and open items.

Do not treat fewer comments as the goal. The goal is code that does not need misleading prose to be understood or kept safe.
