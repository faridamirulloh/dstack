# ElevenLabs sound-effect generation

Use this reference only when ElevenLabs is selected and generation is authorized. It adapts [the official sound-effects skill](https://github.com/elevenlabs/skills/tree/main/sound-effects); its supported API and SDK behavior can change. Check the installed client version and current [sound-generation API](https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert) before relying on a parameter or output format.

## Use one available client path

Choose an existing CLI, Python SDK, JavaScript SDK, or a scoped HTTP client. Do not install all of them. Official setup guidance includes Windows options, but dependency installation is a separate action. Adapt shell syntax to the actual host; Bash `export` and backslash continuation are not PowerShell syntax. Read [installation guidance](https://github.com/elevenlabs/skills/blob/main/sound-effects/references/installation.md) and the selected client's documentation when setup is in scope.

Read credentials through the user's configured environment or credential mechanism. Never echo a key, place it in a prompt, hardcode it in a game project, or pass it into a browser export. Do not write a new credential file or change persistent shell settings for a generation request unless that setup is authorized. A missing credential blocks generation, not brief preparation or review of existing assets.

## Confirm request settings

As reviewed on 10 October 2026, the API uses `POST /v1/sound-generation`, a required `text` description, and the `eleven_text_to_sound_v2` model. The documented duration is 0.5 to 30 seconds or automatic when omitted/null; `prompt_influence` is between 0 and 1, with a documented default of 0.3. The loop option is documented for v2. Recheck these facts for the chosen service/client version. A shorter delivered UI cue may require editing a longer generated result.

Keep field naming specific to the client. Python/HTTP fields include `model_id`, `duration_seconds`, `prompt_influence`, and `output_format`; the JavaScript SDK uses camelCase fields such as `modelId`, `durationSeconds`, `promptInfluence`, and `outputFormat`. Do not blindly copy a Python request object into JavaScript.

Select a supported output format that the account and engine can use. Some formats are account-tier dependent. MP3 is a compressed delivery candidate; `pcm_*` means raw PCM rather than a WAV file. Do not treat the provider's Opus or telephony output choices as automatic Godot imports. Use [asset preparation](asset-preparation.md) for the handoff.

## Save the returned audio correctly

Consume the selected client's response according to its version. The reviewed Python pattern writes binary chunks from the conversion result. The reviewed JavaScript client returns a Web `ReadableStream<Uint8Array>`, not a Node stream with `.pipe()`. In Node, use a compatible Web-to-Node stream adapter with an awaited pipeline, or another supported binary consumer. Recheck Node/SDK types and methods before implementing. See [the current SDK client](https://github.com/elevenlabs/elevenlabs-js/blob/main/src/api/resources/textToSoundEffects/client/Client.ts) and [Node stream APIs](https://nodejs.org/api/stream.html).

Use an explicit output path and matching extension. Save to a new temporary/working file, await completion, and promote the completed result according to the filesystem's supported operations. Preserve a selected take and avoid returning a partial file as successful. Track request parameters and provider/request metadata when available, without credentials or sensitive headers.

## Bound usage and failures

Use the authorized candidate count, durations, and cost limits. Report actual usage/cost information returned by the provider; do not infer a bill from a fixed price copied into this reference. The API exposes a billing-related response header, but its presence does not establish the user's complete account balance or exact currency charge.

Treat authentication, invalid parameters, rate limits, and transport failures separately. Stop for authentication/permission failures rather than attempting alternate credentials. Correct an invalid request before resubmitting. Limit retries and honor rate-limit guidance; account for SDK automatic retry settings too. A timeout after submission can leave completion uncertain, so do not blindly regenerate a large batch and charge for duplicates.

Keep a selected file even when playback tooling fails. Report generation, file-saving, listening, and engine-import outcomes separately. The request's `loop` setting and a successful response do not establish audio quality or seamless playback. A generation permission does not authorize publishing audio, uploading reference recordings, or installing another service.
