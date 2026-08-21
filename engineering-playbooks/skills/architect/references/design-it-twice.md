# Design It Twice

Use when the first architecture is not obviously forced by constraints. The goal is structural contrast, not a vote among agents.

## Process

1. Frame the constraints, callers, invariants, and dependency category using [deepening guidance](deepening.md).
2. Produce at least two materially different interfaces. Do this locally by default. When optional subagents are available and their isolation is useful, assign independent candidates without requiring a model, worktree, or shared write target.
3. For each candidate show caller usage, types and methods, what the implementation hides, seam and adapter choices, error and ordering behavior, and tradeoffs.
4. Compare depth, locality, leverage, misuse resistance, migration cost, and verification surface.
5. Recommend one design or a clearly defined hybrid. Record why the others lost.

Useful contrasting constraints include smallest public interface, simplest common caller, strongest invariant encoding, and cleanest external seam. Two names for the same shape do not count as alternatives.
