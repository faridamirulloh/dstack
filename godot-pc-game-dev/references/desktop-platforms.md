# Desktop platforms

## Keyboard, mouse, and controller

Use action-based gameplay controls. Preserve logical/physical key choice, modifiers, mouse buttons, gamepad buttons/axes, and dead zones when remapping or saving bindings. Keep analog movement magnitude and deliberate aim sensitivity.

Provide navigable UI focus where controller/keyboard support is in scope. Switching input devices must not lose focus or trigger both a UI command and a world action. Update prompts from the current device without repeatedly stealing focus from a pointer user.

Handle controller connection/disconnection and ambiguous mappings according to the multiplayer/device policy. A transient device index is not a durable player identity. Preserve complete bindings and handle missing devices without leaving an action held.

For captured mouse controls, define capture, release, pause/menu, focus-loss, and recapture behavior. Clear transient held/drag state when its lifecycle ends. Select a documented mouse-motion representation and sensitivity policy; do not multiply event-relative motion by frame delta without a reason.

## Windows, display modes, and focus

Use supported window/display APIs for the project's release. Distinguish window size, viewport/internal resolution, UI scaling, and physical display pixels. Account for resizing, aspect ratios, high DPI, monitor changes, and restored window positions without altering gameplay reach unintentionally.

Display-setting changes should leave a usable path back if a monitor or mode is unavailable. Store preferences separately from campaign saves. Keep remembered window coordinates within an available display when restoring them.

Define focus-loss/minimize behavior explicitly. Pause policy, audio behavior, mouse capture, cosmetic updates, and authoritative simulation are separate choices. Avoid full-speed background rendering when it adds no useful behavior, particularly on laptops and handheld PCs.

Handle window-manager close requests through the game's supported quit/save path. Preserve Godot's automatic-quit behavior unless the task requires interception; if intercepted, ensure save success/failure and the actual exit have a defined outcome. [Quit requests](https://docs.godotengine.org/en/stable/tutorials/inputs/handling_quit_requests.html).

## Common native export decisions

Read existing export presets before modifying them. Establish the target OS/architecture, Godot/templates version, language/runtime requirements, output location, icons, identity/version fields, and external native extensions or libraries. Export-template use and compiling the engine are distinct workflows.

Keep saves/settings under `user://` or the existing approved user-data location. Do not assume the executable's directory is writable. Resource lookup must survive different working directories, packaged paths, and filename case sensitivity.

For GDExtension or other native dependencies, ensure the requested target OS/architecture has compatible binaries. Do not infer portability from the main Godot executable. Preserve the existing plugin/runtime version contract.

## Windows

Choose the intended architecture and packaging strategy. Preserve the relationship among the executable, PCK, and any required libraries. Treat signing as a separate distribution operation requiring the available certificate/tooling and task authorization. [Windows export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html).

## Linux

Confirm target architectures/distributions and any native dependency assumptions. Preserve executable permissions and resource paths in the chosen package. Pay attention to filename case when assets were authored on another OS. A successful export on Windows does not verify startup on Linux. [Linux export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_linux.html).

## macOS

Preserve the app bundle's required structure, executable permissions, identifier, architecture support, and relevant entitlements. Packaging from Windows can affect executable permissions; follow the documented archive/export path for the chosen release. [macOS export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_macos.html).

Code signing and notarization depend on the distribution route, host tools, and credentials. Identify requirements when asked to prepare distribution, without changing machine trust/security settings or publishing a release as a side effect. Never print private signing material.

## Integrations and checks

Storefronts, achievements, cloud saves, launchers, multiplayer transports, crash reporting, and mod loading are optional systems with their own compatibility and product decisions. Do not install a platform SDK merely because a project targets desktop.

When runtime/export checks are authorized, select checks relevant to the change: native startup, resource references, display/focus transitions, controller reconnect, save locations, or included native dependencies. Record the actual OS/architecture and any target not checked.
