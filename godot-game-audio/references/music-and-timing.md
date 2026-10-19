# Music and timing

<!-- Modified adaptation, 10 October 2026: added synchronized/interactive stream choices, cancellation, clock handling, and frame-independent transitions. -->

## Keep music work optional

Preserve the project's music design. A single authored loop can be sufficient; an effects or volume-settings task does not authorize adaptive music. When adaptation is requested, establish the intended states, intensity inputs, transition urgency, musical boundaries, and memory/voice budget before adding stems or middleware.

Vertical layering changes the mix of aligned stems. Horizontal sequencing changes which authored section follows. Their suitability depends on the composition; neither technique repairs unrelated clips automatically. Retain a fallback when the target playback mode cannot provide the desired stream behavior.

## Synchronize with supported streams

For synchronized layers in a compatible release and playback mode, consider `AudioStreamSynchronized`, which starts its sub-streams together. Do not assert that separately calling `play()` on several players guarantees sample alignment. Author compatible stem lengths and loop points, and budget the decoded or streamed cost of silent layers as well as audible layers. See [Godot 4.6 synchronized streams](https://docs.godotengine.org/en/4.6/classes/class_audiostreamsynchronized.html).

For authored clips with transition rules, consider `AudioStreamInteractive` where supported. Its transition table and playback APIs differ from a single ordinary stream. In Godot 4.6, `AudioStreamPlayer.get_playback_position()` returns zero for an interactive stream; do not feed that value into a generic beat scheduler. Select the appropriate playback interface and timing source for the stream type. See [interactive streams](https://docs.godotengine.org/en/4.6/classes/class_audiostreaminteractive.html) and [the player API](https://docs.godotengine.org/en/4.6/classes/class_audiostreamplayer.html).

Treat these as release-specific choices, not guaranteed browser support. Read [platform guidance](platforms-and-performance.md) before using complex streams or effects in a web export.

## Measure the relevant clock

For an ordinary engine-mixed stream, playback position advances with mixing blocks. Where those APIs are supported, timing guidance uses playback position plus time since the last mix and minus estimated output latency to approximate what is audible. Guard startup, seek, pause/resume, track changes, and loop wraps so the scheduler cannot dispatch old boundaries twice. A Web Audio Sample path or another stream type may need a different timing source.

Check `AudioServer.get_time_since_last_mix()` and `get_output_latency()` against the actual release/backend. Output latency is an estimate, and calling a sound on a game frame is not a sample-accurate scheduling guarantee. The [Godot synchronization guide](https://docs.godotengine.org/en/4.6/tutorials/audio/sync_with_audio.html) carries an outdated-content notice; cross-check its API advice with the current class documentation and permitted target behavior checks.

Preserve explicit beat/bar/section data from the authored track. Define boundary tolerance and whether an event occurring exactly on a boundary should run now or on the next one. Do not delay authoritative gameplay simply to fit a beat unless rhythm timing is part of the approved rules. Schedule an audio accent separately when appropriate.

## Handle intensity and transitions

Drive intensity from the chosen gameplay state and smooth it using elapsed time, such as exponential smoothing based on a time constant, rather than a fixed interpolation factor per rendered frame. Use hysteresis or a dwell policy when thresholds would otherwise flap. Keep the mapping editable and avoid forcing combat/health assumptions into a game with another design.

Track the active section and the pending request. Cancel or replace superseded transition/fade work deliberately, and use current gain as the new starting value. A delayed callback for an old section must not stop the new track. On pause, seek, scene handoff, or device interruption, reconcile the scheduler and playback according to the game's policy.

Retain player volume/mute settings throughout transitions. A stem fade, crossfade, and duck can act at different gain stages; do not let each overwrite the same user-controlled property. Validate loop wraps and transition seams by listening only when that work is authorized. Report timing assumptions that remain unverified.
