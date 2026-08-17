# Sprite Studio Clean-room Policy

## Permitted research inputs

- Publicly available user documentation and release notes.
- Official RPG Maker MV asset standards and help pages.
- Black-box observation of user-visible behaviour by contributors who do not copy implementation details.
- Independently authored test images and project files.

## Forbidden inputs and outputs

- Aseprite source code copied, translated, mechanically transformed, or used as a line-by-line implementation guide.
- Redistributed Aseprite executables or libraries.
- Aseprite icons, themes, screenshots, bundled palettes, samples, fonts, and UI resources.
- Decompilation, disassembly, or reverse engineering of proprietary binaries.
- Copied RPG Maker RTP artwork or commercial generator assets.

## Contribution record

Compatibility-sensitive pull requests must identify the public behavioural source, the independently designed Playroma approach, and synthetic tests. Contributors who inspected restricted implementation details must not provide copied code or implementation-specific instructions.

## Automated enforcement

`Playroma.Sprite.Core.CleanRoomAudit` rejects known Aseprite executables, libraries, vendored source paths, and project references. `eng/verify-sprite-phase0.*` runs this audit with the contract tests.
