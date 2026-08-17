# Bounded project directory search

- Status: Accepted
- Date: 2026-07-19

## Context

Unbounded recursion can traverse drives, network mounts, or directory cycles.

## Decision

Use breadth-first search with configurable depth and directory limits, and skip reparse-point directories.

## Consequences

Discovery is predictable and cancellable, although deeply nested projects may require a larger explicit search depth.
