# Sources and adaptation

This skill adapts the reviewed local `godot-web-game-dev` architecture and asset guidance into a self-contained native Android/iOS workflow. It does not depend on installing the web or PC skill.

Original reviewed source: [vl4dt/godot-skills](https://github.com/vl4dt/godot-skills/tree/1ae280d35f2cc3931d4d8eb63f4bac82d6acf4a0), commit `1ae280d35f2cc3931d4d8eb63f4bac82d6acf4a0`. The upstream copyright and MIT permission notice remain in [LICENSE](../LICENSE). Upstream hooks, trackers, MCP bridge, mobile/IAP defaults, unverified examples, and mandatory execution workflows are not included.

Platform guidance was prepared against official Godot documentation on 8 October 2026. Entry points include [renderer comparison](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html), [Android export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_android.html), [iOS export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html), [lifecycle/quit handling](https://docs.godotengine.org/en/stable/tutorials/inputs/handling_quit_requests.html), [touch events](https://docs.godotengine.org/en/stable/classes/class_inputeventscreentouch.html), [DisplayServer](https://docs.godotengine.org/en/stable/classes/class_displayserver.html), and [GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html).

Select the actual project release when applying engine documentation; `stable` can move. SDK, signing, and app-store requirements need current official platform verification when preparing a real distribution. This instruction-only skill includes no executable helpers and makes no claim of device/runtime validation.
