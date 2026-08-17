# Two-level fingerprints

- Status: Accepted
- Date: 2026-07-19

## Context

Hashing every audio, movie, and image file makes initial loading unnecessarily expensive.

## Decision

Always record path, length, and timestamp; compute SHA-256 for data, plugin, and runtime files, and hash assets only when requested.

## Consequences

Snapshots remain useful and responsive while forensic asset hashing is opt-in.
