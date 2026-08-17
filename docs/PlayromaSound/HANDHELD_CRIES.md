# Classic handheld monster cries

## Purpose

Playroma Sound includes a dedicated generator for **original cartridge-era monster cries**. It does not copy commercial cry data and it does not process samples through a bit crusher. It creates new cry programs from the same broad sound resources available in a classic handheld:

- two independent fixed-duty pulse channels;
- four-bit channel volume;
- discrete note commands with separate duration, period, volume, duty, and pitch movement;
- one independently sequenced 15-bit or 7-bit LFSR noise channel controlled by an NR43-style polynomial byte;
- a simple mono handheld output stage with DC removal, speaker bandwidth limiting, and peak protection.

The first implementation created several overlapping sustained pulse nodes. Although the timbre was correct, the result could be perceived as one note. The revised implementation uses a compact internal command sequencer. Every generated cry now contains a rhythmic phrase of separate attacks, rests, pitch jumps, duty changes, counter-voice notes, and noise bursts.

## Sound Lab workflow

1. Open **HANDHELD CRIES** in the Sound Lab.
2. Choose an archetype or leave it on **Random**.
3. Choose a contour or leave it on **Random**.
4. Set **Syllables** to control the number of recognizable cry gestures.
5. Adjust **Pitch**, **Length**, **Body Size**, and **Ornament Complexity** for the main identity.
6. Use **Pitch Range**, **Second Pulse**, **LFSR Noise**, and **Roughness** for cartridge character.
7. Use **Repetition** to repeat a compact motif and **Punch** to alter articulation and decay.
8. Press **Surprise Me** or **New Voice** to create another deterministic cry family, then save good results as usual.

A useful starting range is 5–9 syllables, 45–80% ornament complexity, and 55–90% pitch range.

## Archetypes

- **Squeak**: rapid high notes, octave jumps, narrow duties, and small noise attacks.
- **Chirp**: ascending and answering note fragments with short articulation gaps.
- **Buzz**: close pitch alternation, pulse beating, and sustained short-mode noise.
- **Croak**: descending low notes, uneven rhythm, and coarse noise bodies.
- **Roar**: heavy pulse intervals, long terminal notes, and low-rate noise.
- **Wail**: long arching phrases with a slower answering pulse voice.
- **Rattle**: extreme alternating intervals and sharply separated noise bursts.
- **Electronic**: broken interval patterns, duty changes, and irregular counter-motion.

## Three independent command streams

The generator builds one program for each hardware-style voice:

### Pulse 1

Carries the primary melodic identity. It uses discrete periods, note-by-note duty patterns, individual four-bit volume contours, short rests, and within-note slides.

### Pulse 2

Acts as a counter-voice rather than a simple detuned duplicate. It has a short lead-in, independent intervals, alternating counter-motion, and optional two-part ornaments inside selected notes.

### Noise

Uses separate timing and polynomial values. Depending on the archetype, it can provide attack clicks, insect buzz, croak texture, rattling fragments, or a sustained roar body.

## Graph node

### `core.handheld-cry-sequence`

Parameters:

- `pulse1Program`
- `pulse2Program`
- `noiseProgram`
- `masterAmplitude`
- `noiseMix`
- `pitchShiftSemitones`

Each program is parsed during node construction and converted to fixed runtime command arrays during `Prepare`. Rendering is allocation-free. A generated cry can contain dozens of micro-commands while using only one graph source node, so even Draft quality retains a recognizable multi-note phrase.

The lower-level `core.handheld-pulse` and `core.handheld-noise` nodes remain available in the visual graph for manual one-shot channel programming.

## Deterministic families

The identity phrase is derived from the root and family seeds at variation zero. It determines the archetype, contour, rhythm weights, motif, duty sequence, counter-voice interval, and noise character. Changing the variation index makes restrained changes to tuning and noise details without discarding the family identity.

## Originality

The implementation models public hardware behaviour and creates new procedural command sequences. It contains no Pokémon cry tables, no extracted ROM data, no copyrighted samples, and no species names in the generator or built-in presets.
