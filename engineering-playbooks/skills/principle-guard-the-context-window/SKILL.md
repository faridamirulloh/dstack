---
name: principle-guard-the-context-window
description: Apply when large outputs, long files, repeated reads, or broad exploration threaten useful context. Keep only relevant evidence in the main thread and use optional subagents when available and beneficial.
---

# Guard the Context Window

Every payload should earn its place.

- Search and read the narrowest relevant sections first.
- Summarize verbose command output and retain exact pointers to source evidence.
- Split work into bounded phases with explicit inputs and outputs.
- Reuse already established facts instead of rereading the same material.
- Use optional subagents to isolate independent large payloads when available; otherwise process sources sequentially and keep compact notes.
- Keep frequently required invariants near the entrypoint and move occasional detail to linked references.

Do not compress away authorization boundaries, user decisions, failing evidence, or details needed to verify a claim.
