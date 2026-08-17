# Command-Line Interface

The `Playroma.JRPG.Cli` project exposes compatibility, inspection, recovery, validation, reference, and asset tools.

Run help:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- --help
```

General form:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- <command> [arguments]
```

## Commands

```text
probe <project> [--format json]
inspect <project> [--complete] [--format json]
roundtrip verify <project>
project copy <source> <destination>
project compare <left> <right> [--format json]
document set-string <project> <file> <json-pointer> <value> --save
recovery inspect <project>
recovery rollback|complete|cleanup <project> <transaction-id>
references <project> [--type skill] [--id 42] [--exact-only] [--format json]
dependencies <project> --type class --id 16 [--format json]
validate <project> [--format json]
assets <project> [--query "category:characters used:true"] [--format json]
asset-audit <project> [--skip-duplicates] [--format json]
```

Inspection, reference indexing, validation, catalogue browsing, and asset auditing are intended to be read-only. Save-capable commands use the repository's external-change, backup, transaction-journal, and verification infrastructure.

## Examples

Inspect a project:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- inspect "C:\Games\MyGame" --complete
```

Verify a lossless round trip:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- roundtrip verify "C:\Games\MyGame"
```

Validate and emit JSON:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- validate "C:\Games\MyGame" --format json
```

Find dependencies on class ID 16:

```powershell
dotnet run --project src/Playroma.JRPG.Cli -- dependencies "C:\Games\MyGame" --type class --id 16
```
