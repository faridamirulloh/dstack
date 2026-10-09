---
name: godot-mobile-game-dev
description: Implement or review native Godot games for Android and iOS, including touch controls, adaptive UI, mobile rendering, lifecycle-safe persistence, and export preparation.
---

# Godot mobile game development

Build native phone/tablet gameplay around usable touch controls, sustained device performance, and reliable suspend/resume behavior. Preserve the project's chosen language, perspective, renderer, and approved rules. A browser game on a phone follows the web export path; this skill covers native Android/iOS work.

## Establish the project context

Read applicable instructions, the requested feature, `project.godot`, relevant scenes/scripts, and export configuration where available. Identify the Godot version, GDScript or C# choice, target Android/iOS versions and device tiers, renderer/driver, screen orientation/aspect ratios, input model, and persistence expectations.

Use project records or permitted queries to establish installed tools and matching export templates when needed. Project feature tags alone do not prove the editor or SDK version. For C#, verify support and limitations for the exact engine/platform combination before changing the language or runtime.

For performance work, establish a representative low-end target, internal resolution, frame-rate goal, visible populations, memory/startup goals, and acceptable sustained power/thermal behavior. For controls, establish simultaneous actions, finger roles, UI reach, accessibility needs, and optional external-controller support. Keep unresolved choices explicit.

Use available editor, CLI, device, or MCP capabilities. Follow task permissions for imports, scripts, profiling, device deployment, builds, exports, SDKs, plugins, signing, Git, and publishing. A mobile task does not authorize installing toolchains, uploading to a store, or adding ads/IAP/analytics.

## Choose the relevant guidance

Read only what the current task needs. Each reference is bundled with this skill.

| Task | Reference |
| --- | --- |
| Scenes, gameplay, inventory, jobs, physics, state ownership | [Architecture and state](references/architecture-and-state.md) |
| Touch, controller support, safe areas, orientation, UI scaling | [Touch and layout](references/touch-and-layout.md) |
| Device tiers, renderer choice, memory, overdraw, thermal limits | [Mobile performance](references/mobile-performance.md) |
| Pause/resume, Android/iOS integration, native export requirements | [Mobile platforms](references/mobile-platforms.md) |
| Saves, interruption recovery, schema migration | [Persistence](references/persistence.md) |
| GLB imports, materials, pivots, UVs, collision, animation | [Asset integration](references/asset-integration.md) |
| Animation, audio, dialogue, localization, shaders | [Optional systems](references/optional-systems.md) |
| Attribution and authoritative documentation | [Sources](references/sources.md) |

## Implement for real device behavior

Extend the current architecture before adding frameworks. Separate persistent world state from visual scenes and transient touch state. Offscreen rendering, game pause, OS suspension, and offline progression are separate decisions.

Choose Mobile or Compatibility according to the supported devices and rendering features. Verify feature/driver support against the selected release; Forward+ is not a sensible default for a broad mobile target. Preserve an existing renderer until the task supports a deliberate change.

Design touch interaction from gameplay actions rather than copying keyboard bindings onto arbitrary screen buttons. Keep multitouch ownership, UI capture, cancellation, orientation changes, and simultaneous actions explicit. Validate usability on a device only when such checks are authorized.

Save during meaningful gameplay boundaries and prepare for termination after suspension. Resume must reconstruct state without duplicating inventory changes or completed jobs. Do not invent background execution or wall-clock progress to replace suspended simulation.

Keep shared gameplay/save schemas consistent across requested targets. Put touch presentation, device quality profiles, and platform services behind deliberate configuration. Check unfamiliar API names, setting keys, plugin requirements, and export options against the actual release.

## Finish with explicit evidence

Separate editor/mouse-emulated input, simulator behavior, physical-device behavior, native export success, and sustained performance in reports. If a target device, host toolchain, or runtime check is unavailable or unauthorized, identify the remaining limitation.

Report changed files, resulting behavior, checks actually performed, and unresolved platform, controls, design, or performance assumptions. Keep store integrations and publication outside ordinary gameplay work unless requested.

This skill adapts reviewed MIT-licensed Godot guidance; see [sources](references/sources.md) and [LICENSE](LICENSE).
