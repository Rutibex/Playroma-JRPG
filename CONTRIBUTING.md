# Contributing

Thank you for helping build Playroma jRPG.

## Ground rules

- Preserve unknown project data unless a user explicitly removes it.
- Never commit proprietary RPG Maker assets, editor files, commercial plugins,
  or third-party game projects.
- Add synthetic fixtures instead of copied project fragments.
- Keep core format and document libraries independent of WinForms.
- Add tests for every compatibility-sensitive behavior.
- Keep nullable analysis enabled and review compiler/analyzer warnings before merging; new warnings should be justified rather than ignored.

## Development workflow

1. Open an issue for substantial design changes.
2. Create a focused branch.
3. Build the complete solution and run the test projects that cover your change (CI uses `eng/run-ci-tests.ps1`).
4. Update documentation and ADRs when architecture changes.
5. Update provenance metadata for every third-party file.
6. Submit a pull request with compatibility impact and test evidence.

## Commit guidance

Use clear imperative subjects, for example:

- `Add deterministic event-command fixture catalogue`
- `Reject encrypted deployment assets in repository audit`
- `Document raw-plus-typed document architecture`

## Compatibility changes

A pull request that changes MV-format interpretation must include:

- the affected file or command codes,
- the source used to establish behavior,
- a synthetic reproduction fixture,
- expected round-trip behavior,
- whether Strict MV Mode is affected.

## Private compatibility corpora

Real projects may be registered locally for testing but must remain outside the
repository. Use `eng/register-private-corpus.ps1`; generated private manifests
are ignored by Git.


## Sprite Studio clean-room contributions

Sprite Studio changes must follow `docs/PlayromaSprite/CLEAN_ROOM_POLICY.md`. Do not submit copied or translated Aseprite code, icons, themes, screenshots, palettes, samples, binary builds, or implementation-specific information obtained by reverse engineering. Cite public behavioural documentation and include independently authored synthetic tests.
