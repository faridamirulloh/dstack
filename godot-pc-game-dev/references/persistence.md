# Persistence

## State and storage

Persist stable application IDs and plain state values. Keep content definitions, owned item instances, equipment-slot references, campaign changes, and pending jobs distinct from scene presentation. Save each authoritative value once and include a schema version.

Choose a supported format that fits the existing project. JSON needs deliberate encoding for engine-specific values and references. Load data into a candidate state rather than executing arbitrary serialized resources from an untrusted source.

Use `user://` or the project's established user-data policy. The installed game's directory, current working directory, and resource package are not reliable writable save locations. Application identity/user-data configuration can change where existing saves are found; preserve it during unrelated work. [Filesystem guidance](https://docs.godotengine.org/en/stable/tutorials/io/data_paths.html).

Keep graphics, audio, accessibility, and binding preferences independent of the campaign schema where their lifecycle differs. Preserve full input-event details when saving remaps.

## Consistent writes and recovery

Capture a consistent transaction boundary. Inventory ownership, reservations, consumed materials, battery state, and job completion must agree in the snapshot. Serialize active and offscreen entities from the same authoritative state model.

When the backend supports it, write and close a temporary candidate, check I/O results, validate it, and replace the active save while retaining a recoverable previous version. Define what failures leave on disk. Do not describe every multi-step rename/write sequence as crash-proof or universally atomic.

Keep backups until the replacement is known to be usable. Validate a backup before recovery. A failed load should not destroy the only existing save. Report recoverable errors without partially replacing live state.

Validate decoded types, required fields, ranges, duplicate IDs, missing definitions, ownership, equipment references, and pending-job prerequisites. Repair only under an explicit compatibility policy; otherwise retain the original data and explain the incompatibility.

## Migration and quit

Migrate one known schema version at a time and verify each step advances. Reject unsupported future versions explicitly. Keep the original save during migration, and define mappings for renamed or removed content.

Save at approved gameplay boundaries so recovery does not depend only on clean exit. If the quit path is intercepted, complete or report the save operation before exiting, with a defined cancellation policy. Crashes, power loss, or process termination may bypass orderly quit.

Resume from persistent state without replaying consumed materials, rewards, or job completion twice. Game time and wall-clock time are separate; offline progression needs explicit design rules.

## Optional cloud/mod behavior

Cloud saves require a requested backend, conflict policy, account identity, and compatible schema. Local versus cloud modification timestamps alone may not establish the desired campaign authority. Do not add a storefront or network dependency to implement ordinary local persistence.

Mods and user-editable saves require deliberate data-validation and content-version policy. A platform port must not silently invalidate existing saves or change ownership IDs.

## Checks when authorized

Choose cases relevant to the change: a pending offscreen job, transferred/equipped item, supported older schema, failed write, backup recovery, or altered settings. Compare authoritative state after resume. Merely opening the file does not establish correct recovery.

Check the selected release's [FileAccess](https://docs.godotengine.org/en/stable/classes/class_fileaccess.html) and [DirAccess](https://docs.godotengine.org/en/stable/classes/class_diraccess.html) APIs before adding I/O. Report target-platform persistence checks that were not performed.
