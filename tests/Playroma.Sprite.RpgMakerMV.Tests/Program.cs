using System.Security.Cryptography;
using System.Text.Json;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

try
{
    RpgMakerMvProfileRegistry registry = RpgMakerMvProfileRegistry.LoadBuiltIn();
    if (registry.Profiles.Count != 27) throw new InvalidOperationException($"Expected 27 built-in MV profiles, found {registry.Profiles.Count}.");

    AssertValid(registry.GetRequired("mv-character-standard"), new("Actor1.png", "img/characters", 576, 384));
    AssertValid(registry.GetRequired("mv-character-single"), new("$Hero.png", "img/characters", 144, 192));
    AssertValid(registry.GetRequired("mv-face-sheet"), new("Actor1.png", "img/faces", 576, 288));
    AssertValid(registry.GetRequired("mv-animation-sheet"), new("Fire.png", "img/animations", 960, 3840));
    AssertValid(registry.GetRequired("mv-parallax"), new("!Ground.png", "img/parallaxes", 384, 384));
    AssertValid(registry.GetRequired("mv-iconset"), new("IconSet.png", "img/system", 512, 320));

    IReadOnlyList<MvAssetValidationIssue> badAnimation = MvAssetValidator.Validate(
        registry.GetRequired("mv-animation-sheet"), new("TooTall.png", "img/animations", 960, 4032));
    if (!badAnimation.Any(issue => issue.Code is "PSPR-MV-027" or "PSPR-MV-028"))
        throw new InvalidOperationException("Animation maximum was not enforced.");

    IReadOnlyList<MvAssetValidationIssue> wrongSystemName = MvAssetValidator.Validate(
        registry.GetRequired("mv-window"), new("MyWindow.png", "img/system", 192, 192));
    if (!wrongSystemName.Any(issue => issue.Code == "PSPR-MV-033"))
        throw new InvalidOperationException("Reserved MV system filenames were not enforced.");

    IReadOnlyList<MvAssetValidationIssue> badA1 = MvAssetValidator.Validate(
        registry.GetRequired("mv-tileset"), new("Outside_A1.png", "img/tilesets", 768, 768));
    if (!badA1.Any(issue => issue.Code == "PSPR-MV-046"))
        throw new InvalidOperationException("A1 tileset dimensions were not enforced.");
    IReadOnlyList<MvAssetValidationIssue> badB = MvAssetValidator.Validate(
        registry.GetRequired("mv-tileset"), new("Outside_B.png", "img/tilesets", 768, 720));
    if (!badB.Any(issue => issue.Code == "PSPR-MV-047"))
        throw new InvalidOperationException("B-E tileset dimensions were not enforced.");

    ValidateSyntheticFixtures(registry);
    ValidateProfileDetection(registry);
    ValidateSheetExchange(registry);
    ValidateProjectProfilesAndInstallation(registry);

    IReadOnlyList<MvPreviewMotion> actorMotions = MvAnimationPreview.GetMotions("mv-sv-actor");
    if (actorMotions.Count != 18 || actorMotions[0].DisplayName != "Walk" || actorMotions[^1].DisplayName != "Dead")
        throw new InvalidOperationException("Side-view actor motion catalogue is incomplete.");
    IReadOnlyList<IntRect> actorRects = MvAnimationPreview.ResolveSourceRects("mv-sv-actor", 576, 384, actorMotions[7]);
    if (actorRects.Count != 3 || actorRects.Any(rect => rect.Width != 64 || rect.Height != 64))
        throw new InvalidOperationException("Side-view actor source rectangles are incorrect.");

    Console.WriteLine("PASS Playroma Sprite RPG Maker MV Phase 5 complete profile-engine contracts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}

static void ValidateProfileDetection(RpgMakerMvProfileRegistry registry)
{
    MvAssetProfileDetector detector = new(registry);
    MvProfileDetectionCandidate icon = detector.DetectBest(new("IconSet.png", "img/system", 512, 320))
        ?? throw new InvalidOperationException("IconSet was not detected.");
    if (icon.Profile.Id != "mv-iconset") throw new InvalidOperationException("IconSet detected as " + icon.Profile.Id);

    MvProfileDetectionCandidate character = detector.DetectBest(new("Actor1.png", "img/characters", 576, 384))
        ?? throw new InvalidOperationException("Character sheet was not detected.");
    if (character.Profile.Id != "mv-character-standard") throw new InvalidOperationException("Character motion preview profile detected as " + character.Profile.Id);

    MvProfileDetectionCandidate single = detector.DetectBest(new("!$Door.png", "img/characters", 144, 192))
        ?? throw new InvalidOperationException("Single object character was not detected.");
    if (single.Profile.Id != "mv-character-single") throw new InvalidOperationException("Single character detected as " + single.Profile.Id);
}

static void ValidateSheetExchange(RpgMakerMvProfileRegistry registry)
{
    RpgMakerMvImageProfile singleProfile = registry.GetRequired("mv-character-single");
    List<PixelBuffer> cells = [];
    for (int index = 0; index < 12; index++)
    {
        PixelBuffer cell = new(48, 48);
        cell.Clear(new Rgba32((byte)(index * 17), (byte)(255 - index * 13), (byte)(index * 7), 255));
        cell.SetPixel(index % 48, index / 48, new Rgba32(255, 255, 255, 255));
        cells.Add(cell);
    }
    PixelBuffer sheet = MvSheetExchange.Assemble(cells, singleProfile);
    if (sheet.Width != 144 || sheet.Height != 192) throw new InvalidOperationException("Single-character sheet dimensions are wrong.");
    IReadOnlyList<PixelBuffer> split = MvSheetExchange.Split(sheet, singleProfile);
    if (split.Count != 12) throw new InvalidOperationException("Single-character split count is wrong.");
    for (int index = 0; index < cells.Count; index++)
        if (cells[index].ComputeSha256() != split[index].ComputeSha256()) throw new InvalidOperationException("Sheet split/assembly was not lossless.");

    SpritePackageData characterPackage = MvSheetExchange.SplitToAnimationPackage(sheet, singleProfile, "Hero");
    if (characterPackage.Document.Frames.Count != 12 || characterPackage.Document.Tags.Count != 4)
        throw new InvalidOperationException("Character semantic directions were not created.");
    if (characterPackage.Document.Tags.Any(tag => !tag.UserData.ContainsKey("mvDirection")))
        throw new InvalidOperationException("Character direction tags lack MV metadata.");

    RpgMakerMvImageProfile animationProfile = registry.GetRequired("mv-animation-sheet");
    PixelBuffer[] animationCells = Enumerable.Range(0, 7).Select(index =>
    {
        PixelBuffer cell = new(192, 192);
        cell.Clear(new Rgba32((byte)(index * 30), 0, 0, 255));
        return cell;
    }).ToArray();
    PixelBuffer animationSheet = MvSheetExchange.Assemble(animationCells, animationProfile,
        new MvSheetAssemblyOptions(RequireExactProfileCellCount: false));
    if (animationSheet.Width != 960 || animationSheet.Height != 384)
        throw new InvalidOperationException("Animation sheet padding is wrong.");
    AssertValid(animationProfile, new("Effect.png", "img/animations", animationSheet.Width, animationSheet.Height));

    RpgMakerMvImageProfile svProfile = registry.GetRequired("mv-sv-actor");
    PixelBuffer[] svCells = Enumerable.Range(0, 54).Select(index =>
    {
        PixelBuffer cell = new(64, 64);
        cell.Clear(new Rgba32((byte)(index * 3), (byte)(index * 5), (byte)(index * 7), 255));
        return cell;
    }).ToArray();
    PixelBuffer svSheet = MvSheetExchange.Assemble(svCells, svProfile);
    SpritePackageData svPackage = MvSheetExchange.SplitToAnimationPackage(svSheet, svProfile, "HeroSV");
    if (svPackage.Document.Tags.Count != 18 || svPackage.Document.Tags[0].UserData["mvMotionId"] != "walk")
        throw new InvalidOperationException("Side-view semantic motion tags are incomplete.");
}

static void ValidateProjectProfilesAndInstallation(RpgMakerMvProfileRegistry registry)
{
    string root = Path.Combine(Path.GetTempPath(), "PlayromaSpritePhase5-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        MvProjectProfileProvider.WriteProjectTemplate(root);
        string overridePath = Path.Combine(root, MvProjectProfileProvider.ProjectProfileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(overridePath) || Directory.EnumerateFiles(Path.GetDirectoryName(overridePath)!, "*.tmp-*").Any())
            throw new InvalidOperationException("Project profile template was not written atomically.");
        File.WriteAllText(overridePath,
            "{\n" +
            "  \"schemaVersion\": 1,\n" +
            "  \"engine\": \"RPG Maker MV\",\n" +
            "  \"profiles\": [\n" +
            "    {\n" +
            "      \"id\": \"mv-plugin-portrait\",\n" +
            "      \"displayName\": \"Plugin Portrait\",\n" +
            "      \"assetFolder\": \"img/pictures\",\n" +
            "      \"category\": \"picture\",\n" +
            "      \"runtimeFormat\": \"png\",\n" +
            "      \"validationMode\": \"strict\",\n" +
            "      \"dimensions\": { \"fixedWidth\": 300, \"fixedHeight\": 400 },\n" +
            "      \"filenameRules\": {},\n" +
            "      \"motions\": [],\n" +
            "      \"metadata\": { \"plugin\": \"contract-test\" }\n" +
            "    }\n" +
            "  ]\n" +
            "}\n");
        RpgMakerMvProfileRegistry projectProfiles = MvProjectProfileProvider.LoadForProject(root);
        AssertValid(projectProfiles.GetRequired("mv-plugin-portrait"), new("Portrait.png", "img/pictures", 300, 400));

        PixelBuffer first = new(144, 192);
        first.Clear(new Rgba32(20, 40, 60, 255));
        RpgMakerMvImageProfile single = registry.GetRequired("mv-character-single");
        MvRuntimeInstallResult initial = MvRuntimeInstaller.Install(new(root, single, "$Hero.png", first));
        if (!File.Exists(initial.FullPath) || initial.BackupPath is not null) throw new InvalidOperationException("Initial runtime installation failed.");

        PixelBuffer second = first.Clone();
        second.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));
        MvRuntimeInstallResult replaced = MvRuntimeInstaller.Install(new(root, single, "$Hero.png", second));
        if (replaced.BackupPath is null || !File.Exists(replaced.BackupPath)) throw new InvalidOperationException("Runtime replacement backup was not created.");
        if (PngCodec.Load(replaced.FullPath).GetPixel(0, 0).R != 255) throw new InvalidOperationException("Runtime replacement was not written.");

        bool escaped = false;
        try { MvRuntimeInstaller.Install(new(root, single, "../Outside.png", first)); }
        catch (InvalidDataException) { escaped = true; }
        if (!escaped) throw new InvalidOperationException("Runtime installer accepted a path traversal filename.");
        bool windowsEscaped = false;
        try { MvRuntimeInstaller.Install(new(root, single, "..\\Outside.png", first)); }
        catch (InvalidDataException) { windowsEscaped = true; }
        if (!windowsEscaped) throw new InvalidOperationException("Runtime installer accepted a Windows path traversal filename.");
        bool backupEscaped = false;
        try { MvRuntimeInstaller.Install(new(root, single, "$Hero.png", second, BackupFolderName: "../outside-backup")); }
        catch (UnauthorizedAccessException) { backupEscaped = true; }
        if (!backupEscaped) throw new InvalidOperationException("Runtime installer accepted a backup path outside the project.");

        MvProjectAssetAuditResult audit = new MvProjectAssetAuditor(projectProfiles).Audit(root);
        if (audit.Assets.Count != 1 || audit.Assets[0].DetectedProfileId != "mv-character-single" || audit.ErrorCount != 0)
            throw new InvalidOperationException("Project asset audit did not identify the installed character.");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

static void AssertValid(RpgMakerMvImageProfile profile, MvAssetValidationContext context)
{
    IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(profile, context);
    if (issues.Any(issue => issue.Severity == "error"))
        throw new InvalidOperationException(profile.Id + ": " + string.Join("; ", issues.Select(issue => issue.Message)));
}

static void ValidateSyntheticFixtures(RpgMakerMvProfileRegistry registry)
{
    string fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "manifest.json")));
    foreach (JsonElement entry in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        if (!entry.TryGetProperty("profileId", out JsonElement profileId)) continue;
        string relativePath = entry.GetProperty("path").GetString() ?? throw new InvalidDataException("Fixture path is missing.");
        string fullPath = Path.Combine(fixtureRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string expectedHash = entry.GetProperty("sha256").GetString() ?? string.Empty;
        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Fixture checksum mismatch: " + relativePath);
        PngImageInfo info = PngHeaderReader.Read(fullPath);
        if (info.Width != entry.GetProperty("width").GetInt32() || info.Height != entry.GetProperty("height").GetInt32()) throw new InvalidDataException("Fixture dimension mismatch: " + relativePath);
        RpgMakerMvImageProfile profile = registry.GetRequired(profileId.GetString() ?? string.Empty);
        IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.ValidateFile(profile, fullPath, entry.GetProperty("relativeFolder").GetString() ?? string.Empty);
        if (issues.Any(issue => issue.Severity == "error")) throw new InvalidDataException(relativePath + ": " + string.Join("; ", issues.Select(issue => issue.Message)));
    }
}
