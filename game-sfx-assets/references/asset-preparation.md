# Asset preparation

## Preserve sources and timing

Keep the original recording, synthesis source, or provider output. Edit a separate working copy and export chosen delivery variants to intentional paths. Conversion cannot restore information lost in a compressed original; record that limitation instead of labelling a converted WAV as a lossless master.

Trim unwanted silence while preserving the attack and decay the game needs. A click, impact, or repair cue should begin at its intended event time, not after an accidental lead-in. Use fades where edits introduce discontinuities; do not blunt every transient or cut every decay to the same length.

Assess clipping and noise before boosting gain. Peak normalization, perceived loudness, and gain in the game mix are different decisions. Leave room for overlapping voices, and avoid prescribing one LUFS value for every short effect or normalizing every asset to full scale. If analysis or listening tools are unavailable, report that the level has not been checked.

## Make loops usable

Match the loop's beginning and ending in waveform continuity and audible texture. A zero crossing can reduce an edit click but does not guarantee a convincing ambient loop. Check repeated wraps, the perceived repeating rhythm, and changes in noise or decay across the seam.

Edit loop points or prepare a crossfade only when that fits the engine's loop support and the sound's intended behavior. Preserve separately authored start/stop phases where the machine's action needs them. Do not bake a fade-in/out into every running loop; that can create an obvious dip on every repetition.

A provider's loop option produces a candidate, not proof of a seamless engine loop. Set or describe the engine's loop behavior separately. Check whether loop markers use samples or seconds for the resource being delivered. Keep engine import edits within the requested scope.

## Prepare variation and processing

Supply distinct takes when repetition exposes a sound's identity. Runtime pitch or gain variation can supplement them but also changes timbre, duration, or perceived meaning. Avoid randomizing warnings so widely that players cannot recognize them. Keep dry assets when room effects or distance will be controlled in the engine; baked processing can be useful for a fixed perspective or a browser mode without runtime effects.

Do not combine unrelated cues into one file if they must be triggered or mixed separately. Conversely, avoid splitting an inseparable mechanical gesture into many simultaneous voices without a gameplay reason. Retain an editable processing recipe when the task produces a family of effects.

## Choose engine-compatible delivery

Check the actual engine release, decoder/import support, resource type, and target platform before converting. Match the extension to the container. Raw PCM needs its sample format, rate, channels, and a proper container or an explicit raw-data path; renaming it to `.wav` is not conversion. An `.ogg` container alone does not establish that its codec is Vorbis.

Godot 4.6 documents WAV, Ogg Vorbis, and MP3 imports. WAV is useful for short, frequently repeated effects, while compressed long ambience/music trades smaller files for decoding work. Select using voice count, memory, download size, and the actual playback path; do not impose Ogg for everything or reject MP3 categorically. These are release-specific entry points, not evidence for every export configuration. See [Godot audio import guidance](https://docs.godotengine.org/en/4.6/tutorials/assets_pipeline/importing_audio_samples.html).

Mono can suit positional one-shots; stereo can preserve an authored ambient bed. Choose according to the spatial design instead of reducing every asset to mono. Retain adequate source quality and select delivery rate/compression from the sound content and device budget. Account for decoded memory as well as compressed bytes.

## Deliver within scope

Give the engine task stable asset IDs/paths, format details, intended trigger, loop behavior, variants, gain notes, and any start/stop phases. Keep a distinction between files prepared for import, files actually imported, and files heard in the target runtime. Do not claim playback latency or performance from file size alone.

For Godot integration, use an available Godot audio/platform skill or the project's existing guidance. This reference does not authorize editor startup, imports, exports, or playback. State which editing, listening, loop, level, and engine checks were actually performed.
