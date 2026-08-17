# Architecture

Playroma Sound separates audio generation, realtime rendering, authoring, and platform hosting so the engine can be embedded without the desktop editor.

## Dependency direction

```text
Core
  ↓
DSP
  ↓
Graph
  ↓
Generators
  ↓
Streaming

Analysis and Authoring consume the platform-neutral layers.
NAudio and WinForms are Windows-specific hosts at the outer edge.
```

Lower-level projects never reference WinForms or an operating-system audio API.

## Projects

### Playroma.Sound.Core

Defines audio formats, caller-owned buffers, render contracts, deterministic random generation, source nodes, offline rendering, block rendering, and WAV writing. Core has no external package dependency.

### Playroma.Sound.Dsp

Provides prepared in-place processors, parameter smoothing, filters, modulation, delay, reverb, dynamics, resampling, and analysis helpers.

### Playroma.Sound.Graph

Defines serializable `.playsound` graphs and compiles them into immutable execution plans. Compilation resolves node factories, deterministic seeds, reusable buffers, latency compensation, macros, scheduled events, validation, and effect tails.

### Playroma.Sound.Generators

Converts semantic descriptions into ordinary graph definitions. Generators own presets, profiles, deterministic variation families, mutation, quality budgets, phrase composition, and the universal sound renderer.

### Playroma.Sound.Streaming

Runs long-lived `.playstream` scenes with sound assets, buses, voice pools, layers, emitters, clusters, state machines, parameters, snapshots, spatial hooks, virtualization, telemetry, and hot replacement.

### Playroma.Sound.Analysis

Performs realtime and offline level, spectral, and performance analysis. Audio reaches analysis through bounded taps and copied blocks rather than unbounded work in a device callback.

### Playroma.Sound.Authoring

Owns project files, editable documents, undo history, validation, autosave, dependency tracking, compilation services, and export execution. It does not depend on WinForms.

### Playroma.Sound.NAudio

Adapts streaming audio sources to Windows WASAPI using NAudio. Procedural rendering occurs on a producer thread and feeds a bounded ring buffer. The device callback performs bounded conversion, gain, metering, and underrun-safe silence handling.

### Playroma.Sound.WinForms

Composes the authoring services and playback adapter into the desktop application. Controls own presentation state, not independent document models or alternate compiler state.

## Realtime rules

Audio-path implementations should remain bounded and allocation-free after preparation wherever practical. Expensive parsing, graph construction, buffer sizing, and asset loading belong outside the render callback.

Control messages cross bounded queues and are applied at block boundaries. Recording and analysis use separate bounded buffers and workers so disk or UI activity cannot block playback.

## Determinism

Root, family, and variation seeds are separated so callers can preserve a sound identity while requesting related variations. Random streams use named derivations; changing unrelated code should not intentionally reseed every property.

## Quality budgets

Generators support Draft, Realtime Low, Realtime High, and Offline quality modes. Budgets limit node count, event count, voice count, contour detail, tails, and processor complexity. A generated sound should remain recognizable at every supported quality level.

## File boundaries

- `.playgen`: semantic generator request
- `.playsound`: editable sound graph
- `.playstream`: streaming scene
- `.playproject`: project asset index
- `.playexport`: export profile

See [FILE_FORMATS.md](FILE_FORMATS.md) and the JSON Schemas in `schemas/`.
