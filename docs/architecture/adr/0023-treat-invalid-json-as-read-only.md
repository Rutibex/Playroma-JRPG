# Treat invalid JSON as read-only

- Status: Accepted
- Date: 2026-07-19

## Context

Automatic repair can destroy evidence or choose the wrong interpretation of malformed project data.

## Decision

Retain invalid document bytes and diagnostics but do not expose structured mutation or normal save.

## Consequences

Users can inspect and copy malformed projects without Playroma silently rewriting them.
