# Multi-phase Plan

Use for migrations, broad rewrites, rollouts, and efforts spanning multiple independently verifiable systems.

1. Confirm the work truly needs phases; keep a coherent one-change task on its simpler route.
2. Ground the plan in current code, constraints, ownership, and verification surfaces.
3. State the target outcome, non-goals, invariants, risks, and rollback or containment strategy when relevant.
4. Divide work into outcome-oriented phases. Each phase must leave the system in a defined state and have its own evidence of completion.
5. Sequence dependency removal, caller migration, compatibility windows, data changes, and cleanup explicitly.
6. Identify decision points that require user or external authority.
7. Write a plan file only when requested; otherwise present the plan in the response.

Planning does not authorize execution. Execute only the phases and external actions the user approved.
