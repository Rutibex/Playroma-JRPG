# Integration guide

## Choose the smallest useful layer

Use only the projects your application needs:

- **Core** for direct node construction, offline rendering, and WAV output.
- **Core + DSP + Graph** for serializable graphs and graph compilation.
- **Generators** for semantic sound creation and deterministic variations.
- **Streaming** for long-running scenes and realtime game-state control.
- **Analysis** for meters, spectral data, and profiling.
- **Authoring** for project/document services outside the WinForms editor.
- **NAudio** for Windows WASAPI playback.

## Render a built-in preset

```csharp
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.IO;
using Playroma.Sound.Core.Rendering;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Presets;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Compilation;

var format = new AudioFormat(48_000, 2, 512);
var context = new SoundGenerationContext
{
    RootSeed = 1001,
    FamilySeed = 9001,
    VariationIndex = 0,
    QualityMode = SoundQualityMode.RealtimeHigh
};

var preset = BuiltInPresetCatalog.GetRequired("magic.unstable-teleport");
var definition = preset.Generate(context);
var compiler = new SoundGraphCompiler(BuiltInGraphRegistry.CreateDefault());
var rendered = OfflineRenderer.RenderUntilComplete(
    compiler.Compile(definition, format),
    format,
    maximumDurationSeconds: 20);

WavWriter.Write("teleport.wav", rendered, WavEncoding.Pcm16);
```

## Generate from a semantic description

```csharp
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Generators;
using Playroma.Sound.Generators.Profiles;

var generator = new FootstepGenerator();
var description = new FootstepDescription
{
    Footwear = FootwearType.Boot,
    Surface = SoundMaterial.Concrete,
    Environment = AcousticEnvironment.UrbanStreet,
    Wetness = 0.35f,
    Weight = 0.62f,
    Gait = FootstepGait.Run
};

var graph = generator.Generate(description, new SoundGenerationContext
{
    RootSeed = 42,
    FamilySeed = 7,
    GraphId = "footstep.boot-concrete",
    DisplayName = "Boot on Concrete",
    QualityMode = SoundQualityMode.RealtimeHigh
});
```

## Produce an identity-preserving variation family

```csharp
var family = generator.GenerateSet(description, context, count: 8);

foreach (var variation in family.Variations)
{
    // Compile, cache, serialize, or render each related graph.
}
```

Equal descriptions and seeds produce equal graph definitions. Change `VariationIndex` or request a generated set to obtain related alternatives while retaining the family identity.

## Save and load graphs

```csharp
using Playroma.Sound.Graph.Serialization;

var serializer = new SoundGraphSerializer();
serializer.Save("effect.playsound", graph);
var loaded = serializer.Load("effect.playsound");
```

Use `BuiltInGraphRegistry.CreateDefault()` when compiling graphs that rely on built-in nodes. Applications can register additional node factories in their own registry before compiling.

## Load generator requests

`.playgen` files are serialized through `SoundGeneratorRequestSerializer` or the editable authoring request serializer, depending on whether the application needs authoring metadata. The generator catalog resolves the stored generator ID and description type.

## Streaming scenes

A scene compiler needs both the `.playstream` definition and an `ISoundAssetResolver` that can resolve the logical IDs referenced by the scene. The repository contains file and in-memory resolver implementations.

Create or load a scene, prepare it against a format, then render blocks through the streaming runtime. Send parameter, event, snapshot, listener, emitter, and hot-reload commands through the runtime's bounded control interface rather than modifying prepared state directly.

## Threading guidance

- Construct and compile graphs off the audio thread.
- Prepare processors and allocate buffers before playback.
- Reuse rendered or compiled assets when a sound is triggered frequently.
- Use bounded control queues for realtime changes.
- Keep disk I/O, JSON serialization, logging, and UI work out of render callbacks.
- Treat telemetry as sampled data rather than a synchronous query into the audio path.

## Exported runtime packages

The authoring exporter can copy selected definitions and their dependencies into a runtime package. The package includes a manifest and integration notes. Applications remain responsible for choosing an audio-device adapter and mapping game state to scene controls.
