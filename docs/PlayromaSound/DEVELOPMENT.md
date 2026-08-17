# Development guide

## Build

```powershell
.\Build.ps1
```

The build treats compiler warnings as errors and enables nullable reference analysis, implicit usings, deterministic output, and the latest supported C# language version for the configured SDK.

## Self-tests

The repository uses executable self-test projects rather than a third-party test framework. Each test executable returns a non-zero exit code on failure. `Build.ps1` runs all nine suites after a successful Release build.

When changing a library, add focused coverage to its corresponding project under `tests/`. Changes that cross project boundaries may require tests in more than one suite.

## Architecture rules

- Keep Core free of external dependencies.
- Keep platform-neutral projects free of WinForms and operating-system audio APIs.
- Put Windows endpoint code in `Playroma.Sound.NAudio`.
- Put presentation code in `Playroma.Sound.WinForms`.
- Compile immutable runtime structures before audio rendering.
- Avoid allocations, locks, file I/O, parsing, and unbounded work in realtime paths.
- Preserve deterministic output for equal seeds unless a documented compatibility change is intentional.
- Respect quality budgets in every generator.

## Coding style

The repository includes `.editorconfig`. Use nullable annotations, explicit validation at public boundaries, meaningful exception messages, and deterministic ordering in serialized output.

Prefer small composable nodes and processors over generators that contain a second hidden audio engine. Semantic generators should produce ordinary graph definitions wherever practical.

## Adding a graph node

1. Add the node and any shared contracts to the lowest appropriate project.
2. Keep construction and parsing outside the render loop.
3. Register the factory in the built-in graph registry.
4. Add validation metadata and visual metadata where applicable.
5. Add serialization, finite-render, parameter-boundary, and budget tests.
6. Add a concise entry to the relevant documentation.

## Adding a generator family

1. Define a semantic description and enums.
2. Implement a deterministic generator with a stable generator ID.
3. Use named random streams and quality budgets.
4. Register the generator in the catalog.
5. Add editor descriptors and appropriate quick controls.
6. Add at least one built-in preset and one `.playgen` example.
7. Test validation, determinism, finite rendering, preset compilation, catalog coverage, and Draft budget compliance.
8. Update `docs/SOUND_CATALOG.md`.

## Pull requests

Keep pull requests focused. Explain the user-visible change, architectural impact, tests run, and any file-format or deterministic-output compatibility implications. Do not include `bin/`, `obj/`, `.vs/`, `Generated/`, exported WAV collections, or copyrighted sample material.
