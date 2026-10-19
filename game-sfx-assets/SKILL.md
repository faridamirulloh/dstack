---
name: game-sfx-assets
description: Create, source, compare, and prepare game sound effects, UI cues, foley, and ambient loops. Use for audio asset production and delivery; use engine guidance for runtime playback. ElevenLabs generation is optional.
license: MIT
---

# Game sound-effect assets

Turn a sound brief into reviewable audio assets and a usable engine handoff. Preserve the game's approved sound direction and the user's chosen creation method. This skill covers non-speech effects and ambience; music composition and voice cloning need their own scope.

## Establish the brief

Read applicable instructions and relevant existing sound plans, asset conventions, and gameplay requirements. Establish the sound's source/material, action, perspective, environment, intended trigger, one-shot or loop behavior, duration, variation needs, and target engine/platform. Ask for missing details only when they block a useful decision. A factory or robot example is not a requirement for another game.

Choose among existing licensed assets, recordings, local synthesis, and an available generation provider. Reuse suitable assets before adding tools. Do not assume an API key, MCP connection, local model, editor, or audio utility is installed. Generation credentials belong in development tooling, never in browser code, game exports, audio metadata, or user-visible logs.

Skill use does not authorize installing dependencies, buying credits, uploading recordings, changing engine import settings, or executing a render. Follow task authorization. An instruction-only update can finish without generating audio.

## Read the relevant guidance

| Task | Reference |
| --- | --- |
| Sound briefs, event families, candidate comparison, asset records | [Briefs and review](references/briefs-and-review.md) |
| Trimming, gain, loops, variants, formats, engine handoff | [Asset preparation](references/asset-preparation.md) |
| Authorized ElevenLabs generation, SDK differences, credentials, failures | [ElevenLabs](references/elevenlabs.md) |
| Attribution, license scope, and documentation evidence | [Sources](references/sources.md) |

## Produce and select deliberately

Agree on a bounded candidate batch using the task's existing authorization and cost limits. A request for one effect does not authorize an entire soundtrack or repeated unattended generation. Preserve originals and selected takes. Use new paths for alternatives and edits instead of overwriting a selected asset.

Describe sound mechanisms clearly: object, material, motion, microphone/listener perspective, acoustic space, and temporal structure. Separate a dry action sound from surrounding ambience when they need independent mixing or repetition. Keep gameplay-critical event families distinguishable and avoid adding music or speech to an effects brief unless requested.

Inspect or audition with available, authorized tools. Do not claim an audio file sounds correct from its filename, waveform, provider success response, or prompt alone. When listening is unavailable, deliver the candidate as unreviewed and identify the remaining decision. Installing this skill does not grant a new generation or playback tool.

Prepare chosen assets for the actual engine version and platform. Keep source quality separate from delivery settings. Raw PCM is not a WAV container; an API-supported format is not necessarily engine-importable. Avoid arbitrary loudness, sample-rate, compression, or duration rules for every game.

## Finish with a usable handoff

Deliver the selected files, remaining alternatives when useful, and the relevant trigger, loop, variation, gain, and format notes. For a collection or project handoff, retain provenance and selection metadata in an existing asset record or a scoped manifest. Do not add a new planning system for a single sound.

Report creation method, files changed, edits and checks actually performed, usage/cost information when available, and anything still unverified. Leave playback code and import changes to the requested engine task. Runtime generation, a hosted audio service, or a new middleware dependency is not implied by an asset request.

This skill adapts reviewed ElevenLabs guidance; see [sources](references/sources.md) and [LICENSE](LICENSE).
