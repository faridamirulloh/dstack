---
name: improve-codebase-architecture
description: Scan a codebase for deepening opportunities and produce a self-contained visual HTML architecture survey. Use for a whole-codebase architecture audit or survey; use `$architect` for designing one selected change.
---

# Improve Codebase Architecture

This skill is a read-only survey of the codebase plus an HTML report. The explicit request for this skill authorizes the report artifact, not code changes, Git operations, or browser automation.

## Process

1. **Scope.** Use the area the user named. Otherwise inspect the codebase without Git unless Git history was separately authorized. Read domain context and relevant ADRs.
2. **Explore.** Look for shallow modules, leaky interfaces, poor locality, scattered test seams, repeated navigation, and coupled responsibilities. Apply the deletion test: deleting a useful module should expose concentrated complexity, not merely move delegation elsewhere.
3. **Rank candidates.** For each candidate name files, current friction, proposed deepening, benefit to locality and testability, ADR conflicts, and recommendation strength: Strong, Worth exploring, or Speculative.
4. **Write the report.** Follow [HTML-REPORT.md](HTML-REPORT.md). Write one self-contained HTML file to the requested location or the OS temporary directory and report its absolute path. Avoid network-loaded assets unless the user requests them.
5. **Stop.** Ask which candidate the user wants to explore. Use `$architect` for alternative interfaces and `$grilling` for decision pressure-testing only after the user chooses.

Do not edit domain docs or create ADRs as a side effect. Use `$domain-modeling` later if the user requests those artifacts.
