# Getting Started

## 1. Install prerequisites

Install the .NET 10 SDK on Windows 10 or later. Confirm it is available:

```powershell
dotnet --info
```

The repository uses `global.json` to select .NET 10.

## 2. Build Playroma

From the repository root:

```powershell
./build.ps1
```

Equivalent commands:

```powershell
dotnet restore Playroma.JRPG.sln
dotnet build Playroma.JRPG.sln -c Release
```

## 3. Start the editor

```powershell
dotnet run --project src/Playroma.JRPG.Editor.WinForms -c Release
```

You can also pass an RPG Maker MV project directory as the final argument.

## 4. Open a project

Use the Project Hub to select an RPG Maker MV project. Playroma supports normal MV source layouts and selected readable deployed layouts. Encrypted deployment assets are not treated as editable source assets.

For your first session, work on a copy of a project or commit the project to version control before editing.

## 5. Understand the workspace

The Map Editor is the primary project workspace. Related tools open in separate windows, including Database, Event Editor, Resources, Plugins, Validation, References, Output, Preferences, and the integrated content studios.

All editors share one project session. A secondary window does not load or save an independent copy of the project.

## 6. Make a first safe edit

A good first test is a small database text change:

1. Open **Database**.
2. Select an existing record.
3. Change a simple text field such as a name or description.
4. Review validation if any warnings appear.
5. Save with `Ctrl+S`.
6. Confirm the project still opens in RPG Maker MV or your normal runtime workflow.

## 7. Recovery and external changes

Playroma uses transaction/recovery infrastructure for project writes. If a file changes outside Playroma while you are editing, unsafe overwrites should be blocked rather than silently replacing the external change.

If the editor detects an interrupted save or recoverable session, use the recovery UI to inspect the available action before continuing.

## 8. Next steps

Read the [User Guide](USER_GUIDE.md) for the main editor and [Integrated Studios](STUDIOS.md) for content-generation tools.
