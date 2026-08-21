# Blast Radius

Treat impact analysis as a graph walk, not a file-count exercise.

1. Identify the changed contract: inputs, outputs, side effects, timing, failure modes, and persistence.
2. Trace direct callers and implementations.
3. Follow shared types, serialized data, schema, configuration, caches, jobs, events, and public APIs to indirect consumers.
4. Check operational effects: rollout order, backward compatibility, retry behavior, observability, and recovery.
5. Separate confirmed impact from plausible impact and unknowns. Cite code paths or artifacts for confirmed claims.

Output affected surface, failure scenario, confidence, evidence, and the smallest verification that would detect it.
