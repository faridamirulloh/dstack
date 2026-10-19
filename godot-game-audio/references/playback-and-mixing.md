# Playback and mixing

<!-- Modified adaptation, 10 October 2026: replaced incomplete upstream snippets with ownership, settings, lifecycle, and Godot API guidance. -->

## Own playback by event and state

Map a semantic event to a cue definition containing its stream/variants, bus, gain, spatial policy, priority, and lifetime. Use the project's existing resources or sound bank rather than a mandatory global service. Keep cue IDs stable when swapping files so gameplay code does not depend on provider filenames.

Fire a one-shot once for the relevant committed event. Do not play a repair-completion cue each time its room is loaded, or couple a damage cue to a repeatedly evaluated display flag. UI confirmation should follow the accepted interaction; rejected operations should not produce a success cue. Use animation timing for an authored accent only when the animation owns that timing under the game's rules.

For a loop, define which state starts it, which parameters update it, how it releases, and what stops it. A machine starting, running, and shutting down may need distinct phases. Returning an audible view should reconstruct the current sustained state without replaying a historical start effect. Track loop ownership to avoid duplicates across scene changes.

## Choose players and bound voices

Use `AudioStreamPlayer` for non-positional UI/music or a fixed mix, and 2D/3D variants for the game's chosen spatial presentation. A top-down camera alone does not determine whether the game needs 2D or 3D audio. Check listener placement and attenuation instead of assuming an unseen object is inaudible.

Choose a bounded player pool, appropriate polyphony, or the project's existing playback abstraction when many events can overlap. A separate node per effect can be adequate for sparse one-shots, but does not establish a voice budget. For `AudioStreamPlayer`, `max_polyphony` bounds overlapping plays and cuts off the oldest when exceeded; replacing `stream` stops current playback. A pool sharing one player across different streams therefore needs deliberate ownership. See [the release-specific API](https://docs.godotengine.org/en/4.6/classes/class_audiostreamplayer.html).

Assign priorities, repeat throttling, and stealing rules from gameplay needs. Preserve important warnings while reducing redundant ambience or distant impacts. Do not allocate unbounded nodes in a burst or report a fixed pool size as optimal for every target.

Natural `finished` cleanup is suitable only for an uninterrupted finite one-shot. Loops do not finish naturally, and `stop()` does not emit that signal. Explicitly release or recycle owners on cancellation, scene exit, and interruption. Cancel late callbacks associated with an older use of a pooled voice.

## Apply variation safely

Reject or handle an empty variant collection before random selection; validate missing/null resources. Use weighted or non-repeating selection where that suits repeated cues. `AudioStreamRandomizer` can provide stream, pitch, and gain variation in supported releases, but validate it for the actual playback path. See [its API](https://docs.godotengine.org/en/4.6/classes/class_audiostreamrandomizer.html).

Pitch variation changes both timbre and duration; it should not break synchronization or turn a warning into another cue. Choose ranges from the sound direction, not one universal percentage. Retain an independent RNG/selection policy if gameplay determinism or replay behavior requires it; cosmetic randomness must not perturb authoritative gameplay random sequences.

## Route buses and retain settings

Reuse the project's bus layout, adding categories only when independent control is needed. Music, effects, ambience, UI, and voice are examples, not five mandatory buses. Resolve names with `AudioServer.get_bus_index()` and handle `-1` before calling indexed methods. Player assignment can fall back to Master for a missing bus, so a mistaken name can silently defeat category controls. See [AudioServer](https://docs.godotengine.org/en/4.6/classes/class_audioserver.html) and the player API.

Distinguish authored clip gain, saved user/category volume, temporary duck/fade gain, distance attenuation, and mute. Combine them through deliberate control ownership rather than writing the same property from independent systems. A temporary duck should return to the current user baseline, not hardcoded 0 dB.

Use `linear_to_db()` only when the UI value is linear amplitude; a slider already expressed in dB needs no such conversion. Handle exact zero and mute explicitly, rather than describing a small positive clamp as silence. Save category settings through the existing versioned settings mechanism and apply them after the bus layout is available.

Leave mix headroom for summed voices and effects. Peak meters, clip gain, and integrated loudness are different evidence. A limiter is an optional protection, not proof of a balanced mix, and audio effects depend on platform/playback mode. Do not prescribe one LUFS target for every game or normalize all clips to full scale.

## Own fades and ducking

Keep a handle to the active fade for each controlled gain. Cancel or replace a superseded tween from the current value; do not allow repeated requests to accumulate writers. Two tracks must overlap while their gains change for a crossfade. Choose the curve from the material and mix; naming a fade does not guarantee an equal-power result.

For overlapping dialogue or important effects, retain active duck requests or another explicit policy. Compute the effective attenuation from current requests, and release only after the last applicable request ends. Recompute from the latest user volume when settings change during a duck. On teardown or interrupted playback, release that request without restoring another owner's state.

Sidechain compression and manual gain automation are different mechanisms. Use a supported compressor when desired, or a scoped gain envelope when the platform/mode cannot process the effect. Check bus effect order, send routing, and release behavior against the engine version; do not impose effects on a browser Sample mix.
