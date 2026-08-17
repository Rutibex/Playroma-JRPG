# 0009 — Use clean-room synthetic public fixtures

- Status: Accepted
- Date: 2026-07-19

## Context

Real MV projects and many plugin suites contain copyrighted or commercially licensed content that cannot be redistributed.

## Decision

Public tests use independently authored, minimal fixtures generated from machine-readable specifications. Real projects remain private local corpora and are never copied into the repository.

## Consequences

Synthetic fixtures may not reproduce every real-world quirk, so maintainers may run private stress tests in addition to the public regression suite.
