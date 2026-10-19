# Sources and adaptation

Prepared on 10 October 2026 from a review of [ElevenLabs sound-effects](https://github.com/elevenlabs/skills/tree/main/sound-effects), its `SKILL.md`, and its installation reference. The upstream material is MIT-licensed; its copyright and permission notice are preserved in [LICENSE](../LICENSE).

This adaptation keeps text descriptions, one-shot/loop controls, provider parameters, and client choices. It adds scoped candidate review, source preservation, engine-compatible delivery, provenance, and bounded usage/failure handling. It corrects the upstream JavaScript Node `.pipe()` example and distinguishes JavaScript camelCase fields from Python/HTTP names. No upstream executable helpers or OpenClaw-specific provisioning metadata are imported.

Authoritative sources include the [ElevenLabs API](https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert), [JavaScript SDK implementation](https://github.com/elevenlabs/elevenlabs-js/blob/main/src/api/resources/textToSoundEffects/client/Client.ts), [CLI](https://github.com/elevenlabs/cli), and [Node streams](https://nodejs.org/api/stream.html). Godot delivery guidance uses the [4.6 audio import manual](https://docs.godotengine.org/en/4.6/tutorials/assets_pipeline/importing_audio_samples.html).

URLs tracking `main`, `stable`, or an unversioned service can change. Select the actual client/engine release and verify unfamiliar symbols or constraints when applying this skill. Instruction licensing does not establish rights to recordings, library assets, model weights, or generated outputs; retain their own applicable provenance/terms.

This package contains instructions only. Creation involved file inspection and documentation review, not generation, audio audition, SDK execution, dependency installation, or engine validation.
