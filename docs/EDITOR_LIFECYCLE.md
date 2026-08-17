# Editor Lifecycle

## Startup

1. `EditorApplicationContext` creates application services and displays `ProjectHubForm`.
2. Opening a project creates one `ProjectSession`.
3. `EditorApplicationController` creates and initializes `MapEditorForm`.
4. The Project Hub hides and secondary tools become available through the window registry.

## Editor windows

- Forms obtain document leases from the shared workspace.
- Forms subscribe only while alive and unsubscribe during disposal.
- Window closure releases local controls and leases but does not dispose the project session.
- Reopening a stable key activates the existing Form or creates a new one after the previous instance is disposed.

## Project close

1. Stop accepting new project operations.
2. Prepare local edit sessions without changing their state.
3. Present one coordinated decision.
4. Apply, discard, or preserve local sessions as confirmed.
5. Save, discard, or preserve shared changes.
6. Flush successful workspace state or recovery.
7. Close child windows.
8. Cancel and drain background tasks.
9. Dispose leases, watchers, controls, and session services.
10. Show the Project Hub or exit.

Cancelling at the decision stage leaves local and shared state unchanged.
