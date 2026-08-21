# Model the Domain in Code

Before writing conditionals, ask what data structure makes the behavior obvious.

- Replace combinations of booleans with a finite set of named states.
- Replace stringly typed concepts with domain types when the distinction carries rules.
- Centralize variant behavior in an exhaustive match, registry, reducer, or state machine instead of repeating switches across callers.
- Keep invariants at construction and transition boundaries.
- Put data and behavior that change together in one domain module; keep transport and framework adapters outside it.

Prefer the smallest representation that captures real states. Do not add a framework or abstraction when a typed union, map, or function is enough.
