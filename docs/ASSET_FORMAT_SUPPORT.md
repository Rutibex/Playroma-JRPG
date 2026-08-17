# Asset Format Support

## Standard directories

Images: `img/animations`, `battlebacks1`, `battlebacks2`, `characters`, `enemies`, `faces`, `parallaxes`, `pictures`, `sv_actors`, `sv_enemies`, `system`, `tilesets`, `titles1`, and `titles2`.

Audio: `audio/bgm`, `bgs`, `me`, and `se`.

Other: `fonts` and `movies`.

## Images

Built-in metadata recognizes PNG, JPEG, BMP, and basic WebP headers. Windows thumbnail/preview decoding uses `System.Drawing` capabilities available on the host. Encrypted `.rpgmvp` assets are catalogued but not treated as editable source assets.

Specialized MV views support characters, faces, animations, side-view actors, IconSet, Window, and raw tileset sheets. Autotile composition is handled by the map/tile rendering and Tile Studio subsystems.

## Audio

- WAV: metadata and platform-neutral PCM/IEEE-float waveform analysis.
- OGG Vorbis: header/comments, loop metadata, Windows playback, and waveform analysis.
- M4A/MP4 audio: Windows Media Foundation playback/analysis where host codecs support it.
- Other formats accepted by NAudio/Windows may preview; unsupported codecs produce diagnostics.

Common OGG loop comments are read case-insensitively: `LOOPSTART`, `LOOPLENGTH`, and `LOOPEND`.

## Movies

Basic MP4/ISO-BMFF and WebM/Matroska container recognition is provided. Detailed embedded playback depends on the available host media support.

## Fonts

TTF and OTF SFNT metadata is inspected where possible. Windows preview uses a private font collection and does not install fonts. WOFF/WOFF2 remain catalogued even when Windows preview cannot load them.

## Encrypted and unsupported files

Encrypted deployment assets and unknown extensions remain visible with status and diagnostics. Playroma does not claim they are safely editable source material and does not discard them.
