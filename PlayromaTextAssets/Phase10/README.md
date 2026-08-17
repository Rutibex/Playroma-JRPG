# Phase 10 — Dialogue and Character Voices

Phase 10 introduces reusable `.playvoice.json` character-voice profiles and the `phase10.voice` recipe stage.

The intended workflow is: generate semantic dialogue intent first, then apply a voice profile. The Voice Lab edits profile-level style values, vocabulary, safe literal dialect rules, catchphrases, fillers, taboo words, and act/emotion framing. The Character Voice panel supplies speaker, listener, dialogue act, emotion, relationship affinity/trust, and arbitrary conversation-state values to the current generation context.

`example-guard.playvoice.json` is an editable profile example. `example-voiced-warning.playtext.json` shows how to apply the active voice to generated dialogue.
