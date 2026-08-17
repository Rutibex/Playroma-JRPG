# 0003 — Keep MV files authoritative

- Status: Accepted
- Date: 2026-07-19

## Context

A proprietary database or imported internal representation would create drift, noisy exports, and recovery risks.

## Decision

Standard MV JSON, JavaScript, and asset files remain the project's source of truth. Caches, indexes, history, and enhanced metadata are derived sidecars.

## Consequences

Projects stay interoperable with existing tools. Search indexes and caches must be disposable and rebuildable.
