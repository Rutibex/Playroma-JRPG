# Crash and Unsaved-Workspace Recovery

Recovery data is stored outside the MV project under the user's Local Application Data directory. This avoids adding proprietary fields or editor files to a Strict MV project.

A recovery manifest records project identity, creation time, modified document paths, original SHA-256 fingerprints, generated recovery filenames, and open document requests. Modified JSON bytes are stored under a `documents` subdirectory.

Writes use a temporary recovery directory followed by replacement of the final recovery directory. If no documents are modified, no candidate is written.

After each committed editor transaction, the shell schedules a debounced recovery update. A newer edit replaces the pending update, so rapid typing or painting does not create a write per command. Saving or closing cancels any pending recovery write.

Restoration validates project identity and loads each document through the normal mutable workspace. Recovered bytes are applied to memory. Disk files are not replaced until the user performs a verified Save.

Recovery is removed after a successful save or explicit discard. Corrupt manifests are ignored and retained for manual inspection.
