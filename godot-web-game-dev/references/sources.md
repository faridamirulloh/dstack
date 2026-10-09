# Sources and adaptation

Reviewed source: [vl4dt/godot-skills](https://github.com/vl4dt/godot-skills/tree/1ae280d35f2cc3931d4d8eb63f4bac82d6acf4a0), commit `1ae280d35f2cc3931d4d8eb63f4bac82d6acf4a0`.

The repository's 22 skills were reviewed. This draft rewrites the guidance rather than importing its examples, hooks, trackers, MCP bridge, or execution workflows. The source copyright and MIT permission notice are preserved in [LICENSE](../LICENSE).

Core topics retained: architecture, code review, GDScript, input, inventory, performance, physics, project setup, saves, state management, and UI. Selected animation, audio, brainstorming, debugging, dialogue, headless/export, localization, and shader ideas appear only where needed. Networking, C#, and claimed future-version migration advice are deferred.

Corrections include separating state from visual scenes, respecting browser renderer limits, checking API/version evidence, transactional inventory changes, bounded migrations, preserving input event detail, and replacing universal asset limits with project budgets. Broken upstream reference links and unverified code were not carried into the draft.

Authoritative entry points include the [Godot manual](https://docs.godotengine.org/en/stable/), [web export guidance](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html), [GDScript reference](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_basics.html), and [renderer comparison](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).

Documentation links follow `stable` unless a version is explicit. Select the target release when applying them. This instruction-only draft was prepared on 8 October 2026; it contains no executable helpers and does not claim runtime validation.

Desktop and mobile browser controller guidance was added on 9 October 2026 in [browser controller input](browser-controller-input.md), using linked Godot 4.6 documentation/source and MDN Gamepad API documentation. It covers automatic defaults, browser exposure limits, shared device identifiers, saved overrides, connection ownership, analog controls, controller UI, and touch coexistence. Version-specific behavior must be checked again for a different project release. This addition contains instructions only; no game implementation or browser/controller runtime checks were performed.
