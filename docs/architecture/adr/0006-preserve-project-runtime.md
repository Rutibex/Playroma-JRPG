# 0006 — Preserve each existing project runtime

- Status: Accepted
- Date: 2026-07-19

## Context

MV plugins frequently monkey-patch exact CoreScript versions and may rely on NW.js or Node APIs.

## Decision

Do not automatically replace CoreScript, PixiJS, NW.js, `index.html`, or plugin files. The project's existing runtime is the authoritative compatibility target.

## Consequences

Compatibility is maximized. Security and runtime upgrades become explicit migrations with separate testing and rollback.
