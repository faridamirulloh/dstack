# Mobile platforms and lifecycle

## Suspension, termination, and resume

Define foreground, focus, pause, suspension, resume, and cold-start behavior from the game design. On Android/iOS, the OS can terminate a suspended application; an orderly desktop-style quit callback is insufficient. Use supported application lifecycle notifications and save at normal gameplay boundaries. [Quit/lifecycle guidance](https://docs.godotengine.org/en/stable/tutorials/inputs/handling_quit_requests.html).

Keep suspension handling bounded: save a consistent compact snapshot where appropriate, clear transient touch/drag state, and pause or release resources according to supported platform behavior. Do not begin a long dependency chain at suspension or assume an asynchronous operation must finish before termination.

Resume and cold start are different paths. Restore owned items, reservations, pending jobs, and completed effects from authoritative saved state. Reconnect views and necessary services without replaying rewards or consuming materials twice.

Choose whether game time pauses during backgrounding. Offline progression needs explicit limits, clock/prerequisite handling, and completion rules; it is not an automatic consequence of a suspended process. Do not add an OS background service to implement ordinary in-game jobs without separate scope.

Handle audio interruption/device changes and permission flows according to the supported platform API. An interrupted touch gesture must not leave a tool or movement action stuck after resume.

## Android preparation

Read existing export presets. Establish package identity, version fields, supported architecture/device tier, intended APK/AAB distribution, permissions, icons, orientation, renderer, and any native plugin requirements. Distinguish development deployment from a release package. [Android export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_android.html).

Check the exact Godot release's export templates, Java/Android SDK, and Gradle/custom-build requirements. Do not hardcode a permanently current SDK/JDK version, install a toolchain, or force a custom build when the existing feature needs neither.

Preserve release signing identity for existing apps. Signing mismatches can prevent an update; uninstalling the installed app can remove data and needs separate authorization. Keep signing secrets out of output and shared source files.

Use only permissions needed by an approved feature and handle denied/revoked permission behavior. Internet access, notifications, sensors, storage access, and background work must not be added by default. Store policies and target-API requirements change; verify current official platform requirements when preparing a real submission.

Handle Android Back using the selected release's quit-on-back setting and back-request notification where relevant. Prefer the approved navigation hierarchy; do not accidentally exit while closing a menu or text-entry screen.

## iOS preparation

The documented native iOS export workflow requires a macOS host with Xcode and matching Godot export templates. Establish the actual available host/toolchain before promising an iOS build. A Windows-only environment can still edit shared game code, but cannot complete that documented build path. [iOS export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_ios.html).

Read existing bundle identity, team/signing/provisioning configuration, version fields, architecture/device targets, orientations, capabilities, and native plugins. Preserve identifiers and signing identity for existing apps. Do not change developer-account settings or publish as a side effect of a gameplay task.

Verify simulator renderer/architecture restrictions for the selected release. Simulator or Apple Silicon desktop execution does not establish physical iPhone/iPad behavior, touch usability, thermals, or memory pressure. Distinguish creating an Xcode project, signing/building it, device deployment, and distribution in reports.

Use capabilities/usage descriptions only for approved features. Check current Apple requirements when distribution work is requested. Keep credentials and private signing material out of logs and shared source.

## Plugins, services, and language

Confirm C# support and limitations for the actual Godot/platform release before relying on it. Existing language choice does not authorize a rewrite to GDScript or a different engine version.

Ads, IAP, achievements, platform accounts, notifications, cloud saves, analytics, and crash reporting require explicit feature scope and compatible plugins/backends. Establish availability and lifecycle/denial behavior rather than treating a plugin's presence as a complete integration.

## Checks when authorized

Select physical-device/export checks relevant to the change: startup, simultaneous touches, safe areas, back navigation, pause/resume, termination recovery, offline behavior, permission denial, or sustained scene performance. Avoid a mandatory build/test suite for every edit.

Report engine/templates, host, OS/device, renderer, package mode, and the actions actually performed. Identify targets or lifecycle paths that remain unverified; an exported package alone does not prove their behavior.
