# Optional Investigator Prompt

Use this template when an available subagent can independently search one authorized evidence source. A single agent may search several sources when fan-out is unavailable or unnecessary.

## Brief

- Question: `{QUESTION}`
- Target files and symbols: `{CODE_ANCHOR}`
- Assigned available source: `{SOURCE}`
- Relevant source guidance: `{SOURCE_GUIDANCE}`

Gather evidence, not a final narrative.

1. Search broadly, then narrow. Record exact queries and scope.
2. Open primary items and read enough context to avoid snippet errors.
3. Follow links within the source when authorized; record cross-source leads for later.
4. Capture short quotes or accurate paraphrases with stable citations, author, and date when available.
5. Record contradictions, alternative readings, and specific searches that returned nothing.
6. Never infer intent from mechanics alone.

Return: source and coverage; direct evidence; indirect evidence with inference chain; contradictions; gaps; and additional leads. Do not modify files or external state.
