---
name: domain-modeling
description: Clarify a software domain through shared language, decisions, and code shape. Use for glossaries, bounded concepts, ADRs, state machines, typed models, registries, reducers, domain modules, or requests to model the domain in language or code.
---

# Domain Modeling

Choose the mode from the requested output. Stay read-only unless the user asks for an artifact or code change.

## Language mode

1. Extract the nouns, verbs, states, invariants, actors, and boundaries already present in code and discussion.
2. Resolve overloaded terms and name distinctions the system must preserve.
3. Produce or update a compact glossary. Record consequential, disputed choices as ADRs only when requested.
4. Keep language tied to observable behavior; do not invent a business ontology for its own sake.

Use [CONTEXT-FORMAT.md](CONTEXT-FORMAT.md) for a persistent domain context and [ADR-FORMAT.md](ADR-FORMAT.md) for decisions.

## Code-shape mode

Choose the structure that makes invalid or forgotten cases hard to represent:

- State machines for legal transitions.
- Discriminated or typed models for variants and invariants.
- Registries for named, extensible behavior.
- Reducers for explicit event-driven state evolution.
- Domain modules for cohesive concepts with behavior and data together.

Model the domain before writing control flow. Prefer one authoritative representation, exhaustive handling, and domain names over primitive flags or scattered conditionals. See [code-shape guidance](references/code-shape.md).

## Output

Show the model, invariants, boundaries, examples, and unresolved questions. Explain what simpler representation was rejected and why.
