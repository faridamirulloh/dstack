# Touch and layout

## Translate gameplay actions deliberately

Start from movement, aiming, interaction, tools, menus, and other approved actions. Decide which actions must work simultaneously and which fingers own them. A mouse-aimed game needs an explicit mobile aiming choice; an automatic targeting mechanic changes game design and needs scope.

Use a suitable control layout such as separate movement/aim regions, direct tapping, dragging, or contextual controls when supported by the brief. Keep visual controls, touch hit areas, and action processing separate so reach/readability can improve without changing the simulation.

Account for thumbs obscuring the play area, handedness, device grip, and interruption. Support repositioning/scaling, accessibility, or haptics when requested and supported. Touch targets should be judged by physical usability rather than one universal pixel size.

## Multitouch ownership and cancellation

Track each active finger by its touch index. A movement finger must not steal aim or menu input when a second touch begins. Keep press, drag, release, and canceled-touch behavior consistent, including gestures that leave their original control region.

Use `InputEventScreenTouch`/`InputEventScreenDrag` fields appropriate to the target Godot release. Touch positions are supplied in the receiving viewport's coordinates; convert correctly for nested/scaled controls and gameplay projection. Avoid comparing these directly with unrelated world or physical-screen coordinates. [Touch event API](https://docs.godotengine.org/en/stable/classes/class_inputeventscreentouch.html).

Clear transient finger/action state on cancellation, lifecycle loss, replaced screens, and orientation/layout changes when ownership becomes invalid. Releasing one finger must not release an unrelated finger's action.

Route UI capture deliberately so a tap on a menu/button cannot also trigger a world tool. Mouse-from-touch and touch-from-mouse emulation can generate a second input path; choose which path is authoritative instead of processing both blindly. [Input examples](https://docs.godotengine.org/en/stable/tutorials/inputs/input_examples.html).

## Safe areas and adaptive layouts

Choose allowed orientations and gameplay framing from the brief. Keep controls and required information clear of cutouts, system bars, and gesture regions using documented safe-area/platform APIs. Convert the returned coordinate space into the UI's viewport/canvas space rather than assuming all values use one scale. [DisplayServer](https://docs.godotengine.org/en/stable/classes/class_displayserver.html#class-displayserver-method-get-display-safe-area).

Recalculate layout on relevant size/orientation changes. Use containers/anchors, sensible minimum sizes, text scaling, and appropriate stretch behavior. Display resolution, internal 3D render resolution, and UI density are different controls.

Do not change the gameplay camera's visible reach silently to fill a different aspect ratio. Choose an approved crop, letterbox, or expanded-view policy. Verify aim coordinates remain aligned with that camera and viewport.

Keep important information readable across supported phone/tablet sizes and text languages. Test focus/virtual-keyboard effects only when execution is permitted; account for the keyboard obscuring fields and for dismiss/back behavior when text entry is in scope.

## External controllers and system navigation

Controller support is optional but must use the same gameplay action model when added. Preserve analog magnitude, dead zones, reconnect behavior, and complete remapping data. Define UI focus and prompts without assuming a touchscreen is always the active input.

Android Back/system navigation should follow the screen/menu hierarchy and the approved quit policy. Do not make a navigation gesture consume an unrelated gameplay action. Check the selected release's back-request setting/notification behavior through [mobile platform guidance](mobile-platforms.md).

## Usability evidence when authorized

Use a representative physical device for simultaneous movement/aim/tool input, thumb occlusion, cancellation, safe areas, rotation, and system gestures as relevant. Mouse emulation can diagnose logic but cannot establish multitouch comfort or physical reach.

Report the inspected device/orientation/layout and any assumptions still pending. A desktop screenshot does not establish safe-area or touch usability on every supported device.
