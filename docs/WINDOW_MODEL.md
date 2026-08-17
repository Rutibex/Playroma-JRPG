# Window Model

## Primary forms

- `ProjectHubForm` is visible when no project is open.
- `MapEditorForm` is the primary Form for an open project.
- Closing the Map Editor requests coordinated project closure; closing a secondary Form does not close the project.

## Secondary forms

Database, Resource Manager, Plugin Manager, Validation, References, Output, Background Tasks, Raw Data, Project Summary, Preferences, recovered sessions, and integrated studios are registered through `WinFormsWindowService`. Stable `WindowKey` values prevent duplicate mutable editors for the same logical target.

## Owned modal forms

Event editors, parameter editors, map properties, import wizards, maximum-count editors, pickers, and other transactional dialogs use an owner so focus and modality remain associated with the launching editor.

## Local tabs

Tabs are used inside purpose-built editors where they clarify a single workflow, such as Event Commands, Preferences, raw-data views, references, and tileset selections. The application does not use a generic central document-tab shell.

## Placement

`WinFormsWindowPlacementService` stores normal bounds, maximized state, and monitor identity. Restored bounds are clamped to available monitors and minimum sizes. Project-specific navigation is stored separately in the workspace snapshot.
