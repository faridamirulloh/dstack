# Bug Fix

Use when existing behavior is wrong or has regressed.

1. Define the observed behavior, expected behavior, and smallest reliable reproduction.
2. Trace every relevant caller and data boundary before editing. Use `$how` for mechanics and `$research` for historical rationale when necessary.
3. Identify the root cause. Prefer one fix at the shared cause over guards at each symptom.
4. If the fix changes ownership or an interface boundary, sketch it with `$architect` before implementation.
5. Make the smallest evidence-backed change within the user's authorized scope.
6. Verify through the same surface that exposed the defect, then run the narrowest relevant surrounding checks.
7. Use `$code-review` for risk-sensitive changes, including Blast Radius analysis.

Do not silently broaden the fix, create commits, publish, or run unrelated checks. If the root cause demands a large migration, switch the primary route to Multi-phase Plan and say why.
