---
name: architect
description: Design software architecture with a sketch-first workflow for types, interfaces, modules, seams, locality, and deep abstractions. Use for "architect this", "design this", architecture alternatives, interface depth, module boundaries, or codebase design. Use `$improve-codebase-architecture` instead for a whole-codebase HTML architecture survey.
---

# Architect

Design the shape before filling in implementation. Stay read-only unless the user also asks to implement.

## Workflow

1. **Ground.** Trace the relevant behavior with `$how`. Use `$research` in rationale mode when existing history or external constraints matter.
2. **Write the caller first.** Show the simplest intended usage, then sketch types, signatures, state ownership, and a module map. Bodies may be pseudocode or `not implemented`.
3. **Design it twice.** For novel or expensive-to-reverse choices, produce at least two structurally distinct shapes. Optional subagents may explore independent candidates when available.
4. **Compare.** Prefer deep modules: small, stable interfaces hiding substantial complexity. Evaluate information leakage, temporal decomposition, pass-through methods, seam quality, locality, reader load, and how many facts a caller must know.
5. **Choose.** Record the tradeoff and why the rejected shape loses. Use [design red flags](references/design-red-flags.md), [deepening](references/deepening.md), and [design-it-twice](references/design-it-twice.md) as needed.
6. **Implement only when authorized.** Treat the sketch as a hypothesis. Surface deviations; repeated friction of the same shape means redesign, not another escape hatch.

Use [the rationale template](references/rationale-template.md) for substantial design artifacts. A checkpoint is opt-in unless higher-priority instructions require one.

## Output

Lead with caller usage, then the chosen type/interface sketch, module boundaries, invariants, rejected alternative, and verification implications. Keep the artifact proportional to the decision.
