# Refactoring

Use for structural change that must preserve behavior.

1. State the behavior that must remain fixed and identify its existing verification surface.
2. Trace callers, data flow, and ownership with `$how`.
3. Describe the structural pressure: reader load, shallow modules, leakage, duplication, coupling, or misplaced domain concepts.
4. Use `$architect` when boundaries change and `$domain-modeling` when the code shape is the problem.
5. Subtract before adding. Prefer moving responsibility and simplifying call sites over introducing parallel abstractions.
6. Migrate callers, then remove the legacy path when authorized; do not leave duplicate APIs by default.
7. Re-run the pinned behavior checks and compare externally observable behavior.

If behavior must change, route as Feature or Bug Fix. If the work needs staged compatibility or rollout, route as Multi-phase Plan.
