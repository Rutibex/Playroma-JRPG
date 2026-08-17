# Preserve plugins.js prefix and suffix

- Status: Accepted
- Date: 2026-07-19

## Context

plugins.js is JavaScript source wrapped around a JSON-compatible array and may contain comments or nonstandard spacing.

## Decision

Extract the $plugins array without evaluation, edit it losslessly, and copy all prefix and suffix bytes unchanged.

## Consequences

Plugin order, duplicates, wrapper comments, and raw parameter strings survive editing.
