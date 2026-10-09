# Browser controller input

Use this guidance for gamepad support in Godot web exports on desktop and mobile browsers. Automatic mapping should make a supported controller usable without setup, while preserving manual overrides and another usable input method. This is an implementation workflow, not a claim that every browser exposes every controller.

## Establish browser access

Record the target browser/OS versions and relevant controller families, including wired or Bluetooth connections where applicable. Treat Android and iOS browsers as web targets. Godot 4.6 uses a different controller backend on native desktop; desktop-editor behavior alone does not establish web behavior. See the versioned [controller guide](https://docs.godotengine.org/en/4.6/tutorials/inputs/controllers_gamepads_joysticks.html).

Use HTTPS for deployed controller support. Detection can require the player to press a controller button with the game page visible. Provide a short activation prompt and a keyboard/mouse or touch path while waiting. A click on the page does not prove that a connected gamepad has been exposed. Browser restrictions and controller mappings are described in [Godot web export guidance](https://docs.godotengine.org/en/4.6/tutorials/export/exporting_for_web.html#gamepads).

If the game is embedded, consider the host's `gamepad` Permissions Policy when access is blocked. Support for this directive varies; do not add headers or iframe attributes blindly. A child frame cannot bypass restrictions imposed by its host. See [MDN's gamepad policy documentation](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Permissions-Policy/gamepad).

## Map hardware to actions automatically

Keep hardware layout mapping separate from gameplay bindings. Hardware mapping translates exposed controls into logical buttons and axes; gameplay bindings assign those controls to the game's semantic actions. Extend the existing action catalog rather than creating a parallel gameplay path for controllers.

Resolve hardware layout first. Prefer Godot's mapped controls for a recognized device or browser standard layout. Apply a project-supplied hardware correction only when its identity and mapping are established for the target combination. For unknown or incorrect layouts, offer manual binding and calibration from events the engine actually receives. Keep the alternate input path available.

Then resolve action bindings: restore the player's saved bindings and calibration for a compatible selected profile; otherwise apply the game's defaults automatically. Repeat this resolution on startup and connection changes without overwriting player choices. Hardware corrections must not reset action bindings.

Derive a complete default binding table from the game's required gameplay and menu actions. Use familiar stick, D-pad, face-button, shoulder, and trigger assignments where they suit the existing design. Give pointer-dependent interactions a usable controller path. A detected controller with only movement mapped is incomplete support if the player still needs a mouse for required actions.

Use Godot's `JOY_BUTTON_*` and `JOY_AXIS_*` constants for mapped devices. Browser `Gamepad.mapping === "standard"` describes the browser layout; an empty mapping means it has not standardized the layout. Do not copy raw JavaScript button indices into Godot bindings: engine conversion can change their meaning. See [MDN's mapping property](https://developer.mozilla.org/en-US/docs/Web/API/Gamepad/mapping).

Use `Input.get_connected_joypads()` and `Input.joy_connection_changed` for the device lifecycle. `Input.is_joy_known()` indicates mapping knowledge, not successful support for every game action. Unknown devices can still produce events. `Input.add_joy_mapping()` accepts SDL2-format hardware mappings, but use it only when the selected web backend and exposed identity can apply that mapping. It cannot recover controls the browser withholds. Check the project's release of the [Input API](https://docs.godotengine.org/en/4.6/classes/class_input.html).

Do not infer a unique model from a GUID or display name. In Godot 4.6, `get_joy_info()` is empty on Web. The web backend can use the shared `standard` GUID, or a fallback identity without vendor/product data. Avoid applying a custom hardware correction to all devices with that shared identity. This behavior is visible in the [Godot 4.6 web input source](https://github.com/godotengine/godot/blob/4.6/platform/web/js/libs/library_godot_input.js); recheck it when the engine version changes.

Prefer Godot's input path. Add a scoped JavaScript bridge only for a demonstrated missing browser capability. Do not poll and inject the same controls through both Godot and JavaScript. Avoid UA or controller-name guesses as the sole basis for behavior.

## Own connections and input state

Enumerate exposed controllers at startup and reconcile them on connection changes and return from a hidden or unfocused page. Device indices are connection-local and may be reused after a disconnect; they are unsuitable as save keys. Browser exposure and index behavior are documented in [MDN's Gamepad API guide](https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API/Using_the_Gamepad_API).

Keep device assignment separate from prompt selection. For one player, choose an active controller by deliberate input or the project's existing policy. A newly connected device must not silently take control. For multiple players, retain per-player filtering and explicit joining; unrestricted InputMap bindings can combine all controllers.

On disconnect, focus loss, page hiding, or scene teardown, clear that source's cached held buttons, analog values, and touch pointers. Reconcile current input before resuming gameplay, using the game's pause/resume policy. Avoid carrying a held fire command across a reconnect. When sources share an action, combine their contributions deliberately so releasing one source does not cancel another source that is still holding it. Do not indiscriminately call `action_release()` for every action.

## Preserve analog controls and player remaps

Apply configurable dead zones and keep stick magnitude when the game supports analog movement. Map stick vectors into the chosen camera/world basis. Treat pointer aiming and stick aiming as distinct input conversions into the same gameplay command; preserve the project's aim and camera rules. For directional aiming, define what happens when the stick returns to center. Do not introduce aim assistance or change movement speed without design authorization.

Handle positive and negative axis directions, trigger rest/range differences, inversion, sensitivity, and digital thresholds explicitly. During remapping, wait for a neutral state, accept deliberate button presses or axis excursions, and exclude drift, releases, and repeated events. Suppress gameplay while capturing a binding, and require release before the captured control can activate its new action. Show conflicts and provide a cancel path and reset to defaults.

Persist action bindings separately from hardware profiles, retaining event type, button/axis, axis direction, device policy, dead zone, and calibration settings. Store a versioned profile key and enough layout/context metadata to judge reuse. When identity is ambiguous, let the player choose a profile instead of treating all unknown devices as one model. Reconnect and automatic defaults must not overwrite saved choices. Use the skill's [persistence guidance](persistence.md) for browser storage behavior.

## Support menus and mixed input

Make required menus usable with a controller, including settings, remapping, pause, and modal dialogs. Give the initial control focus, define usable focus neighbors, and restore focus when a menu closes or its focused node disappears. Keep UI navigation actions separate from gameplay actions and prevent a menu confirmation from also firing a gameplay tool. Follow [Godot's controller navigation guidance](https://docs.godotengine.org/en/4.6/tutorials/ui/gui_navigation.html).

Switch prompts on meaningful recent input, with a threshold that excludes stick drift. Render prompts from the effective bindings after remapping. Use family-specific glyphs only with reliable evidence or a player-selected glyph set; otherwise show generic positional labels. Do not assume Xbox, PlayStation, and Nintendo face-button labels are interchangeable, or alter the game's accept/cancel convention merely to match a guessed brand.

On desktop browsers, preserve keyboard/mouse use alongside controllers. On mobile browsers, retain touch controls as a usable fallback and allow deliberate switching between touch and a Bluetooth/USB gamepad. Hide touch controls only according to the project's policy after usable controller input, with a way to restore them. A connection notification alone does not prove the controller can operate the game.

Track touch pointer ownership and cancellation, support the simultaneous touches the game requires, and avoid firing an action twice through touch-to-mouse emulation. Keep required controls reachable when the viewport, orientation, or browser chrome changes. A supported gamepad must not make touch-only settings inaccessible.

## Verification within authorization

When runtime checks are authorized, use the intended web export and actual target browser/OS/controller combinations. Relevant cases include activation after load, connection before load, unplug/reconnect, index reuse, two controllers, analog drift, triggers, saved remaps, menu focus, tab hiding/resume, and mobile touch/controller switching. Inspect embedding or HTTPS failures where applicable. Report which combinations were checked and which remain unknown.

Do not run exports, browser tests, helper scripts, or device checks without task authorization. Static instruction edits do not establish working controller support in a game. Report any unverified mapping assumptions rather than claiming universal compatibility.
