# Legal and Provenance Baseline

This document is an engineering policy, not legal advice.

## Project code

Playroma-owned source code is released under the MIT licence in `LICENSE`.
Documentation authored for this repository is distributed with the source
under the same licence unless a file says otherwise.

## RPG Maker MV compatibility

Compatibility research may study openly licensed CoreScript behavior and
project files supplied by their owners for private testing. Private projects
must not be committed, redistributed, quoted as sample content, or used to
seed public fixtures.

The synthetic fixture corpus uses original placeholder names and values. It
contains no copied maps, dialogue, notes, plugin code, graphics, sounds, or
records from any real project.

## Runtime policy

Playroma does not bundle CoreScript or JavaScript libraries. Before a later
phase bundles a runtime, maintainers must:

1. select an exact upstream revision,
2. verify every included file's licence,
3. copy the required licence notices,
4. record file hashes and provenance,
5. confirm that no proprietary editor assets are present.

## Binary asset policy

Every redistributed binary must have an entry in
`provenance/ASSET_PROVENANCE.json`. The repository audit fails when it finds
known RPG Maker encrypted assets, deployed runtime binaries, or unregistered
media.

## User responsibility

Playroma can edit projects containing assets and plugins the user legitimately
possesses. Technical compatibility does not grant permission to redistribute
those materials.
