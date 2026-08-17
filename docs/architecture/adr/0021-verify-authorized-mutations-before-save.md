# Verify authorized mutations before save

- Status: Accepted
- Date: 2026-07-19

## Context

A serializer defect could change data outside the field the user edited.

## Decision

Compare original and generated JSON semantically and require every difference to fall under an authorized DocumentChange path.

## Consequences

Unexpected data changes block saving and become actionable diagnostics.
