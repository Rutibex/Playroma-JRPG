# Playroma Sound integration

Playroma Sound is integrated into Playroma jRPG as a complete WinForms tool window. Open an RPG Maker MV project, then choose **Tools > Playroma Sound Studio** or click the **Sound** toolbar button.

## Full Playroma Sound editor

The integrated window hosts the original Playroma Sound application rather than a reduced generator panel. It retains:

- the fast **Sound Lab** workflow with Make Sound, Try Another, Keep + Next, Surprise Me, seeded variations, semantic families, and advanced JSON;
- editable `.playgen` generator requests;
- the visual `.playsound` graph editor, node palette, parameters, minimap, graph validation, compilation, and bypass/macro workflows;
- the `.playstream` streaming-scene editor for ambience, engines, weather, machines, crowds, adaptive layers, buses, emitters, zones, portals, and state machines;
- realtime WASAPI preview, audio-device selection, master gain, recording, diagnostics, waveform/spectral analysis, and realtime profiling;
- normal Playroma Sound project creation/open/save, undo/redo, autosave recovery, dependency validation, project export, runtime-package export, source definitions, rendered variations, stems, and analysis reports.

Each RPG Maker MV project receives a default editable Playroma Sound project at:

`data/PlayromaSound/Project/ProjectSounds.playproject`

The editor can still create or open other `.playproject` files. Settings, recent-project history, and autosaves are kept in the user's local Playroma jRPG application-data directory rather than in the deployable game folder.

## RPG Maker MV audio export

Open or create a sound and choose **Export to MV**, **File > Export Active Sound to RPG Maker MV**, or **Sound > Export Current Sound to RPG Maker MV**.

The exporter supports every audio container used by RPG Maker MV:

- Ogg Vorbis: `.ogg`
- AAC in MPEG-4: `.m4a`

Both can be generated together. The destination category controls the game folder:

| Category | Destination |
|---|---|
| BGM | `audio/bgm` |
| BGS | `audio/bgs` |
| ME | `audio/me` |
| SE | `audio/se` |

**SE** is selected by default for sound graphs and generator requests. **BGS** is selected by default for streaming scenes. Finite generated sounds stop naturally; the maximum render length acts as a safety limit. Streaming scenes render for the selected fixed duration.

The export uses the same authoring compiler as preview and project export. It supports compiled sound graphs, semantic generator requests, and streaming scenes. Generated files are immediately visible to RPG Maker MV-compatible audio selectors because MV audio resources are file based and do not require modification of a core JSON database table.

When **Save editable Playroma Sound source** is enabled, the current `.playgen`, `.playsound`, or `.playstream` definition is copied to:

`data/PlayromaSound/Exports/<category>`

A neighbouring `.playroma-sound-export.json` manifest records the source document, destination category, generated files, duration, sample rate, channel count, and export time.

M4A encoding uses Windows Media Foundation. Windows editions without the optional media components can export OGG or install the corresponding Media Feature Pack.

## Integrated source projects

The solution includes the complete Playroma Sound engine and editor projects:

- `Playroma.Sound.Core`
- `Playroma.Sound.Dsp`
- `Playroma.Sound.Graph`
- `Playroma.Sound.Generators`
- `Playroma.Sound.Streaming`
- `Playroma.Sound.Analysis`
- `Playroma.Sound.Authoring`
- `Playroma.Sound.NAudio`
- `Playroma.Sound.WinForms`

The original MIT licence, third-party notices, file schemas, documentation, and example authoring project are preserved under `docs/PlayromaSound` and `PlayromaSoundAssets`.
