# Mobile persistence

## Save model

Use stable IDs and plain authoritative state for entities, inventory instances, equipment slots, repairs, energy, reservations, and jobs. Keep scene presentation and active finger state outside campaign ownership. Include a schema version and save each value once.

Use `user://` through supported engine APIs or the established platform storage policy. The app package and working directory are not general writable save locations. App identity, reinstall, storage clearing, backup/restore, and OS policy can affect saved data; do not promise persistence across every installation change. [Data paths](https://docs.godotengine.org/en/stable/tutorials/io/data_paths.html).

Keep preferences, bindings, graphics quality, and control layouts separate where their lifecycle/schema differs from campaign state. Choose a supported format; encode engine-specific values deliberately and validate parsed data before applying it.

## Transactions and recovery

Capture a consistent state boundary. Owned items, equipped-slot references, reserved/consumed materials, charge, and pending/completed job state must agree. Serialize offscreen and onscreen entities from the same state owner.

Where supported, write/close a temporary candidate, check results, validate it, and replace the active save with a recoverable prior version. Define failure outcomes. Do not call an arbitrary rename/write sequence crash-proof or assume a completed API call guarantees every storage lifecycle outcome.

Validate backups before use and retain original data during recovery/migration. Check types, required fields, duplicate IDs, ranges, missing definitions, ownership, equipment references, and job dependencies. Load a candidate state first so a malformed save cannot partially replace the running session.

Advance through explicit known schema migrations and verify progress at every step. Unsupported future versions need a clear incompatibility result. Removed/renamed content needs a defined mapping or a reported incompatibility, not silent replacement rewards.

## Backgrounding and cold start

Save at approved gameplay checkpoints or transaction boundaries during normal operation. Mobile suspension can lead to termination without a final clean quit; a suspension handler alone is insufficient as the complete save strategy. Keep any final lifecycle work short and supported. [Mobile lifecycle](https://docs.godotengine.org/en/stable/tutorials/inputs/handling_quit_requests.html#on-mobile-devices).

On resume, rebuild visual scenes from current state and clear invalid input ownership. On cold start, recover the last valid snapshot, including pending reservations and the information required to resume/cancel jobs. Neither path may duplicate a committed result.

Keep game time and wall-clock time distinct. Add offline progress only when approved, with bounded catch-up, supported job prerequisites, clock handling, and exactly-once completion. Avoid restoring held touch actions from a campaign save.

## Optional network/backup behavior

Cloud saves, account identity, cross-device progress, conflict resolution, and manual import/export are separate product decisions. Use the approved backend and a clear authority/schema policy when requested. Do not add network or external-storage permissions for ordinary local saves.

Define how supported platform backup/restore interacts with game identity and version when that behavior is part of distribution scope. An update/uninstall workaround must not silently destroy the user's campaign.

## Checks when authorized

Use relevant lifecycle cases such as a pending offscreen job, equipped/transferred item, suspension during a transaction, cold-start recovery, supported old schema, failed write, or invalid backup. Compare authoritative state, ownership, and job outcomes after recovery.

Read the selected release's [FileAccess](https://docs.godotengine.org/en/stable/classes/class_fileaccess.html) and [DirAccess](https://docs.godotengine.org/en/stable/classes/class_diraccess.html) APIs before changing storage. Identify physical-device termination/storage paths that have not been checked.
