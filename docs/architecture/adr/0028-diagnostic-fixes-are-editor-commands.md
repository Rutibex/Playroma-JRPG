# Diagnostic fixes are editor commands

## Status

Accepted for Phase 3.

## Context

Validators must never bypass undo, transaction preview, external-change checks,
or the verified Phase 2 save pipeline. A separate mutation API for quick fixes
would create an unsafe alternate write path.

## Decision

A validation diagnostic may optionally provide a `DiagnosticFix` containing an
ordinary `IEditorCommand`. The application layer previews or commits that command
through a `ProjectTransaction`. Unsafe or ambiguous repairs are reported without
an automatic fix.

## Consequences

- Safe fixes are previewable, undoable, and included in reference invalidation.
- Validators remain read-only.
- Future UI panels can use the same command and transaction services as all other
  editors.
- A fix is not considered safe merely because it is easy to implement; it must
  have a deterministic, compatibility-preserving result.
