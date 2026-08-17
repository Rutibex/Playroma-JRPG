# ADR 0033: File Watcher Is Advisory

Status: Accepted for Phase 4.

`FileSystemWatcher` marks documents stale and informs the user, but it never authorizes a save. Save-time cryptographic fingerprints and the Phase 2 external-modification policy remain authoritative.
