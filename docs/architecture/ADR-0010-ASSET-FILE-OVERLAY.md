# ADR-0010: Stage Physical Asset Mutations in a Project File Overlay

Status: Accepted

## Context

Phase 5 requires file creation, replacement, movement, and deletion to participate in the same preview, undo, save, and recovery model as JSON editing. Direct file-system operations from WinForms controls would bypass transaction safety.

## Decision

Use `IProjectFileOverlay` to stage physical mutations outside the project. Editing commands target files and JSON uniformly. Asset content reads merge disk and overlay states. Save plans journal and atomically apply both kinds of mutation.

## Consequences

- imported/replaced assets can be previewed before save;
- project files remain unchanged during ordinary editing;
- mixed rename/reference updates are one transaction;
- save recovery must understand physical mutations;
- staging storage requires cleanup and quotas;
- later asset generators can use the same interface without direct writes.
