# Synthesizer Prompt

Use this structure to combine evidence gathered locally or by optional investigators.

## Inputs

- Question: `{QUESTION}`
- Code anchor: `{FILES_AND_SYMBOLS}`
- Evidence: `{FINDINGS}`
- Sources unavailable or not searched: `{GAPS}`

Follow [epistemics](epistemics.md). Classify claims as Direct, Supported, Inferred, Speculative, or Unknown. Direct and Supported claims need precise citations. Expose contradictions and use calibrated language for inference. Code proves mechanics, not its own motivation.

## Output

1. **Question**: concise restatement.
2. **Code in question**: paths, lines, and symbols.
3. **What we found**: cited Direct and Supported claims.
4. **What we can infer**: explicit inference chains.
5. **Competing hypotheses**: evidence for, against, and missing.
6. **What we do not know**: unanswered questions, empty searches, and unavailable sources.
7. **Sources consulted**: only tools, repositories, links, and systems actually inspected.
8. **Confidence**: overall level and what evidence could change it.

Spot-check citations using available, authorized tools. Do not assume Git, a CLI, an MCP connector, an external account, or write access. Never claim a source was searched when it was unavailable.
