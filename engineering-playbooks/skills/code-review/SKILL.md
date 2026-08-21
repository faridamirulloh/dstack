---
name: code-review
description: Read-only code review across Standards, Spec, and Blast Radius. Use for full reviews, targeted review axes, branch or diff review, risk review, impact analysis, "what could this break", or blast-radius-only review. Do not use to edit code; use `$no-comments` for the separate comment-removal workflow.
---

# Code Review

Review without modifying files. Support a supplied diff, changed files, a fixed point, or a user-selected area. Run Git commands only when explicitly authorized and allowed by repository instructions.

## Modes

- **Full:** Standards + Spec + Blast Radius.
- **Targeted:** run only the axis the user names. "What could this break?" selects Blast Radius.

## Axes

### Standards

Read repository instructions and relevant conventions. Check correctness, security, maintainability, tests, and code smells, but skip rules already enforced by tooling unless the tool output is evidence of a failure. Repository rules override generic taste.

### Spec

Compare behavior with the supplied issue, requirement, plan, or acceptance criteria. Report missing or partial requirements, incorrect behavior, and scope creep. If no spec exists, say so instead of inventing one.

### Blast Radius

Trace direct callers, downstream consumers, shared types, persistence or schema effects, API contracts, configuration, concurrency, error handling, deployment, and observability. Classify each impact as confirmed, plausible, or unknown and name the evidence needed to resolve unknowns. See [blast-radius guidance](references/blast-radius.md).

## Findings

Report actionable findings first, ordered by severity. Each finding names the location, concrete failure or risk, evidence, and smallest useful remediation. Keep axes separate so a Standards pass cannot hide a Spec failure. Summarize clean axes explicitly.
