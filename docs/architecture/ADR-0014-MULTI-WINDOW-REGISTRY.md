# ADR-0014: Registry-managed top-level WinForms editors

## Status

Accepted.

## Context

Playroma originally places nearly every editor in one docked shell and one centre `TabControl`. The target experience uses independent top-level windows while preserving one shared project session, document cache, undo system, and save transaction system.

Directly constructing Forms from controls would create duplicate windows, inconsistent ownership, lost document leases, and application-lifetime problems.

## Decision

Use a platform-neutral window contract in `Editor.Shell` and a WinForms implementation in `Editor.WinForms`.

Every top-level editor is identified by a stable `WindowKey`. `WinFormsWindowService` owns the live registry and is the only normal path for opening, activating, and closing migrated editor windows. Editor kinds are supplied by an explicit factory registry.

Existing editor controls migrate through `HostedEditorToolForm`, which owns their visual and asynchronous lifetimes without requiring an immediate editor rewrite.

Window placement is persisted by placement family rather than by every record identity. For example, every Event Editor can share one useful size while each event still has its own live registry key.

## Consequences

Positive:

- duplicate editor windows are prevented
- Form ownership and shutdown become deterministic
- document leases have explicit lifetimes
- migration can occur one editor at a time
- the shell contracts remain independent of WinForms
- later active-window command routing has a single source of truth

Trade-offs:

- both the old document service and new window service coexist during migration
- each migrated editor requires a registered factory
- async close validation requires Form close interception
- editor loaders must not recursively wait on the same window operation they are currently performing
