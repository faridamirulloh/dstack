# Optional Candidate Prompt

Use this brief only when an available Codex subagent would improve independent architecture exploration. The main workflow must also work without subagents.

Read `$architect` and the supplied problem context. Produce one candidate design package using [the rationale template](rationale-template.md):

1. caller usage and realistic call sites;
2. core data structures and invariants;
3. public types and signatures with placeholder bodies;
4. module map, state ownership, seams, and adapters;
5. failure, ordering, concurrency, and idempotency behavior where relevant;
6. complexity hidden versus exposed;
7. migration and verification implications;
8. honest tradeoffs.

Prefer a small, deep interface; domain types at public boundaries; one source of truth; short call chains; and local ownership of related behavior. Do not edit files, create worktrees, run Git, or assume a particular model. Return the candidate to the parent for comparison.
