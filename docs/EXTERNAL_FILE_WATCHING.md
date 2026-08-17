# External File Watching

The editor uses `FileSystemWatcher` only as an early warning system.

Notifications are debounced, converted to normalized project-relative paths, and ignored for `.playroma` paths. Matching open documents enter the Stale state, and the Output/status areas report the change.

File-system notifications are not reliable enough to authorize or reject a save. They can be combined, duplicated, delayed, or dropped. The save pipeline therefore re-fingerprints every affected destination and remains the authoritative conflict detector.

The watcher is temporarily suppressed during Playroma-owned save operations to avoid reporting its own atomic replacements as external edits.
