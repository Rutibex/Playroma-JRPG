# Playroma Editor Architecture

## Production window model

Playroma is a multi-form WinForms editor. There is no MDI container, central document tab host, or docked compatibility shell.

```text
EditorApplicationContext
├─ ProjectHubForm
├─ EditorApplicationController
├─ WinFormsWindowService
│  ├─ DatabaseForm
│  ├─ ResourceManagerForm
│  ├─ PluginManagerForm
│  ├─ ValidationForm
│  ├─ ReferenceExplorerForm
│  ├─ OutputForm
│  ├─ BackgroundTasksForm
│  ├─ RawJsonForm
│  ├─ ProjectSummaryForm
│  ├─ PreferencesForm
│  └─ RecoveredSessionsForm
└─ MapEditorForm
   ├─ EventEditorForm
   ├─ EventCommandPaletteForm
   ├─ MapPropertiesForm
   └─ owned parameter dialogs
```

The `EditorApplicationContext` owns process lifetime. `EditorApplicationController` owns the open-project transition. `WinFormsWindowService` owns secondary editor-window identity, activation, modality, placement, and disposal. `MapEditorForm` is the primary project workspace and implements the narrow `IProjectWorkspaceHost` contract.

## Shared project state

All Forms use one `ProjectSession`. They do not load separate mutable copies of project JSON and never write project files directly. Mutations flow through document leases, edit sessions, project transactions, undo/redo, validation, and the save coordinator.

Logical navigation still uses `DocumentRequest`; the name describes a project target, not an operating-system document tab. `WindowRequest` and `WindowKey` select or create the correct dedicated Form.

## Window identity

Every reusable editor has a stable key. Opening the same key activates the existing window instead of creating a duplicate. Modal event editing uses stable event identity, while utility windows are project- or application-scoped singletons.

## Commands

`WinFormsCommandRouter` resolves commands in this order:

1. Focused native control.
2. Focused editor control or provider.
3. Active Form's explicit `IEditorCommandTarget`.
4. Window command fallback.
5. Shared shell command fallback.

The retired reflection-based `ProcessCmdKey` compatibility target is not part of the production route. Custom controls may still override `ProcessCmdKey` for intrinsic navigation such as map-cursor movement or command-palette spatial movement.

## Lifetime and shutdown

Project close is coordinated. Local edit sessions prepare without mutation, one shutdown decision is collected, shared project changes are saved/discarded/preserved once, recovery is flushed when needed, background work is drained, leases are disposed, the session is released, and the Project Hub returns.

## Settings and recovery

Application settings use schema version 9. `EditorSettingsMigrator` reads versions 1–8 and maps the old main-window bounds and left dock proportion to the Map Editor's placement and navigation width. Obsolete shell properties are never written again.

Workspace recovery uses the current snapshot model. New recovery manifests do not write the retired `OpenDocuments` list. Old manifests remain readable and are converted by `WinFormsWorkspaceRecoveryCoordinator` during restoration.
