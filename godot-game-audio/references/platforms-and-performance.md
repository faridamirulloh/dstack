# Platforms and performance

<!-- Modified adaptation, 10 October 2026: added web/native distinctions, interruption policy, and device-dependent audio budgets. -->

## Establish the target path

Identify the Godot release, browser or native target, selected playback mode, assets/stream types, and representative hardware. A desktop editor run does not prove browser or phone behavior. Keep platform choices behind existing configuration/capability boundaries and preserve the current settings unless a scoped change is justified.

The versioned links here use Godot 4.6 as reviewed evidence. Check a different release's documentation before relying on the same behavior. Prefer available API/editor/CLI/MCP tools; do not require a specific server or install a toolchain for an unrelated audio fix.

## Desktop and mobile browsers

In the reviewed web path, Sample playback uses Web Audio for low latency but does not support Godot audio effects, procedural generation, reverb, or Doppler. Positional behavior has documented limitations. Stream playback enables the engine's fuller audio path at a latency cost, especially without threads. See [Godot web audio](https://docs.godotengine.org/en/4.6/tutorials/export/exporting_for_web.html#audio-playback) and [audio streams](https://docs.godotengine.org/en/4.6/tutorials/audio/audio_streams.html).

Choose a path from required features and measured target behavior. Do not globally switch to Stream, enable threaded export, add cross-origin isolation headers, or change hosting solely to apply a generic mixing example. Use a per-player override only where the chosen release and task support it, and verify the mixed setup rather than assuming all controls or timings behave alike.

Prepared clips can supply fixed processing that the selected runtime cannot compute. Manual gain envelopes may provide a requested duck without a sidechain effect, subject to that mode's supported gain behavior. Do not report baked reverb as dynamic room acoustics or claim that every native stream type is available in Sample mode.

Browsers can restrict autoplay until a valid user interaction. Use the existing start/resume flow and handle a blocked start explicitly; a device connection or a programmatic action is not proof that audio is unlocked. Preserve saved mute/volume choices, and avoid turning every menu action into a new audio permission prompt. See [web autoplay guidance](https://docs.godotengine.org/en/4.6/tutorials/export/exporting_for_web.html#audio).

Handle page hiding, focus changes, and return using the project's pause policy. Do not replay all one-shots accumulated while the page was inactive. Reconstruct sustained loops from current state, retain their ownership, and avoid duplicate players after repeated resumes. Mobile browser work follows these web rules, including decode, memory, and startup costs on phones.

## Native desktop and mobile

For desktop exports, use the chosen OS/audio backend and device targets. Changing an output device or resuming from an interruption must not create a second music owner or reset user settings. Distinguish software timing from audible latency through an output device; the latter can change with routing and hardware.

For native Android/iOS, establish the requested suspend/resume and interruption behavior. Keep transient playback separate from persistent gameplay state. On interruption, release voices or duck requests according to ownership; on return, restore current loops without replaying stale alerts. Do not invent background audio, recording permissions, platform plugins, or an audio-session policy as part of an ordinary effects task.

Check exact lifecycle notifications and platform behavior for the project's release before coding them. Headphone, speaker, and Bluetooth behavior needs actual device evidence when required, not an assumption from the desktop editor. Adding native platform integrations remains separate work unless requested.

## Budget sources and assets

Assess simultaneous voices, repeated node allocation, compressed decoding, decoded buffers, streaming work, and effects. A small compressed file can still be expensive to decode repeatedly, and inaudible music stems can still consume resources. The backend matters: engine-mixed streaming and browser Sample decoding do not necessarily have identical costs.

Choose short-effect and long-loop formats from the actual import/playback path. Godot 4.6 documents WAV, Ogg Vorbis, and MP3 with different CPU/storage tradeoffs; do not assume provider Opus or raw PCM output is directly importable. See [audio import guidance](https://docs.godotengine.org/en/4.6/tutorials/assets_pipeline/importing_audio_samples.html).

Reduce redundant or inaudible playback using listener-relative audibility and gameplay priorities. Visual room culling alone is not an audio policy: a nearby offscreen machine may still need to be heard. Suspend an unneeded audio presentation without stopping the machine's authoritative simulation. Spatial attenuation alone does not prove that a silent source has stopped consuming all resources.

## Verification within authorization

When checks are authorized, select cases relevant to the change: missing buses/assets, exact mute and persistence, overlapping ducks, burst limits, loop stop/restart, scene teardown, blocked web startup, repeated suspend/resume, and the intended listener/output path. Use target devices for compatibility/performance claims and actual listening for mix/loop-quality claims.

Do not run a build, import, benchmark, script, browser/device test, or audio audition merely because this reference lists it. Keep static inspection, resource import, playback, listening, export startup, and profiling outcomes separate. Report unverified targets and assumptions without inventing measurements.
