# Persistence

## Define the saved state

Separate content definitions, mutable campaign state, and transient presentation. Persist stable IDs and plain values for entities, item instances, equipment-slot references, charge/damage, repairs, jobs, and other approved persistent rules. Do not serialize the active scene tree as the complete world model.

Save each piece of authoritative state once. Multiple views or scenes must not each serialize their own copy of the same inventory or job. Keep a schema version, and define which content/version changes a save can survive.

Choose JSON or another supported format according to the schema and existing project conventions. JSON does not preserve every Godot type automatically; encode vectors, enums, and references deliberately and validate the decoded types. Treat a save as data rather than executable resources from an untrusted location.

## Commit and recover

Capture a consistent snapshot at a transaction boundary. A save taken after consuming materials but before recording job completion can duplicate or lose outcomes on load. Include pending reservations and the state required to resume or cancel them.

When the storage backend permits it, write a temporary file, close/check the write, validate the candidate, and replace the active save with a recoverable backup strategy. Do not call a rename sequence universally atomic or claim that a filesystem operation alone makes browser persistence durable.

Keep the previous valid save until the replacement is known to be usable. Check I/O results. Validate a backup before using it, and do not delete the current file merely because a load attempt failed. Present a recoverable error when possible.

Load into a candidate state before replacing the current session. Check required fields, value ranges, duplicate IDs, missing content definitions, invalid ownership, equipment references, and job dependencies. Reject or repair invalid data according to an explicit policy, without partially applying it to live state.

## Migration

Use explicit migration steps that advance from one known schema version to the next. After each step, verify that the version advanced. Unknown future versions need an incompatibility result, not a loop that repeatedly tries the same migration.

Preserve the original save while migrating. A removed item definition, renamed room, or changed job type needs a defined mapping or a reported incompatibility. Migration is not permission to rewrite unrelated content or silently grant replacement equipment.

## Browser behavior

Use `user://` through the engine's supported file APIs. Browser storage is managed by the browser and origin; it can be unavailable, denied, private-session-only, cleared, or evicted. Follow the target Godot release's persistence and synchronization behavior. [Web filesystem guidance](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html#using-cookies-for-data-persistence).

Distinguish a successful application-level write from confirmed persistence through the browser backend. Do not promise that closing the tab immediately after a write is safe. Save during normal play at meaningful boundaries, rather than relying only on an asynchronous unload handler. Explain storage failure in the UI when the game depends on resumability.

Define hidden-tab, pause, quit, and resume behavior from the game design. Preserve game time separately from wall-clock time. If offline progress is approved, specify its limits, job prerequisites, clock handling, and exactly-once completion behavior; otherwise do not advance the campaign from elapsed real time.

An optional download/upload save feature can improve recovery but is a product decision. Do not add it without scope. Likewise, cloud saves, authentication, and networking are separate work.

## Meaningful checks when authorized

Use representative saves and failure cases relevant to the change: a pending job, a transferred/equipped item, a scene that is not loaded, an older supported schema, or a failed write. Compare authoritative state and ownership after resume. A check that only opens a file does not verify correct recovery.

Read the selected release's [FileAccess](https://docs.godotengine.org/en/stable/classes/class_fileaccess.html) and [DirAccess](https://docs.godotengine.org/en/stable/classes/class_diraccess.html) APIs before adding file operations. Report any browser persistence checks that were not performed.
