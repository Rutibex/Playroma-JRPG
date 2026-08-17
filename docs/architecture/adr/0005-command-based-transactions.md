# 0005 — Use command-based transactions and undo

- Status: Accepted
- Date: 2026-07-19

## Context

Painting, refactoring, bulk edits, and generation can touch many documents and must be reversible as one user operation.

## Decision

Represent editor mutations as commands executed inside project transactions. Commands support execute and undo; transactions support commit and rollback.

## Consequences

All editing features must use the command layer. Direct document mutation is restricted to loaders, transaction internals, and recovery code.
