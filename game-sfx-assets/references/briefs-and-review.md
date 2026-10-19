# Briefs and review

## Describe the audible event

Build the brief from the game's existing audio plan and the action the player must recognize. Establish the physical source, material, motion or mechanism, listening distance, acoustic space, and timing. Useful distinctions include a metal latch releasing versus a heavy door moving, a motor starting versus idling, and an electrical warning versus a mechanical failure.

Record which parts must synchronize with the game: attack/onset, repeating movement, sustained state, release, and decay. A continuous machine sound often needs separate start, running, and stopping assets or a defined loop and release. The finished sound's duration can differ from a provider's minimum generation duration.

Keep cue identity stable within an event family. Variation can change a take's texture without making a success sound resemble a damage warning. Follow the game's chosen realism or stylization rather than choosing cinematic processing for every cue. Do not promise that a sound-only cue is sufficient when the game also requires a visual or accessible alternative.

For a generation prompt, name the audible source and desired temporal sequence. Specify dry or reverberant perspective where it matters, and unwanted content such as speech or music. Provider prompting is model-specific; no prompt wording guarantees the output.

## Choose a production route

| Route | Useful when | Decision to preserve |
| --- | --- | --- |
| Existing library | Suitable takes already exist | Source identity, asset license, permitted edits |
| Recording and editing | A real material/action can be captured | Original recording and capture perspective |
| Local synthesis | Electronic, tonal, noise, or stylized cues suit the design | Parameters, seed when available, editable source |
| Cloud generation | The authorized provider can supply a useful candidate | Provider/model, prompt, usage, review status |

Choose the route from the task, available tools, and desired sound. Do not install a local model or sign up for a provider just because this skill mentions it. A library license, model license, provider terms, and the skill's instruction license are different records; do not infer audio rights from the skill's MIT file.

## Compare candidates

Use a small, scoped batch and change one useful dimension between candidates, such as material weight, perspective, rhythm, or processing. Preserve labels and filenames while the user chooses. If a candidate changes, give it a new identity so an old selection cannot silently point to a replacement.

Compare onset, unwanted speech/music, background contamination, tail length, repeat fatigue, cue identity, and loop continuity where relevant. For a fair comparison, keep the audition gain consistent enough that louder does not automatically appear better; retain gain adjustments as separate notes. A solo audition does not establish clarity in the game's full mix.

Use a local audio link or existing audition surface when available. Creating a comparison site or publishing files externally is a separate action, not a prerequisite for reviewing two takes. If playback is unavailable, provide the files and describe the outstanding listening check without inventing an assessment.

## Retain useful records

For multi-asset delivery, keep stable asset IDs and enough information to reconstruct the choice: event/trigger, variant, creation method, source or provider/model, prompt/parameters or synthesis source, original path, selected path, edits, loop behavior, delivery format, gain, provenance, and review status. Record usage and seeds only when the provider returns or supports them.

Reuse the project's asset metadata conventions. Include required credits and source/license links where appropriate. Do not introduce a database, tracker, manifest schema, or mandatory sound-design document for a narrow request. Mark unresolved selection and rights information explicitly rather than filling it with guesses.
