# Investigation

Use for read-only questions about mechanics, placement, ownership, history, or rationale.

1. Restate the precise question and identify the evidence needed.
2. Trace the relevant execution path, data flow, callers, and boundaries. Use `$how` when the mechanics are non-trivial.
3. For rationale or history, use `$research` in read-only rationale mode. Separate repository facts, external facts, and inference; cite evidence and calibrate confidence.
4. Reconcile conflicting evidence and answer the question directly.
5. Use `$unslop` only if the explanation needs a final clarity pass.

Do not edit code. Do not write an artifact unless the user explicitly requested one through a supporting artifact skill such as `$research`, `$improve-codebase-architecture`, or `$teach`. If the investigation establishes that a code change is required, report the result and route a separately authorized change as Bug Fix, Feature, or Refactoring.
