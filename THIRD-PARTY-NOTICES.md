# Third-Party Notices

## Bundled source and assets

The repository contains Playroma-owned source code, documentation, and independently generated synthetic fixtures. It contains no copied RPG Maker editor code, RTP artwork, commercial audio, fonts, game runtime executables, or private stress-project content.

NuGet dependencies are restored by the build and are not committed as source or binaries in this archive.

## Runtime/build dependencies

### NAudio

- Project: NAudio
- Packages: `NAudio` 2.2.1/2.3.0 and `NAudio.Wasapi` 2.3.0
- Upstream: `https://github.com/naudio/NAudio`
- Licence: MIT
- Use: Windows audio decoding, Media Foundation AAC export, WASAPI playback, device selection, recording, and preview abstractions

Copyright notices and licence terms supplied by the package remain applicable.

### NAudio.Vorbis

- Project/package: `NAudio.Vorbis` 1.5.0
- Upstream: `https://github.com/naudio/Vorbis`
- Licence: MIT
- Use: Ogg Vorbis decoding for waveform and playback previews

Copyright notices and licence terms supplied by the package remain applicable.

### .NET

- Project: .NET
- Publisher: Microsoft and contributors
- Upstream: `https://github.com/dotnet`
- Licence: component-specific open-source licences
- Use: build and runtime platform; not redistributed by this repository

### System.Drawing / Windows media components

Playroma uses APIs supplied by the installed .NET and Windows environment for image, font, and Media Foundation support. No Windows binaries are redistributed.

### GitHub Actions

CI references official GitHub and .NET setup actions. They execute in GitHub infrastructure and are not copied into this repository.

## Referenced but not bundled

### RPG Maker MV CoreScript

- Project: RPG Maker MV CoreScript
- Upstream: `https://github.com/rpgtkoolmv/corescript`
- Licence: MIT
- Use: format/runtime research reference only

A later open starter project may bundle a reviewed CoreScript release. Its exact revision, licence text, and file list must be recorded before distribution.

## Contribution rule

Every contribution containing third-party code, images, audio, fonts, binaries, templates, or sample game data must update `provenance/ASSET_PROVENANCE.json` and identify the source, author/publisher, licence, covered files, and redistribution/modification permission. Unknown-provenance material is not accepted.

## Playroma Music integration dependencies

### MeltySynth

- Package: `MeltySynth` 2.4.1
- Licence: MIT
- Use: managed SoundFont 2 synthesis for playback and offline rendering

### OggVorbisEncoder

- Package: `OggVorbisEncoder` 1.2.2
- Licence: MIT
- Use: Ogg Vorbis encoding for RPG Maker MV audio export

### Included GeneralUser GS SoundFont

- File: `MusicAssets/SoundFonts/default.sf2`
- Embedded name: GeneralUser GS 2.0.3 BETA
- Author: S. Christian Collins
- Copyright: 1997-2025 by S. Christian Collins
- Use: default General MIDI SoundFont 2 bank for Playroma Music playback and rendering
- Licence: the complete embedded licence text is preserved in `MusicAssets/SoundFonts/GENERALUSER_GS_LICENSE.md`


## Playroma Sound integration dependencies

Playroma Sound itself is Michael Robinson's MIT-licensed source and is integrated under `src/Playroma.Sound.*`. Its preserved licence and original notices are available in `docs/PlayromaSound`. It uses the NAudio and OggVorbisEncoder dependencies documented above.


## Playroma Sprite Studio clean-room notice

Playroma Sprite Studio contains original Playroma source, schemas, documentation, and synthetic fixtures. Aseprite is referenced only as a public behavioural research target. No Aseprite code, executable, library, icon, theme, screenshot, bundled palette, sample sprite, or other resource is redistributed. RPG Maker MV format rules are represented as independently authored JSON profiles; no RTP artwork is included.

## Yanfly Engine Plugins metadata support

Yanfly Engine Plugins for RPG Maker MV and the KEL DragonBones integration are referenced as user-supplied compatibility targets for the Plugin Tags system. No YEP/KELYEP JavaScript, plugin archive, or copied full help text is bundled in this repository. The checked-in compatibility inventory contains independently derived filenames, identifiers, versions, SHA-256 fingerprints, short dependency evidence, and authoring-surface classifications only. Users must obtain and license the original plugins from their authorized source.

## Tracery

Text Studio implements a clean C# engine compatible with the public Tracery grammar model created by Kate Compton. The referenced `galaxykate/tracery` project is licensed under Apache-2.0. Playroma does not embed or execute the JavaScript implementation; compatibility was implemented natively from the public syntax/behavior.

## Text Studio optional AI bridge

Text Studio does not bundle an AI model, AI SDK, cloud-service client, provider executable, API key, or model weights. The optional `process` provider is a Playroma-defined stdin/stdout integration boundary for user-selected external software. Any configured provider remains governed by that provider's own licence and terms.
