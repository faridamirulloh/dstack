---
name: godot-game-audio
description: Implement or review Godot sound-effect playback, mixing, spatial audio, saved volume settings, fades, and optional interactive music across web, native desktop, and mobile targets. Use for runtime audio integration, not asset generation.
license: Apache-2.0
---

# Godot game audio

<!-- Modified adaptation, 10 October 2026: Godot platform guidance and runtime corrections added to reviewed audio-design material. See references/sources.md, LICENSE, and NOTICE. -->

Connect audio to the game's events and state while preserving its input, pause, save, and sound-direction rules. Extend the existing audio architecture before introducing a new manager, autoload, framework, or middleware. Asset generation and music composition are separate work.

## Establish the runtime context

Read applicable instructions, `project.godot`, relevant playback scripts/scenes, bus configuration, and asset/import settings where available. Establish the Godot release, target platforms, web playback mode, camera/listener behavior, required event cues, loop lifetimes, and saved settings. A browser on a phone follows the web path; native Android/iOS follows the native path.

Use documentation for the actual release. Project feature tags alone do not establish an installed editor/export template version. For optimization, establish simultaneous voices, stream/decoded-memory pressure, target devices, and the observed bottleneck. Missing budgets remain unknown or provisional. Do not impose universal voice counts, loudness values, or compression settings.

Follow task authorization for editor startup, imports, scripts, listening, profiling, device use, tests, builds, exports, plugins, and dependency installation. An audio request does not authorize changing the renderer, language, export threading, or hosting configuration.

## Read the relevant guidance

| Task | Reference |
| --- | --- |
| Event cues, one-shots, loop ownership, buses, settings, spatial playback, ducking | [Playback and mixing](references/playback-and-mixing.md) |
| Requested music transitions, synchronized stems, timing, fades | [Music and timing](references/music-and-timing.md) |
| Browser restrictions, native targets, interruptions, memory/CPU budgets | [Platforms and performance](references/platforms-and-performance.md) |
| Upstream attribution, modifications, and authoritative documentation | [Sources](references/sources.md) |

## Preserve gameplay and user settings

Trigger important audio from authoritative gameplay events or committed state transitions. A view reconnect, camera movement, or animation replay must not repeat rewards, damage, or a one-shot cue. Machine simulation and audible presentation have separate lifetimes: reducing an inaudible source must not stop its gameplay job.

Give continuous sounds explicit owners and start/update/release/stop behavior. Handle scene exit, device interruption, and cancellation rather than relying only on natural playback completion. Bound concurrent voices according to gameplay priorities and device budgets; dropping an inaudible decorative effect should not remove the only cue for an important event.

Use named buses and handle missing names before indexing them. Keep player volume/mute settings separate from authored asset gain, spatial attenuation, fades, and ducking. Cancel superseded fades and combine overlapping duck requests so an old callback cannot restore music too early or overwrite the user's volume.

Choose a supported player/stream type for the perspective and platform. Godot's default web Sample path differs from native playback; verify effects, timing, and stream support before using a desktop pattern in a browser. Preserve the chosen mode unless the task supports a scoped change.

## Finish within scope

Use existing assets or a scoped asset-production workflow when files are missing. Do not silently generate replacement sounds, install a provider, or request cloud credentials for a playback fix. Interactive music, sidechain processing, and spatial reverb are optional decisions rather than mandatory upgrades.

Report files changed, resulting playback/settings behavior, checks actually performed, and remaining platform or listening uncertainty. Separate static code inspection, editor playback, target export behavior, and measured performance. If runtime checks are unavailable or unauthorized, state that limitation without inventing results.

This is an Apache-2.0 adaptation of reviewed audio-design guidance; see [LICENSE](LICENSE), [NOTICE](NOTICE), and [sources](references/sources.md).
