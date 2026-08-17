# Releasing Playroma jRPG on GitHub

## Before tagging

1. Build the complete solution on Windows with `./build.ps1`.
2. Run the public test suites (`./eng/run-ci-tests.ps1`).
3. Confirm the GitHub `CI` and `CodeQL` workflows are green on `main`.
4. Review `CHANGELOG.md`, `THIRD-PARTY-NOTICES.md`, and any changed provenance records.
5. Confirm no private RPG Maker projects, proprietary assets, credentials, build output, crash dumps, or local archives are staged.
6. Smoke-test the editor against a disposable/copy MV project, including open, edit, save, close, and reopen.

## Create a release

The repository contains `.github/workflows/release.yml`. Tags beginning with `v` trigger a Windows x64 self-contained publish and create a GitHub Release with the generated ZIP attached.

Example:

```powershell
git tag v1.0.0-rc.1
git push origin v1.0.0-rc.1
```

The workflow publishes `Playroma.JRPG.Editor.WinForms`, includes the repository licence/notices/readme in the package, creates `Playroma-jRPG-win-x64.zip`, uploads it as a workflow artifact, and creates the GitHub Release for tag-triggered runs.

## After release

- Download the release ZIP from GitHub and launch it on a clean Windows machine or VM.
- Verify the bundled documentation and GeneralUser GS licence are present where expected.
- Record user-visible changes in `CHANGELOG.md` for the next release.
- Use GitHub issues for ordinary bugs and the private security-reporting process in `SECURITY.md` for vulnerabilities.
