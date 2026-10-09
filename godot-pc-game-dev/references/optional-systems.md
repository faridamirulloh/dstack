# Optional systems

Read the relevant section only when it is in scope. Follow the project's existing subsystem before introducing a replacement or dependency.

## Animation

Keep cosmetic pose updates separate from authoritative interactions. Canceled or resumed clips must not duplicate gameplay effects. On visual reattachment, derive the pose from current state.

Use configured AnimationTree parameter paths and playback objects. Choose gameplay-driven movement or a defined root-motion pipeline for each actor; applying both to the same displacement can double movement. Confirm imported clip names, timing, looping, and skeleton mapping when import/runtime inspection is authorized. [AnimationTree](https://docs.godotengine.org/en/stable/classes/class_animationtree.html).

## Audio

Use named buses, validate missing buses, and cancel superseded fades. A crossfade needs overlapping volume changes. Budget simultaneous spatial sources from the intended scene and mix.

Define focus/minimize, pause, and output-device behavior where relevant. Preserve user volume/mute preferences across scene changes. A controller or headset reconnect should follow the project's supported audio-device behavior rather than resetting the entire game.

## Dialogue and localization

Keep dialogue content/conditions independent of its UI. Validate missing IDs, empty branches, and choice bounds. Typewriter skip and frame catch-up should reach the same final text exactly once.

Use stable translation keys and the correct import format. Preserve placeholders, font fallback, text expansion, bidirectional text, and keyboard/controller focus. Do not label PO content as CSV or mix placeholder conventions. [Internationalization](https://docs.godotengine.org/en/stable/tutorials/i18n/internationalizing_games.html).

## Shaders and effects

Choose supported shader types, stages, uniforms, and texture hints for the project release and renderer. Budget fullscreen passes, shaded pixels, texture reads, light/shadow work, and overlapping transparency.

Keep gameplay cues readable at the lowest supported quality profile. Advanced effects need explicit renderer support and hardware evidence; a high-end editor preview is insufficient. [Shading language](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/shading_language.html).

## Language and native integrations

Preserve GDScript/C# conventions in the project. For C#, check Godot/.NET compatibility, exported runtime needs, signals/delegates, and target architectures before proposing interop changes. Avoid subscriptions whose retained callable cannot be disconnected correctly.

For requested multiplayer, storefront, or native-extension work, establish the integration's actual API, authority model, threading constraints, lifecycle, and target-platform support. Do not reuse obsolete replication APIs or assume a native extension has binaries for every OS.

## Diagnostics

Separate inspection, parsing/importing, editor execution, export startup, target-platform profiling, and release packaging in reports. Use documented monitors and real errors with their relevant resource paths/stack traces. Avoid guessed APIs and unbounded logs.

Add checks or toolchain changes only within the task's permission and scope. A dependency or verification framework is not a prerequisite for every gameplay edit.
