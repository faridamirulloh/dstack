# Optional systems

Read the relevant section only when that system is in scope. These patterns do not make animation, audio, dialogue, networking, or advanced shaders mandatory for a game.

## Animation

Separate cosmetic animation from authoritative interactions. An offscreen animation can stop while the machine or job continues; reconnect its pose to current state on activation. Completion callbacks must not duplicate state changes after cancellation or re-entry.

For AnimationTree, use the configured parameter paths and playback objects. Do not guess a `state_machine` property or invent blend-node types. Decide whether motion comes from gameplay code or root motion; do not apply both to the same movement. Root motion needs the documented AnimationTree extraction APIs and the project's world-space conversion.

Budget visible skeletal actors, bone counts, blend shapes, and update frequency for the target camera. Prefer rigid-part animation for machinery when it meets the motion requirements. Imported clip existence, timing, looping, and skeleton mapping need explicit checks when import/runtime inspection is authorized. [AnimationTree](https://docs.godotengine.org/en/stable/classes/class_animationtree.html).

## Audio

Use named audio buses and handle missing buses before indexing them. Cancel superseded fades; a crossfade needs overlapping volume changes rather than the default sequential tween behavior.

Account for the browser's user-gesture requirements and the target release's sample/stream behavior. Do not promise every desktop bus effect works in every web export mode. Limit simultaneous sources according to the mix and target-device budget. [Web audio](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html#audio).

## Dialogue and localization

Separate dialogue content and conditions from the UI that displays it. Validate missing IDs, empty sequences, and choice bounds. Typewriter effects modify the label or its visible character count, not an unrelated local string; skip and frame catch-up must reach the same final text once.

Use stable translation keys and the format the import pipeline expects. CSV and PO are different formats. Match placeholder syntax to the chosen formatting API, preserve variables across translations, and consider font fallback, layout expansion, bidirectional text, and locale selection.

Do not replace a project's dialogue system or add a dialogue dependency for one conversation. [Internationalization](https://docs.godotengine.org/en/stable/tutorials/i18n/internationalizing_games.html).

## Shaders and effects

Choose the shader type for the rendered object and the supported renderer. Shader types and stages are distinct; `vertex` is a stage, not a shader type. Use documented uniforms and sampler hints for the project's release rather than carrying over removed Godot 3 built-ins.

Evaluate effects in terms of passes, shaded pixels, transparency overlap, texture reads, and lights, not just mesh triangles. Keep essential gameplay cues readable if an effect is reduced or disabled. Do not make a costly fullscreen effect the only indication of a machine or hazard state. [Shading language](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/shading_language.html).

## Diagnostics and export work

Separate code inspection, parsing, importing, gameplay execution, browser profiling, and exporting in reports. Each establishes different evidence and can have different authorization requirements. Diagnose the requested issue before adding a test framework or changing export templates.

Use actual engine errors, stack traces, resource paths, and documented monitors. Do not add guessed APIs such as `printf`, custom generic GDScript syntax, or unsupported `Performance` constants. Keep logs bounded and avoid exposing full save contents unnecessarily.

## Work requiring a separate design

Networking, C# web migration, and a future engine-version migration require their own verified architecture and compatibility decisions. Do not copy an ENet design into a browser project or claim a future release solves a limitation without official evidence. Keep these outside the initial GDScript browser workflow unless requested.
