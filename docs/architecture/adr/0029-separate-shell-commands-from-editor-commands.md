# ADR 0029: Separate Shell Commands from Editor Commands

Status: Accepted for Phase 4.

UI commands such as Open, Save, Validate, and Show Panel use `IShellCommand`. Reversible project mutations continue to use Phase 3 `IEditorCommand`. This prevents menus and keyboard routing from becoming part of the project mutation model while allowing all UI entry points to share one command registry.
