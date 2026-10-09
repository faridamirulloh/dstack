---
name: godot-web-game-dev
description: Implement or review Godot GDScript games for desktop and mobile browsers, including automatic controller mapping, persistent state, scene composition, rendering performance, and 3D asset integration.
---

# Godot web game development

Build gameplay that survives scene changes and fits the target browser. Keep the project's chosen perspective, controls, renderer, and game rules. This skill supplies a workflow, not a game design or a fixed performance budget.

## Establish the project context

Read applicable instructions, the requested feature, `project.godot`, relevant scenes/scripts, and export configuration where available. Distinguish approved rules from proposals. Do not turn unresolved design choices into code requirements.

Establish the Godot version, scripting language, rendering method, target browsers/devices, camera behavior, and persistence expectations. Project feature tags can indicate an intended version; they do not prove which editor or export templates are installed. Use existing records or permitted version queries. Ask only for information that blocks the current decision.

For input work, establish the required gameplay actions, keyboard/mouse and touch controls, controller types, and player assignment policy. Desktop browsers and mobile browsers both use the web input backend; native desktop or Android/iOS controller support does not establish browser support.

For optimization, also establish the gameplay resolution, target frame rate, typical and worst visible populations, download/memory goals, and the observed bottleneck. Missing budgets remain unknown or explicitly provisional. Do not impose universal triangle, texture-size, draw-call, or FPS limits.

Use the tools actually available. An MCP integration is optional; do not assume a particular server, tool name, or configuration file. Editor startup can import assets and write files. Follow the task's authorization for editor use, imports, scripts, tests, profiling, exports, dependencies, and Git.

## Choose the relevant guidance

Read only the references needed for the task.

| Task | Reference |
| --- | --- |
| Gameplay code, scenes, input, UI, physics, inventory, jobs | [Architecture and state](references/architecture-and-state.md) |
| Desktop/mobile browser gamepads, automatic mapping, remapping, prompts, touch coexistence | [Browser controller input](references/browser-controller-input.md) |
| Browser rendering, culling, instancing, lighting, performance | [Browser performance](references/browser-performance.md) |
| Saving, loading, migration, browser storage | [Persistence](references/persistence.md) |
| Blender/GLB handoff and imported scenes | [Asset integration](references/asset-integration.md) |
| Animation, audio, dialogue, localization, shaders, diagnostics | [Optional systems](references/optional-systems.md) |
| Upstream attribution and authoritative documentation | [Sources](references/sources.md) |

## Implement with version evidence

Extend the existing architecture before introducing new managers or frameworks. Separate content definitions, mutable world state, and scene presentation where their lifetimes differ. When gameplay must continue offscreen, visibility must not control its authoritative simulation.

Check unfamiliar API names, project-setting keys, shader features, and export options against documentation for the project's actual release. A `stable` or `latest` documentation URL can move to another version. Do not advertise compatibility with releases that have not been checked. Use Godot 4 syntax only for Godot 4 projects; do not mix Godot 3 networking, physics, UI, or shader APIs into them.

For Godot 4 browser projects, use the Compatibility rendering path supported by the applicable release. If the project uses another renderer or C#, explain the verified web limitation before proposing a scoped change. Do not silently switch language or renderer.

When implementing or extending controls, include controller support and automatic default mapping for desktop and mobile browsers unless the user excludes it. Route controllers through semantic InputMap actions, preserve player overrides, handle connection changes, and retain keyboard/mouse or touch fallback. Follow [browser controller input](references/browser-controller-input.md) for detection, mapping limits, UI navigation, and analog controls. A narrow unrelated fix does not authorize a controls overhaul. Automatic mapping cannot identify or correct every controller/browser combination.

Treat optimization as a concrete tradeoff. Specify the rendering or simulation cost being reduced, preserve visible gameplay information, and keep estimated savings separate from measurements. A mesh with fewer triangles can still be slower because of material surfaces, shadows, transparency, textures, or CPU work.

## Finish within the requested scope

Use permitted inspection or verification appropriate to the change. If runtime checks are not authorized or available, report that limitation without inventing results. A successful desktop run does not establish browser compatibility or target-device performance.

Report the changed files, resulting behavior, checks actually performed, and remaining version, design, or performance uncertainty. Do not add an export, dependency, benchmark harness, or broad migration just to make a small change look complete.

This skill adapts reviewed MIT-licensed material from `vl4dt/godot-skills`; see [sources](references/sources.md) and [LICENSE](LICENSE).
