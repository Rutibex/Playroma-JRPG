# Publishing the repository

## First publication

1. Create an empty GitHub repository without generating a README, licence, or `.gitignore`.
2. Copy the contents of the `PlayromaSound` folder into the repository root.
3. Initialize Git, commit the files, and push the default branch.
4. Confirm that the **CI** workflow completes successfully on `windows-latest`.
5. Add a concise repository description and topics such as `procedural-audio`, `sound-synthesis`, `game-audio`, `csharp`, `dotnet`, and `winforms`.
6. Enable Discussions if community questions should be separated from bug reports.
7. Enable private vulnerability reporting in the repository Security settings when available.
8. Review issue labels used by the templates, including `bug` and `enhancement`.

Example commands after creating the empty remote repository:

```powershell
git init
git add .
git commit -m "Publish Playroma Sound 0.8.0"
git branch -M main
git remote add origin <repository-url>
git push -u origin main
```

## Release 0.8.0

After CI succeeds on the publication commit:

```powershell
git tag -a v0.8.0 -m "Playroma Sound 0.8.0"
git push origin v0.8.0
```

Create a GitHub Release from the tag and use the `0.8.0` section of `CHANGELOG.md` as the basis for its notes. GitHub automatically supplies source archives for the tag.

## Repository-specific metadata

Once the final repository address exists, it may be added to `CITATION.cff` as `repository-code` and to shared build metadata as `RepositoryUrl`. These fields were intentionally omitted from the publication package rather than filled with a placeholder address.

## Before every release

- Run `./Build.ps1` on Windows.
- Confirm the example project opens and previews audio.
- Confirm the editor export workflow produces WAV and runtime-package output.
- Update `CHANGELOG.md` and the version in `Directory.Build.props` and `CITATION.cff`.
- Verify that no `bin`, `obj`, `.vs`, generated examples, exported audio, private assets, or unlicensed samples are included.
- Tag only the exact commit that passed CI.
