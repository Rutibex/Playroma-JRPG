# Building and Development

## SDK

The repository targets .NET 10 and pins the SDK family in `global.json`.

```powershell
dotnet --version
```

## Build the complete solution

```powershell
./build.ps1
```

or:

```powershell
dotnet restore Playroma.JRPG.sln
dotnet build Playroma.JRPG.sln -c Release
```

The main WinForms project is:

```text
src/Playroma.JRPG.Editor.WinForms/Playroma.JRPG.Editor.WinForms.csproj
```

## Run from source

```powershell
dotnet run --project src/Playroma.JRPG.Editor.WinForms -c Release
```

## Tests

The solution contains cross-platform console-style test projects as well as Windows-only WinForms/visual smoke tests. GitHub Actions builds the complete solution on Windows and Linux and executes the public compatibility suites appropriate to each runner.

Synthetic test projects live under `testdata/fixtures`. Real/private compatibility corpora belong under `testdata/private` and are ignored by Git.

Regenerate/check deterministic fixtures with the fixture tooling under `tools/` and `eng/`.

## Repository audit

The repository includes a repository-audit utility used by CI to check source-tree policy. Among other release rules, proprietary RPG Maker assets, private projects, generated build output, and archives should not be committed.

## Architecture boundaries

Core format/document/generation libraries should remain independent of WinForms. Windows-specific presentation and media integration belongs in Windows/editor projects. See [ARCHITECTURE.md](ARCHITECTURE.md) and the ADRs under `docs/architecture/`.

## Contribution requirements

Before opening a pull request:

1. build the complete solution;
2. run tests covering the changed subsystem;
3. update documentation for user-visible changes;
4. add synthetic fixtures for compatibility-sensitive behavior;
5. update provenance for any new third-party material;
6. verify no private project data, secrets, build outputs, or archives are staged.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for the compatibility and clean-room rules.
