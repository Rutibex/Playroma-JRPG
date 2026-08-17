# FAQ

## Does Playroma Sound require samples?

No. The included generators and presets are procedural. The project system can index samples and impulse responses for applications that choose to use them, but the default library does not require a bundled sample pack.

## Is generation deterministic?

Yes. Equal descriptions, seeds, variation indices, quality settings, and compatible engine versions are intended to produce equal graph definitions and repeatable output. Deliberate algorithm changes may alter audio between pre-1.0 releases and will be noted in the changelog.

## Can I use it in a game engine?

Yes. Reference the platform-neutral libraries needed by the game, compile or load graphs outside the audio thread, and connect their block output to the engine's audio-device layer. The NAudio adapter is one Windows host, not a requirement for the rest of the engine.

## Can I publish generated sounds commercially?

The code is MIT licensed, and the included procedural definitions are designed to create original output. You remain responsible for reviewing your generated assets, your modifications, and any external samples you add.

## Why are there executable self-tests instead of xUnit or NUnit?

The repository keeps its test harness dependency-free. Each self-test project is an executable that fails with a non-zero exit code. This can be changed later without altering the production libraries.

## Why does the full solution require Windows?

The solution includes a WinForms editor and a WASAPI playback adapter. The core engine libraries are separated so they can be referenced without those projects.

## Are the APIs stable?

Not yet. Version 0.8.0 is a pre-1.0 release. Stable generator IDs and versioned file formats are preserved where practical, but public APIs may still be refined.

## Where should I report a problem?

Use the GitHub bug-report template for reproducible defects, Discussions for usage questions when enabled, and the process in `SECURITY.md` for vulnerabilities.
