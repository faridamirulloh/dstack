---
name: prototype
description: Build a clearly disposable prototype to answer a design, state-model, integration, or UI question. Use for spikes, proofs of concept, behavioral experiments, and requests to compare throwaway alternatives before production work.
---

# Prototype

The question determines the artifact.

1. State the decision and the observable result that would answer it.
2. Choose the smallest branch: [logic/state](LOGIC.md), [UI](UI.md), or a minimal integration probe.
3. Put the artifact in the requested scratch location or a clearly named temporary/prototype path. Reuse the project's runner when possible; do not add dependencies for convenience.
4. Keep state in memory unless persistence is the question. Surface relevant state and behavior after each action.
5. Skip production abstractions, polish, and broad error handling, but retain safety at real trust boundaries.
6. Exercise the uncertain behavior and report observations, limitations, and recommendation.

Do not commit, publish, or promote prototype code automatically. Moving a validated idea into production is a separately scoped Feature or Refactoring task with normal verification.
