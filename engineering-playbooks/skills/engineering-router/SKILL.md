---
name: engineering-router
description: Automatically select and run one engineering playbook for substantive software requests, including investigating how or why code works, fixing defects, adding behavior, refactoring without behavior changes, prototyping, and planning large migrations. Do not activate for unrelated conversation, simple prose, translation, or requests that explicitly invoke another skill.
---

# Engineering Router

Choose one primary playbook, add only the principles that materially improve it, and execute within the user's authority.

## Precedence

1. Follow system, developer, repository, and user instructions.
2. If the user explicitly invokes a leaf skill, use that skill instead of automatic routing. Route any remaining engineering work only if needed.
3. Do not activate for unrelated requests or for trivial questions that need no engineering workflow.
4. Select exactly one primary playbook. A leaf skill or principle may support it, but does not become a second primary playbook.

## Select the primary playbook

| Request shape | Primary playbook |
| --- | --- |
| Read-only mechanics, placement, history, or rationale | [Investigation](references/playbooks/investigation.md) |
| Defect, regression, failing behavior, or incident | [Bug Fix](references/playbooks/bug-fix.md) |
| New user-visible or system behavior | [Feature](references/playbooks/feature.md) |
| Behavior-preserving structural change | [Refactoring](references/playbooks/refactoring.md) |
| Throwaway design or behavioral experiment | [Prototype](references/playbooks/prototype.md) |
| Migration, broad rewrite, or multi-phase effort | [Multi-phase Plan](references/playbooks/multi-phase-plan.md) |

When wording is ambiguous, classify by the intended outcome. "Clean this up" is Refactoring if behavior must stay fixed, Bug Fix if behavior is wrong, and Feature if behavior must change. A prototype is disposable evidence; production code is not.

## Select supporting skills

Use the smallest useful set:

- `$how` for mechanics and placement; `$research` for rationale, history, or external evidence.
- `$architect` when public interfaces, module boundaries, state ownership, or expensive-to-reverse choices are involved.
- `$improve-codebase-architecture` when the user asks for a whole-codebase visual architecture survey rather than one design.
- `$domain-modeling` when domain language or code shape is the core problem.
- `$code-review` after changes when risk warrants Standards, Spec, or Blast Radius analysis.
- `$prototype` when the experiment itself needs its dedicated workflow.
- `$unslop`, `$technical-writing`, `$teach`, or another leaf only when the request actually calls for it.

Apply principle skills selectively. Typical pairings are `$principle-fix-root-causes` for defects, `$principle-prove-it-works` for completed changes, `$principle-subtract-before-you-add` for refactors, `$principle-boundary-discipline` for adapters, and `$principle-sequence-verifiable-units` for migrations. Do not recite principles that do not change the work.

## Large-task fallback

Choose Multi-phase Plan when the request crosses several independently verifiable systems, requires a migration or rollout, or cannot safely complete as one coherent change. Ground the plan in the codebase, make phases independently verifiable, and execute only the phases the user authorized.

If no specialized leaf fits, use the primary playbook with Codex's native capabilities. Optional subagents may isolate independent research or review when available; never require a particular model family.

## Boundaries

- Read-only requests stay read-only.
- Create research files, learning workspaces, reports, ADRs, plans, or other artifacts only when requested.
- Do not run Git, publish, message external systems, install dependencies, or perform destructive work unless the user authorized it and higher-priority instructions allow it.
- Prefer reversible, local steps. Ask when an unresolved decision would change scope, produce an irreversible outcome, or require new authority.
- Report the selected playbook only when it helps the user follow the work; do not add ceremony to small requests.
