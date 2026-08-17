# Use a lazy mutable document cache

- Status: Accepted
- Date: 2026-07-19

## Context

Large projects contain many maps that need not remain parsed simultaneously.

## Decision

Open mutable documents on demand, evict clean documents under configured limits, and never evict modified documents.

## Consequences

Memory use remains bounded while unsaved work stays resident.
