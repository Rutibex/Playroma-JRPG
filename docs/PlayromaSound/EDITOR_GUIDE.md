# Editor guide

The Playroma Sound editor is a WinForms host over the platform-neutral Authoring, Graph, Generator, Streaming, Analysis, and Core libraries. It is intended for fast semantic sound design and detailed graph editing in the same project.

## Projects and assets

A `.playproject` file indexes project assets by logical ID and relative path. Supported project assets include:

- `.playgen` semantic generator requests
- `.playsound` compiled-editable sound graphs
- `.playstream` long-running streaming scenes
- `.playexport` export profiles
- samples, impulse responses, and other supporting files

The editor validates paths and dependencies at project level. Open documents retain undo history, dirty state, background validation, and autosave recovery.

## Sound Lab workflow

The Sound Lab is the fastest way to create a new effect.

1. Select a generator family.
2. Choose a named sound type, action, material, body, vehicle, liquid, environment, or archetype.
3. Adjust shared controls such as duration, intensity, size, pitch contour, rhythm, articulation, emotion, energy, condition, tonal/noise balance, movement, and distance.
4. Use **Surprise Me** for a new seeded design.
5. Use **New Voice** or variation navigation to preserve the broad identity while changing details.
6. Preview, save, export, or convert the result to a graph.

Advanced JSON editing is available when a generator exposes a property not yet represented by a dedicated control.

## Visual graph editor

Sound graphs contain source, processor, mixer, delay, control, and output nodes. The graph compiler validates ports, connections, cycles, formats, latency, event timing, and processor budgets before producing an immutable execution plan.

Use the graph editor when you need to:

- alter source-node parameters directly;
- insert filters, distortion, dynamics, delay, or reverb;
- combine multiple generated sounds;
- inspect generator-produced graph structure;
- bypass nodes while comparing alternatives;
- create reusable macros;
- edit a sound beyond the semantic generator model.

## Streaming-scene editor

Streaming scenes are intended for ambiences, engines, weather, machines, crowds, adaptive environments, and long-running game audio. Scenes can define sound references, buses, parameters, layers, stochastic emitters, event clusters, state machines, acoustic zones, and portals.

The preview runtime supports bounded voice pools, virtualization, snapshots, hot replacement, telemetry, and block-boundary control updates.

## Analysis and profiler

The analysis view can inspect realtime or offline output. It includes level, peak, spectral, and performance information. Profiler data is collected through bounded taps so analysis work does not become part of the audio callback's critical path.

## Export

Use **File → Export Project** to render selected assets or create a runtime package.

Rendered-audio exports can include:

- generated graphs and generator requests;
- fixed-duration streaming scenes;
- multiple deterministic variations;
- stems where supported;
- neighbouring analysis reports;
- a machine-readable export manifest.

Runtime-package exports copy selected definitions and their dependencies for integration in another application.

## Recovery and safety

Autosave files do not silently overwrite source documents. Recovered content is opened as dirty content and must be saved explicitly. Project and settings writes use temporary files followed by atomic replacement where supported.
