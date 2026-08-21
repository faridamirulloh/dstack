# Feature

Use for new production behavior.

1. Pin the outcome, non-goals, constraints, and observable acceptance behavior.
2. Trace the current system with `$how`; identify the narrowest integration seam.
3. Use `$domain-modeling` for important concepts or state transitions and `$architect` for public interfaces or expensive structural choices.
4. Implement the smallest vertical slice that satisfies the requested behavior. Reuse existing patterns and avoid speculative extension points.
5. Verify the behavior at its public surface and add only the smallest durable check required by the project.
6. Use `$code-review` when Standards, Spec fidelity, or Blast Radius warrant a separate pass.

Stay within the requested feature. Do not turn implementation into a migration, dependency upgrade, or publication workflow without authorization.
