# Getting started

## Install the prerequisites

Install the .NET 10 SDK. Visual Studio is optional, but a Visual Studio release that supports .NET 10 with the .NET desktop development workload is the easiest way to work on the WinForms editor.

The complete solution includes Windows-specific WinForms and WASAPI projects. Build the full repository on Windows. The Core, DSP, Graph, Generators, Streaming, Analysis, and Authoring libraries remain platform-neutral.

## Build and run the self-tests

From the repository root:

```powershell
.\Build.ps1
```

The script restores the solution, builds it in Release configuration, and runs every executable self-test project. Any failed command stops the script and returns a non-zero exit code.

Equivalent manual commands begin with:

```powershell
dotnet restore .\Playroma.Sound.sln
dotnet build .\Playroma.Sound.sln -c Release --no-restore
```

## Open the editor

```powershell
.\RunEditor.ps1 .\content\PlayromaSoundExamples.playproject
```

The example project includes generator requests, sound graphs, streaming scenes, and a default export profile.

## First sound in the Sound Lab

1. Open the example project.
2. Create or open a `.playgen` generator request.
3. Choose a family such as Creature Cries, Handheld Cries, Magic, Combat, Machines, or Environments.
4. Use **Surprise Me** to generate a seeded design.
5. Adjust the semantic controls rather than individual oscillator parameters.
6. Preview the result.
7. Save the request, convert it to a graph for detailed editing, or export it to WAV.

A `.playgen` file stores the generator ID, description data, seeds, variation index, quality mode, and identity metadata. It does not store rendered audio.

## Generate example files

```powershell
.\GenerateExamples.ps1
```

This writes generated sound graphs and WAV files into the ignored `GeneratedExamples/` directory. Generation is deterministic for equal inputs and seeds.

## Run the smaller demonstrations

```powershell
.\RunAuthoringDemo.ps1
.\RunWasapiDemo.ps1
```

The source tree also contains focused Core, DSP, Graph, Generator, and Streaming demonstration projects that can be started with `dotnet run --project`.

## Next steps

Use the [editor guide](EDITOR_GUIDE.md) for the desktop workflow, or the [integration guide](INTEGRATION.md) to embed the engine in another C# project.
