# Architecture and state

## Lifetimes and ownership

Use scenes for composed presentation and interaction, resources for reusable content definitions, and a state owner whose lifetime matches the game state. Follow existing conventions rather than creating an autoload for every subsystem.

Separate an item definition from a particular owned item. A definition describes reusable properties; an instance holds charge, damage, installed parts, or other mutable values. Avoid mutating a shared resource when the change belongs to one entity.

Use stable application IDs for persistent bodies, items, machines, rooms, and jobs. Scene paths, node instance IDs, and display names are unsuitable as durable identity. Resolve IDs through a state registry; let visual nodes attach and detach from that state.

Signals communicate completed changes. Define who owns each connection and how it ends when a view is replaced. Retain a callable when an anonymous subscription needs explicit removal. Cancel or replace stale tweens and delayed callbacks when their state is superseded.

If programmatically packing a scene, assign the intended scene owner to children that must be saved. Runtime parenting alone does not establish packed-scene ownership. Preserve inherited scenes and shared resources unless the task calls for changing their source.

## Persistent simulation and visual activity

When jobs or machinery must continue outside the camera, make their state transitions independent of `visible`, screen-notifier signals, and visual scene availability.

Keep three decisions separate:

- Whether the entity's visuals are drawn or animated.
- Whether detailed local physics or navigation is currently needed.
- Whether its authoritative job, energy, inventory, hazard, or repair state must advance.

Offscreen work may use a cheaper model only if it preserves the game's rules. A job moving through dangerous rooms cannot become a timer that ignores blocked routes, hazards, tools, or energy. Define a coarse model explicitly, retain its prerequisites, and reconcile it when the visual entity becomes active.

Choose a game-time clock, update cadence, and pause policy from the design. Browser background throttling, scene unloading, and application pause are different from elapsed game time. Do not invent offline progress or wall-clock catch-up because a tab was hidden.

Store pending jobs and committed results in state. On reattachment, build presentation from current state; do not replay a completion in a way that consumes materials, grants rewards, or reports an event twice.

## Input, camera, and movement

For desktop and mobile browser controller support, use [browser controller input](browser-controller-input.md). It covers automatic defaults, device assignment, saved remaps, analog controls, and coexistence with keyboard/mouse or touch.

Use InputMap actions and the project's event-routing conventions. Consume UI interactions before world actions where appropriate so a button click cannot also fire a tool. Capture remapping events deliberately, excluding releases and repeated key echoes when they are not bindings.

Preserve physical key versus logical key, modifiers, mouse buttons, controller axes, device policy, and dead zones when saving bindings. Do not reduce every event to one integer.

Keep analog magnitude when the controls support it. `Input.get_vector()` provides a bounded vector; normalizing every nonzero result removes analog speed information. Map movement into the chosen camera/world basis, rather than assuming world X/Z always matches the screen.

For mouse aiming in 3D, project a camera ray onto the relevant gameplay surface or use a scoped physics query. Handle no hit, invalid planes, and UI capture. Keep aiming rules separate from what the renderer can draw.

Use `CharacterBody2D`/`CharacterBody3D` for scripted character motion and the appropriate physics callback. Their velocity properties are inherited. In Godot 4, `move_and_slide()` uses that velocity and returns a collision boolean; confirm other movement APIs against the selected release.

Collision layers and masks belong to collision objects, not their shape resources. Set query masks deliberately. Supply the exclusion type the query requires, including RIDs where required. Prefer simple authored collision over detailed render geometry for moving actors.

## Inventory, equipment, and transactions

Compute an operation's validity before committing it. For example, a purchase must establish payment and complete inventory capacity, then change both together. If the destination cannot accept the item, no currency is removed.

For stacking, fill compatible existing stacks before allocating new slots. Respect both stack capacity and remaining slot capacity. Define whether partial acceptance is permitted; return an explicit result and accepted quantity rather than silently losing the remainder.

Transfers preserve instance identity and have one current owner. Equipment slots reference owned instance IDs and validate compatibility. An equipped flag alone is insufficient to reconstruct which slot holds which part.

Crafting and automatic jobs may reserve materials or equipment. Keep reserved and consumed quantities distinct, define interruption behavior, and commit once. Prefer one transaction boundary for related inventory, battery, ownership, and job changes.

## State machines and UI

Use a small explicit state machine when transitions and cancellation matter. Define entry, exit, allowed transitions, and the owner of transition requests. Do not assume a concrete state can call a transition method on an arbitrary parent node.

For a behavior tree, define success, failure, and running semantics. A selector stops on running as well as success. Reset action-local timers when the action re-enters; a resumed action needs an explicit resume policy.

UI observes state and submits commands. It should not become the only owner of inventory, charge, repair progress, or mission completion. Rebuild views from state after scene changes.

Use containers, anchors, theme APIs, and control size flags that exist in the project's release. Account for browser resizing, scaling, focus, and input capture. Keep user-facing errors actionable; logs can carry technical details.

## Version references

Use the matching version of the [GDScript reference](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_basics.html), [CharacterBody3D API](https://docs.godotengine.org/en/stable/classes/class_characterbody3d.html), and [Input API](https://docs.godotengine.org/en/stable/classes/class_input.html). These links follow `stable`; select the project release before relying on a symbol or setting.
