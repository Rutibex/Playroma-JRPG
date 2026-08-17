# Block save on external modification

- Status: Accepted
- Date: 2026-07-19

## Context

RPG Maker MV, text editors, Git, or another process may change files after Playroma opens them.

## Decision

Recompute a strong disk fingerprint during save preflight and block mismatches by default.

## Consequences

External work is protected. Explicit overwrite remains possible only through a deliberate policy selection.
