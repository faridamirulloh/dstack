---
name: godot-pc-game-dev
description: Implement or review native Godot desktop games for Windows, Linux, and macOS, including gameplay, desktop controls, rendering performance, saves, and export preparation.
---

# Godot PC game development

Build native desktop gameplay around the project's hardware, input, and distribution targets. Preserve its chosen perspective, language, renderer, and approved game rules. Native handheld PCs follow their desktop operating-system target.

## Establish the project context

Read applicable instructions, the requested feature, `project.godot`, relevant scenes/scripts, and existing export configuration. Distinguish approved requirements from proposals. Identify the Godot version, GDScript or C# choice, target operating systems/architectures, renderer/driver, camera, and persistence expectations.

Project feature tags indicate an intended version; confirm the actual editor and matching export templates through existing records or permitted queries when that matters. Use documentation for that release. Preserve existing language choices; for C#, check the native export and .NET requirements before changing tooling.

For platform or performance work, establish minimum hardware, resolution/display scaling, frame-rate goal, input devices, visible populations, loading/memory goals, and distribution format. Unknown budgets remain unknown or explicitly provisional. Ask only for missing information that blocks the current decision.

Use available editor, CLI, or MCP capabilities. Follow task permissions for imports, scripts, builds, profiling, exports, dependencies, Git, signing, and publishing. Starting the editor can import assets and write files. Skill use does not authorize a release or installation of platform tools.

## Choose the relevant guidance

Read only what the current task needs. Each reference is bundled with this skill.

| Task | Reference |
| --- | --- |
| Scenes, gameplay, inventory, jobs, input, UI, physics | [Architecture and state](references/architecture-and-state.md) |
| Renderer choice, quality settings, frame time, culling, instancing | [Desktop performance](references/desktop-performance.md) |
| Keyboard/mouse/gamepad, window behavior, native distribution | [Desktop platforms](references/desktop-platforms.md) |
| Save files, schema migration, recovery, settings | [Persistence](references/persistence.md) |
| GLB imports, scale, pivots, materials, collision, animation | [Asset integration](references/asset-integration.md) |
| Animation, audio, dialogue, localization, shaders | [Optional systems](references/optional-systems.md) |
| Attribution and authoritative documentation | [Sources](references/sources.md) |

## Implement for the intended hardware

Extend the current architecture before adding managers or frameworks. Keep reusable content, persistent world state, and visual scenes separate where their lifetimes differ. Offscreen visuals must not stop jobs or machinery that the design requires to continue.

Choose Forward+, Mobile, or Compatibility from feature needs and the minimum hardware. The Mobile renderer can also serve desktop projects. Driver support and fallback behavior depend on version and platform; preserve the existing choice unless the task supports a change. Native desktop export does not inherit the browser's renderer restriction.

Check unfamiliar API names, project-setting keys, shader features, and import/export options against the project's release. Keep Godot 3 and 4 APIs distinct. Reuse meshes/materials and optimize the measured CPU/GPU/loading bottleneck; a triangle reduction alone does not establish a faster frame.

Keep shared gameplay and save behavior consistent when desktop is one of several targets. Put hardware-dependent quality and platform integrations behind deliberate configuration or capabilities. Storefront SDKs, achievements, cloud saves, multiplayer, and mod support require their own requested scope.

## Finish with explicit evidence

Use permitted inspection or verification appropriate to the change. Separate editor behavior, native export startup, target-OS compatibility, and measured performance in reports. If execution is unavailable or not authorized, state what remains unverified.

Report changed files, resulting behavior, checks actually performed, and unresolved platform, design, or hardware assumptions. Do not add a test framework, benchmark harness, custom engine build, signing workflow, or release operation to complete an unrelated feature.

This skill adapts reviewed MIT-licensed Godot guidance; see [sources](references/sources.md) and [LICENSE](LICENSE).
