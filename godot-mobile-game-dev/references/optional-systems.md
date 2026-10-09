# Optional systems

Read the relevant section only when it is in scope. Extend existing systems before adding a new dependency.

## Animation and effects

Keep cosmetic animation independent of authoritative interaction and job state. Reattach the pose from current state; canceled, resumed, or repeated callbacks must not duplicate gameplay effects.

Use configured AnimationTree parameters/playback objects and a defined root-motion convention. Budget bones, visible actors, blend shapes, particle coverage, and update frequency for sustained device performance. Verify imported clips only when import/runtime checks are authorized. [AnimationTree](https://docs.godotengine.org/en/stable/classes/class_animationtree.html).

## Audio and haptics

Use named buses and handle missing buses. Cancel superseded fades and make intended crossfades overlap. Budget simultaneous spatial sources and decoded/streaming memory from the actual mobile mix.

Define backgrounding, interruptions, mute, device-route changes, and resume behavior according to the supported native APIs and game's policy. Preserve user settings. Add haptics only when requested and supported, with device/user-setting fallbacks rather than assuming every platform exposes the same controls.

## Dialogue, text, and localization

Separate dialogue content/conditions from UI. Handle missing IDs, empty branches, and choice bounds. Typewriter skip/catch-up should reach the same final text once.

Use supported translation formats, stable keys, consistent placeholders, suitable fonts/fallbacks, readable physical text size, and layout expansion. Account for bidirectional text, safe areas, and virtual-keyboard obstruction where relevant. [Internationalization](https://docs.godotengine.org/en/stable/tutorials/i18n/internationalizing_games.html).

## Shaders

Use shader types, stages, uniforms, and sampler hints supported by the selected release/renderer. Count passes, shaded pixels, texture reads, and transparency overlap along with geometry. Do not assume desktop effects perform acceptably on phones.

Preserve essential hazard, interaction, and state cues at the lowest supported quality. Read [mobile performance](mobile-performance.md) before adding expensive postprocessing or runtime lighting. [Shading language](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/shading_language.html).

## Native services and networking

For an approved plugin or backend, verify its exact Godot/platform version support, architecture, permissions/capabilities, initialization, interruption/reconnect behavior, and missing-service fallback. Keep purchase or other externally committed outcomes separate from transient UI state.

Multiplayer needs an explicit authority model, lifecycle/reconnect policy, and supported transport. Do not assume continuous foreground execution, stable connectivity, or that background suspension can be avoided by a gameplay timer.

## Diagnostics

Separate mouse-emulated touch, simulator, physical-device execution, exporting, profiling, and publication in reports. Use real errors and documented monitors. Keep logs bounded and exclude save/signing/account secrets.

Follow task permissions for device operations and toolchain changes. Do not add tests, plugins, analytics, or SDKs to complete an unrelated gameplay edit.
