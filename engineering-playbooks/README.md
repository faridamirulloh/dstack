# Engineering Playbooks for Codex

Engineering Playbooks is a Codex plugin that routes substantive software requests to one focused workflow, then applies only the supporting skills and engineering principles that materially help.

The plugin contains 41 deduplicated skills adapted from pinned snapshots of Matt Pocock's skills and Cursor's pstack plugin. It has no runtime dependencies, MCP server, app, or external service requirement.

## How it works

`$engineering-router` is the only skill that may activate implicitly. It classifies a request by its intended outcome and selects exactly one primary playbook:

| Request | Primary playbook |
| --- | --- |
| Understand mechanics, placement, history, or rationale | Investigation |
| Fix a defect, regression, or incident | Bug Fix |
| Add new behavior | Feature |
| Change structure without changing behavior | Refactoring |
| Run a disposable design or behavior experiment | Prototype |
| Plan a migration or broad multi-phase effort | Multi-phase Plan |

Leaf skills are explicit-only. Invoking one directly, such as `$architect` or `$code-review`, overrides automatic routing for that request.

The router preserves Codex authorization boundaries: read-only requests remain read-only, artifacts are created only when requested, and Git, publishing, external actions, dependency installation, and destructive work require explicit authorization.

## Included skills

### Core capabilities

| Skill | Purpose |
| --- | --- |
| `$architect` | Design interfaces, modules, seams, and architecture with a sketch-first workflow. |
| `$code-review` | Perform a read-only review across Standards, Spec, and Blast Radius. |
| `$domain-modeling` | Clarify domain language, decisions, state, and code shape. |
| `$improve-codebase-architecture` | Produce a self-contained visual HTML survey of a whole codebase. |
| `$research` | Investigate rationale, history, and external evidence with calibrated confidence and citations. |
| `$teach` | Explain a codebase or maintain an explicitly requested learning workspace. |
| `$technical-writing` | Write concise technical and agent-facing documentation. |

### Focused workflows

| Skill | Purpose |
| --- | --- |
| `$bro` | Restate an answer in plain language with less jargon. |
| `$grill-me` | Stress-test the user's plan through a focused interview. |
| `$grill-with-docs` | Challenge a plan while maintaining requested domain documentation. |
| `$grilling` | Rigorously interview the user about a plan, decision, or idea. |
| `$handoff` | Create a redacted continuation brief for another agent or session. |
| `$how` | Explain how a codebase, subsystem, request path, or change works. |
| `$no-comments` | Remove misleading comments by encoding their constraints in code. |
| `$prototype` | Build a clearly disposable spike or proof of concept. |
| `$resolving-merge-conflicts` | Resolve an explicitly authorized merge or rebase conflict. |
| `$to-questionnaire` | Turn missing stakeholder knowledge into a focused questionnaire. |
| `$typescript-best-practices` | Apply TypeScript-specific design and implementation guidance. |
| `$unslop` | Remove filler, vague claims, synthetic tone, and other AI tells. |
| `$wizard` | Generate a Bash wizard for manual steps only a human can perform. |

### Engineering principles

The router applies these only when they change the work:

- `$principle-boundary-discipline`
- `$principle-build-the-lever`
- `$principle-encode-lessons-in-structure`
- `$principle-exhaust-the-design-space`
- `$principle-experience-first`
- `$principle-fix-root-causes`
- `$principle-foundational-thinking`
- `$principle-guard-the-context-window`
- `$principle-laziness-protocol`
- `$principle-make-operations-idempotent`
- `$principle-migrate-callers-then-delete-legacy-apis`
- `$principle-minimize-reader-load`
- `$principle-never-block-on-the-human`
- `$principle-outcome-oriented-execution`
- `$principle-prove-it-works`
- `$principle-redesign-from-first-principles`
- `$principle-separate-before-serializing-shared-state`
- `$principle-sequence-verifiable-units`
- `$principle-subtract-before-you-add`
- `$principle-type-system-discipline`

## Installation

These instructions install the current directory as a personal local plugin. Run them from the directory containing this README.

Prerequisites:

- Codex Desktop or CLI with plugin support
- Python 3

### Windows PowerShell

```powershell
$pluginName = "engineering-playbooks"
$sourcePath = (Resolve-Path ".").Path
$destinationPath = Join-Path $HOME "plugins\$pluginName"
$creatorPath = Join-Path $HOME ".codex\skills\.system\plugin-creator\scripts\create_basic_plugin.py"

python $creatorPath $pluginName --with-marketplace
Copy-Item -Path "$sourcePath\*" -Destination $destinationPath -Recurse -Force
codex plugin add "${pluginName}@personal"
```

### macOS and Linux

```sh
plugin_name=engineering-playbooks
source_path="$(pwd)"
creator_path="$HOME/.codex/skills/.system/plugin-creator/scripts/create_basic_plugin.py"

python3 "$creator_path" "$plugin_name" --with-marketplace
cp -R "$source_path"/. "$HOME/plugins/$plugin_name/"
codex plugin add "$plugin_name@personal"
```

The scaffold command creates the personal marketplace at `~/.agents/plugins/marketplace.json` and the plugin source directory at `~/plugins/engineering-playbooks`. The final command installs a versioned copy into the Codex plugin cache.

Start a new Codex task after installation. Existing tasks do not reload newly installed skills.

## Usage

For automatic routing, describe the engineering outcome normally:

```text
Investigate why checkout retries can create duplicate orders.
Fix the regression in the invoice export.
Refactor this parser without changing behavior.
Plan the migration from REST polling to webhooks.
```

Invoke a leaf skill when you want a specific workflow:

```text
$architect design the module boundaries for this feature.
$code-review review this change for blast radius only.
$research explain why this compatibility layer exists.
$technical-writing turn these notes into an RFC.
```

## Project structure

```text
.codex-plugin/plugin.json   Plugin manifest
skills/                     Router, leaf skills, and references
scripts/check_routes.py     Standard-library route and link validation
third_party/                Upstream MIT license texts
THIRD_PARTY_NOTICES.md      Source snapshots and consolidation details
LICENSE                     Plugin license
```

The plugin is a pinned vendored snapshot, not an upstream synchronization tool.

## Validation

From the plugin directory, run:

```sh
python scripts/check_routes.py
```

The check verifies all 41 skill directories, folder/frontmatter agreement, invocation policy, six router playbooks, skill references, local Markdown links, and removed-entrypoint cleanup. It uses only the Python standard library.

## Sources and attribution

This plugin vendors and adapts MIT-licensed material from:

- [mattpocock/skills at `0ab1b63`](https://github.com/mattpocock/skills/commit/0ab1b63a410a03d3627979a109c8695de27af954)
- [cursor/plugins pstack at `51a96e0`](https://github.com/cursor/plugins/commit/51a96e0dd838404da19ba83dc70aa21eef71f868)

Overlapping guidance was consolidated into mode-specific skills instead of duplicate entrypoints. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the full mapping and [third_party](third_party/) for the upstream license texts.

## License

Engineering Playbooks is released under the [MIT License](LICENSE). Vendored upstream material remains subject to its respective MIT license and attribution.
