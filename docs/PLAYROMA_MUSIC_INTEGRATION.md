# Playroma Music integration

Playroma Music is integrated into Playroma jRPG as a native WinForms tool window. Open an RPG Maker MV project, then choose **Tools > Playroma Music Studio** or click the **Music** toolbar button.

## Workspaces

The WinForms studio preserves the original Playroma Music authoring areas:

- **Composer** — structured style-driven composition, form/harmony regeneration, motif regeneration, role locking, role-level regeneration, and an interactive piano roll.
- **Generation Lab** — algorithmic voice presets, per-voice configuration and locking, selected/unlocked regeneration, and full laboratory rebuilds.
- **Studio + Mixer** — mute/solo, channel, bank, program, volume, pan, SoundFont selection, preset scanning, audio settings, diagnostics, and real-time playback.
- **Style Packs** — all bundled JSON style packs, live reload, role inspection, direct style application, and hybrid style blending.
- **Advanced Composition** — tension, arrangement, motif planning, advanced search, and rebuilding from edited plans.
- **Orchestration + Randomizer** — orchestration settings, genome editing, complete instrument catalog editing, domain randomization, and new-universe generation.
- **Reference MIDI** — MIDI analysis, editable blueprints, blueprint load/save, source-track role correction, corpus analysis, corpus style-pack export, and descendant generation.

The top toolbar provides new/open/save, undo/redo, MIDI import/export, WAV rendering, RPG Maker MV export, playback, looping, and master volume.

## Included SoundFont 2

`MusicAssets/SoundFonts/default.sf2` is copied to `SoundFonts/default.sf2` in the editor output and selected automatically for new music documents. The bank identifies itself as **GeneralUser GS 2.0.3 BETA**. Its embedded licence is preserved at `MusicAssets/SoundFonts/GENERALUSER_GS_LICENSE.md` and copied beside the application documentation.

Use **Studio + Mixer > Use Included SF2** to restore the bundled bank after selecting another SoundFont. **Scan Presets** reads the bank and rebuilds the editable orchestration catalog.

## Interactive piano roll

Select a role in Composer to show its generated track.

- Left-click empty space to add a snapped note.
- Left-click and drag an existing note to move it in time and pitch.
- Right-click a note, or select it and press Delete/Backspace, to remove it.
- Use the mouse wheel over the selected note to change velocity.

Every edit is captured by the normal Playroma Music undo/redo history.

## RPG Maker MV audio export

Choose **MV Export** or **Export > RPG Maker MV audio database**. Select one of the four MV audio categories:

| Category | Destination |
|---|---|
| BGM | `audio/bgm` |
| BGS | `audio/bgs` |
| ME | `audio/me` |
| SE | `audio/se` |

The exporter can create both supported deployment files in one operation:

- Ogg Vorbis: `.ogg`
- AAC in MPEG-4: `.m4a`

An editable authoring document can also be stored under `data/PlayromaMusic`. The generated audio files are immediately available to the RPG Maker MV-compatible project without altering the core database JSON files because MV audio resources are file based.

M4A encoding uses Windows Media Foundation. On Windows editions where the optional media components are absent, install the relevant Windows Media Feature Pack or export OGG only.

## Project files

Editable compositions use `*.playroma-music.json`. Standard MIDI import accepts `*.mid` and `*.midi`; export writes a type-1 Standard MIDI file. Reference blueprints use `*.reference.json`, and corpus-derived style packs use `*.style.json`.

## Build dependencies

The WinForms editor references the integrated `PlayromaMusic.Core`, `PlayromaMusic.Generators`, `PlayromaMusic.Audio`, and `PlayromaMusic.Midi` projects. Audio export and playback additionally use MeltySynth, NAudio, and OggVorbisEncoder through NuGet restore.
