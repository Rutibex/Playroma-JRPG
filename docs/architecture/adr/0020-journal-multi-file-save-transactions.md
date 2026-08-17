# Journal multi-file save transactions

- Status: Accepted
- Date: 2026-07-19

## Context

A filesystem cannot atomically replace an arbitrary set of independent project files as one operation.

## Decision

Write and verify temporary files, back up targets, record per-file progress in a transaction journal, and atomically replace each target.

## Consequences

Interrupted saves are detectable and recoverable rather than silently partial.
