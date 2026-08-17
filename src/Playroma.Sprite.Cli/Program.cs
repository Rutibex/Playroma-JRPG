using Playroma.Sprite.Animation;
using Playroma.Sprite.Automation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;
using Playroma.Sprite.Tilemaps;

try
{
    return await RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { PrintHelp(); return 0; }
    switch (args[0].ToLowerInvariant())
    {
        case "profiles":
            foreach (RpgMakerMvImageProfile listedProfile in RpgMakerMvProfileRegistry.LoadBuiltIn().Profiles.OrderBy(item => item.AssetFolder).ThenBy(item => item.DisplayName))
                Console.WriteLine($"{listedProfile.Id}\t{listedProfile.AssetFolder}\t{listedProfile.DisplayName}");
            return 0;
        case "new":
            if (args.Length < 2) throw new ArgumentException("Usage: new <path> [name] [width] [height]");
            string path = args[1]; string name = args.Length > 2 ? args[2] : Path.GetFileNameWithoutExtension(path);
            int width = args.Length > 3 ? int.Parse(args[3]) : 48; int height = args.Length > 4 ? int.Parse(args[4]) : 48;
            await new SpritePackageSerializer().SaveAsync(SpritePackageData.CreateEmpty(name, width, height), path);
            Console.WriteLine("Created " + Path.GetFullPath(path)); return 0;
        case "import-png":
            if (args.Length < 3) throw new ArgumentException("Usage: import-png <png-path> <package-path> [profile-id]");
            PngColorMetadata importedMetadata = PngColorMetadataReader.Read(args[1]); PixelBuffer imported = PngCodec.Load(args[1]);
            SpritePackageData importedPackage = SpritePackageData.CreateEmpty(Path.GetFileNameWithoutExtension(args[1]), imported.Width, imported.Height);
            importedPackage.ActiveBuffer().CopyFrom(imported);
            importedPackage.Document.ColorMode = importedMetadata.ColorMode switch { PngExportColorMode.Indexed => SpriteColorMode.Indexed, PngExportColorMode.Grayscale => SpriteColorMode.Grayscale, _ => SpriteColorMode.Rgba };
            importedPackage.Document.ColorProfile = importedMetadata.IsSrgb ? "sRGB" : "Unspecified RGB";
            importedPackage.Document.ColorProfileMetadata.Name = importedPackage.Document.ColorProfile; importedPackage.Document.ColorProfileMetadata.Kind = importedMetadata.IsSrgb ? "srgb" : "unspecified";
            if (importedMetadata.Gamma is double importGamma && importGamma > 0d) importedPackage.Document.ColorProfileMetadata.Gamma = importGamma;
            SpritePalette? importedPngPalette = importedMetadata.CreatePalette(Path.GetFileNameWithoutExtension(args[1]) + " Palette");
            if (importedPngPalette is not null) { importedPackage.Document.Palettes.Add(importedPngPalette); importedPackage.Document.ActivePaletteId = importedPngPalette.Id; importedPackage.Document.TransparentPaletteIndex = Math.Max(0, importedPngPalette.Entries.FindIndex(entry => entry.Color.A == 0)); }
            if (args.Length > 3) importedPackage.Document.Metadata.RpgMakerMvProfileId = args[3];
            await new SpritePackageSerializer().SaveAsync(importedPackage, args[2]);
            Console.WriteLine("Imported " + Path.GetFullPath(args[1]));
            return 0;
        case "import-sequence":
            if (args.Length < 4) throw new ArgumentException("Usage: import-sequence <package-path> <png-path> <png-path> [...]");
            IReadOnlyList<AnimationFrameImage> sequenceFrames = AnimationImageSequence.Load(args.Skip(2), 100);
            SpritePackageData sequencePackage = CreateAnimationPackage(Path.GetFileNameWithoutExtension(args[2]), sequenceFrames);
            await new SpritePackageSerializer().SaveAsync(sequencePackage, args[1]);
            Console.WriteLine($"Imported {sequenceFrames.Count} PNG frames into {Path.GetFullPath(args[1])}");
            return 0;
        case "import-gif":
            if (args.Length < 3) throw new ArgumentException("Usage: import-gif <gif-path> <package-path> [profile-id]");
            GifAnimation gif = GifCodec.Load(args[1]);
            SpritePackageData gifPackage = CreateAnimationPackage(Path.GetFileNameWithoutExtension(args[1]), gif.Frames);
            if (args.Length > 3) gifPackage.Document.Metadata.RpgMakerMvProfileId = args[3];
            await new SpritePackageSerializer().SaveAsync(gifPackage, args[2]);
            Console.WriteLine($"Imported {gif.Frames.Count} GIF frames into {Path.GetFullPath(args[2])}");
            return 0;
        case "export-png":
            if (args.Length < 3) throw new ArgumentException("Usage: export-png <package-path> <png-path> [frame-index]");
            SpritePackageData exportPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            int exportFrame = args.Length > 3 ? int.Parse(args[3]) : 0;
            PngCodec.Save(AnimationFrameRenderer.RenderFrame(exportPackage, exportFrame), args[2]);
            Console.WriteLine("Exported " + Path.GetFullPath(args[2]));
            return 0;
        case "export-sequence":
            if (args.Length < 3) throw new ArgumentException("Usage: export-sequence <package-path> <directory> [base-name]");
            SpritePackageData sequenceSource = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            string baseName = args.Length > 3 ? args[3] : Path.GetFileNameWithoutExtension(args[1]);
            AnimationFrameImage[] exportFrames = RenderAllFrames(sequenceSource);
            IReadOnlyList<string> exportedPaths = AnimationImageSequence.Save(exportFrames, args[2], baseName);
            Console.WriteLine($"Exported {exportedPaths.Count} PNG frames to {Path.GetFullPath(args[2])}");
            return 0;
        case "export-gif":
            if (args.Length < 3) throw new ArgumentException("Usage: export-gif <package-path> <gif-path> [loop-count]");
            SpritePackageData gifSource = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            int loopCount = args.Length > 3 ? int.Parse(args[3]) : 0;
            GifCodec.Save(RenderAllFrames(gifSource), args[2], loopCount);
            Console.WriteLine($"Exported {gifSource.Document.Frames.Count} GIF frames to {Path.GetFullPath(args[2])}");
            return 0;
        case "frames":
            if (args.Length < 2) throw new ArgumentException("Usage: frames <package-path>");
            SpritePackageData framePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            foreach (SpriteFrame frame in framePackage.Document.Frames.OrderBy(item => item.Number))
                Console.WriteLine($"{frame.Number}\t{frame.DurationMilliseconds} ms\t{frame.Id}");
            foreach (SpriteAnimationTag tag in framePackage.Document.Tags)
                Console.WriteLine($"TAG\t{tag.Name}\t{tag.FromFrame}-{tag.ToFrame}\t{tag.Direction}");
            return 0;
        case "validate":
            if (args.Length < 6) throw new ArgumentException("Usage: validate <profile-id> <folder> <filename> <width> <height>");
            RpgMakerMvImageProfile profile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired(args[1]);
            IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(profile, new(args[3], args[2], int.Parse(args[4]), int.Parse(args[5])));
            foreach (MvAssetValidationIssue issue in issues) Console.WriteLine($"{issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
            return issues.Any(issue => issue.Severity == "error") ? 2 : 0;
        case "validate-file":
            if (args.Length < 4) throw new ArgumentException("Usage: validate-file <profile-id> <folder> <png-path>");
            RpgMakerMvImageProfile fileProfile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired(args[1]);
            IReadOnlyList<MvAssetValidationIssue> fileIssues = MvAssetValidator.ValidateFile(fileProfile, args[3], args[2]);
            foreach (MvAssetValidationIssue issue in fileIssues) Console.WriteLine($"{issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
            return fileIssues.Any(issue => issue.Severity == "error") ? 2 : 0;
        case "detect":
            if (args.Length < 3) throw new ArgumentException("Usage: detect <folder> <png-path>");
            PngImageInfo detectedInfo = PngHeaderReader.Read(args[2]);
            IReadOnlyList<MvProfileDetectionCandidate> detected = new MvAssetProfileDetector(RpgMakerMvProfileRegistry.LoadBuiltIn()).Detect(
                new MvProfileDetectionInput(Path.GetFileName(args[2]), args[1], detectedInfo.Width, detectedInfo.Height));
            foreach (MvProfileDetectionCandidate candidate in detected)
                Console.WriteLine($"{candidate.Score}	{candidate.Confidence:P0}	{candidate.Profile.Id}	{candidate.Profile.DisplayName}");
            return detected.Count == 0 ? 2 : 0;
        case "split-sheet":
            if (args.Length < 4) throw new ArgumentException("Usage: split-sheet <profile-id> <png-path> <package-path> [duration-ms]");
            RpgMakerMvImageProfile splitProfile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired(args[1]);
            PixelBuffer splitSource = PngCodec.Load(args[2]);
            int splitDuration = args.Length > 4 ? int.Parse(args[4]) : 100;
            SpritePackageData splitPackage = MvSheetExchange.SplitToAnimationPackage(splitSource, splitProfile, Path.GetFileNameWithoutExtension(args[2]), splitDuration);
            await new SpritePackageSerializer().SaveAsync(splitPackage, args[3]);
            Console.WriteLine($"Split {splitPackage.Document.Frames.Count} cells into {Path.GetFullPath(args[3])}");
            return 0;
        case "assemble-sheet":
            if (args.Length < 4) throw new ArgumentException("Usage: assemble-sheet <profile-id> <package-path> <png-path>");
            RpgMakerMvImageProfile assembleProfile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired(args[1]);
            SpritePackageData assemblePackage = await new SpritePackageSerializer().LoadPackageAsync(args[2]);
            PixelBuffer assembled = MvSheetExchange.AssemblePackageFrames(assemblePackage, assembleProfile, options: new MvSheetAssemblyOptions(
                RequireExactProfileCellCount: assembleProfile.Id is not "mv-animation-sheet" and not "mv-iconset"));
            IReadOnlyList<MvAssetValidationIssue> assembledIssues = MvAssetValidator.Validate(assembleProfile,
                new MvAssetValidationContext(Path.GetFileName(args[3]), assembleProfile.AssetFolder, assembled.Width, assembled.Height));
            if (assembledIssues.Any(issue => issue.Severity == "error")) throw new MvAssetValidationException(assembleProfile.Id, assembledIssues);
            PngCodec.Save(assembled, args[3]);
            Console.WriteLine("Assembled " + Path.GetFullPath(args[3]));
            return 0;
        case "install":
            if (args.Length < 5) throw new ArgumentException("Usage: install <profile-id> <package-path> <project-root> <filename>");
            RpgMakerMvImageProfile installProfile = MvProjectProfileProvider.LoadForProject(args[3]).GetRequired(args[1]);
            SpritePackageData installPackage = await new SpritePackageSerializer().LoadPackageAsync(args[2]);
            PixelBuffer installPixels = installProfile.Grid is not null && installPackage.Document.Frames.Count > 1
                ? MvSheetExchange.AssemblePackageFrames(installPackage, installProfile, options: new MvSheetAssemblyOptions(
                    RequireExactProfileCellCount: installProfile.Id is not "mv-animation-sheet" and not "mv-iconset"))
                : AnimationFrameRenderer.RenderFrame(installPackage, 0);
            MvRuntimeInstallResult installed = MvRuntimeInstaller.Install(new MvRuntimeInstallRequest(args[3], installProfile, args[4], installPixels));
            Console.WriteLine(installed.RuntimeRelativePath);
            if (installed.BackupPath is not null) Console.WriteLine("Backup: " + installed.BackupPath);
            return 0;
        case "audit-project":
            if (args.Length < 2) throw new ArgumentException("Usage: audit-project <project-root>");
            RpgMakerMvProfileRegistry auditProfiles = MvProjectProfileProvider.LoadForProject(args[1]);
            MvProjectAssetAuditResult projectAudit = new MvProjectAssetAuditor(auditProfiles).Audit(args[1]);
            foreach (MvProjectAssetAuditEntry asset in projectAudit.Assets)
            {
                Console.WriteLine($"{asset.RuntimeRelativePath}	{asset.DetectedProfileId ?? "unknown"}	{asset.Confidence:P0}");
                foreach (MvAssetValidationIssue issue in asset.Issues) Console.WriteLine($"  {issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
            }
            Console.WriteLine($"Assets {projectAudit.Assets.Count}; errors {projectAudit.ErrorCount}; warnings {projectAudit.WarningCount}; unrecognized {projectAudit.UnrecognizedCount}");
            return projectAudit.Success ? 0 : 2;
        case "palette-generate":
            if (args.Length < 3) throw new ArgumentException("Usage: palette-generate <package-path> <color-count> [name]");
            SpritePackageData paletteGeneratePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            int paletteColorCount = Math.Clamp(int.Parse(args[2]), 2, 256);
            string paletteName = args.Length > 3 ? args[3] : $"{paletteColorCount}-Color Palette";
            SpritePalette generatedPalette = PaletteDocumentOperations.GeneratePalette(paletteGeneratePackage, paletteName, paletteColorCount);
            await new SpritePackageSerializer().SaveAsync(paletteGeneratePackage, args[1]);
            Console.WriteLine($"Generated {generatedPalette.Entries.Count} colors in {generatedPalette.Name}.");
            return 0;
        case "palette-import":
            if (args.Length < 3) throw new ArgumentException("Usage: palette-import <package-path> <palette-file>");
            SpritePackageData paletteImportPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            SpritePalette importedPaletteFile = PaletteFileCodec.Load(args[2]);
            paletteImportPackage.Document.Palettes.Add(importedPaletteFile); paletteImportPackage.Document.ActivePaletteId = importedPaletteFile.Id;
            int importedTransparent = importedPaletteFile.Entries.FindIndex(entry => entry.Color.A == 0); paletteImportPackage.Document.TransparentPaletteIndex = Math.Max(0, importedTransparent);
            await new SpritePackageSerializer().SaveAsync(paletteImportPackage, args[1]);
            Console.WriteLine($"Imported {importedPaletteFile.Entries.Count} colors.");
            return 0;
        case "palette-export":
            if (args.Length < 3) throw new ArgumentException("Usage: palette-export <package-path> <palette-file>");
            SpritePackageData paletteExportPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            SpritePalette paletteToExport = paletteExportPackage.Document.ActivePaletteId is Guid exportPaletteId
                ? paletteExportPackage.Document.Palettes.First(palette => palette.Id == exportPaletteId)
                : paletteExportPackage.Document.Palettes.FirstOrDefault() ?? throw new InvalidOperationException("The document contains no palette.");
            PaletteFileCodec.Save(paletteToExport, args[2]); Console.WriteLine(Path.GetFullPath(args[2])); return 0;
        case "palette-sort":
            if (args.Length < 3) throw new ArgumentException("Usage: palette-sort <package-path> <index|hue|saturation|lightness|luminance|red|green|blue|alpha|name> [descending]");
            SpritePackageData sortPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            SpritePalette sortPalette = sortPackage.Document.ActivePaletteId is Guid sortPaletteId
                ? sortPackage.Document.Palettes.First(palette => palette.Id == sortPaletteId)
                : sortPackage.Document.Palettes.FirstOrDefault() ?? throw new InvalidOperationException("The document contains no palette.");
            if (!Enum.TryParse(args[2], true, out PaletteSortMode sortMode)) throw new ArgumentException($"Unknown palette sort mode '{args[2]}'.");
            bool descending = args.Length > 3 && bool.TryParse(args[3], out bool requestedDescending) && requestedDescending;
            PaletteSorting.Sort(sortPalette, sortMode, descending);
            int sortedTransparent = sortPalette.Entries.FindIndex(entry => entry.Color.A == 0); if (sortedTransparent >= 0) sortPackage.Document.TransparentPaletteIndex = sortedTransparent;
            await new SpritePackageSerializer().SaveAsync(sortPackage, args[1]); Console.WriteLine($"Sorted {sortPalette.Name} by {sortMode}{(descending ? " descending" : string.Empty)}."); return 0;
        case "color-profile":
            if (args.Length < 4) throw new ArgumentException("Usage: color-profile <package-path> <assign|convert> <srgb|linear-srgb|gamma18|gamma24|unspecified>");
            SpritePackageData profilePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); BuiltInColorProfile targetProfile = ColorProfileOperations.GetRequired(args[3]);
            if (args[2].Equals("assign", StringComparison.OrdinalIgnoreCase)) ColorProfileOperations.Assign(profilePackage.Document, targetProfile);
            else if (args[2].Equals("convert", StringComparison.OrdinalIgnoreCase)) ColorProfileOperations.Convert(profilePackage, targetProfile);
            else throw new ArgumentException("Color-profile operation must be assign or convert.");
            await new SpritePackageSerializer().SaveAsync(profilePackage, args[1]); Console.WriteLine(profilePackage.Document.ColorProfile); return 0;
        case "convert-mode":
            if (args.Length < 3) throw new ArgumentException("Usage: convert-mode <package-path> <rgba|indexed|grayscale> [colors] [none|ordered|floyd]");
            SpritePackageData convertPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            string requestedMode = args[2].ToLowerInvariant();
            if (requestedMode == "rgba") PaletteDocumentOperations.ConvertToRgba(convertPackage);
            else if (requestedMode == "grayscale") PaletteDocumentOperations.ConvertToGrayscale(convertPackage);
            else if (requestedMode == "indexed")
            {
                int requestedColors = args.Length > 3 ? Math.Clamp(int.Parse(args[3]), 2, 256) : 256;
                PaletteDitherMode requestedDither = args.Length > 4 ? ParseDither(args[4]) : PaletteDitherMode.None;
                SpritePalette convertPalette = convertPackage.Document.ActivePaletteId is Guid activePaletteId
                    ? convertPackage.Document.Palettes.First(palette => palette.Id == activePaletteId)
                    : PaletteDocumentOperations.GeneratePalette(convertPackage, $"{requestedColors}-Color Palette", requestedColors);
                PaletteDocumentOperations.ApplyPalette(convertPackage, convertPalette, requestedDither);
            }
            else throw new ArgumentException("Color mode must be rgba, indexed, or grayscale.");
            await new SpritePackageSerializer().SaveAsync(convertPackage, args[1]); Console.WriteLine(convertPackage.Document.ColorMode); return 0;
        case "effect":
            if (args.Length < 3) throw new ArgumentException("Usage: effect <package-path> <invert|grayscale|blur|sharpen|pixelate|alpha-cleanup> [amount]");
            SpritePackageData effectPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            string effectName = args[2].ToLowerInvariant(); double effectAmount = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1d;
            foreach (PixelBuffer effectBuffer in effectPackage.PixelBuffers.Values)
            {
                switch (effectName)
                {
                    case "invert": AdvancedPixelEffects.Invert(effectBuffer); break;
                    case "grayscale": AdvancedPixelEffects.ConvertToGrayscale(effectBuffer); break;
                    case "blur": AdvancedPixelEffects.GaussianBlur(effectBuffer, Math.Max(1, (int)Math.Round(effectAmount))); break;
                    case "sharpen": AdvancedPixelEffects.Sharpen(effectBuffer, effectAmount); break;
                    case "pixelate": AdvancedPixelEffects.Pixelate(effectBuffer, Math.Max(2, (int)Math.Round(effectAmount))); break;
                    case "alpha-cleanup": AdvancedPixelEffects.AlphaThreshold(effectBuffer, (byte)Math.Clamp((int)Math.Round(effectAmount), 0, 255)); break;
                    default: throw new ArgumentException($"Unknown effect '{effectName}'.");
                }
            }
            await new SpritePackageSerializer().SaveAsync(effectPackage, args[1]); Console.WriteLine($"Applied {effectName} to {effectPackage.PixelBuffers.Count} pixel payloads."); return 0;
        case "export-png-mode":
            if (args.Length < 4) throw new ArgumentException("Usage: export-png-mode <package-path> <png-path> <rgba|indexed|grayscale> [frame-index]");
            SpritePackageData modeExportPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); int modeFrame = args.Length > 4 ? int.Parse(args[4]) : 0;
            PixelBuffer modePixels = AnimationFrameRenderer.RenderFrame(modeExportPackage, modeFrame); string modeName = args[3].ToLowerInvariant();
            PngExportOptions modeOptions = modeName switch
            {
                "grayscale" => new(PngExportColorMode.Grayscale),
                "indexed" => new(PngExportColorMode.Indexed, modeExportPackage.Document.ActivePaletteId is Guid paletteId ? PaletteDocumentOperations.ToColors(modeExportPackage.Document.Palettes.First(palette => palette.Id == paletteId)) : null, PaletteDitherMode.None, modeExportPackage.Document.TransparentPaletteIndex),
                _ => new(PngExportColorMode.Rgba)
            };
            PngCodec.Save(modePixels, args[2], modeOptions); Console.WriteLine(Path.GetFullPath(args[2])); return 0;
        case "tileset-import":
            if (args.Length < 5) throw new ArgumentException("Usage: tileset-import <package-path> <png-path> <tile-width> <tile-height> [name]");
            SpritePackageData tilesetPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); PixelBuffer tilesetPixels = PngCodec.Load(args[2]);
            SpriteTileset importedTileset = TilemapDocumentOperations.ImportTileset(tilesetPackage, tilesetPixels, new(args.Length > 5 ? args[5] : Path.GetFileNameWithoutExtension(args[2]), int.Parse(args[3]), int.Parse(args[4]), SourceImagePath: Path.GetFullPath(args[2])));
            await new SpritePackageSerializer().SaveAsync(tilesetPackage, args[1]); Console.WriteLine($"{importedTileset.Id}	{importedTileset.Tiles.Count} tiles"); return 0;
        case "tilemap-new":
            if (args.Length < 3) throw new ArgumentException("Usage: tilemap-new <package-path> <tileset-id-or-index> [columns] [rows]");
            SpritePackageData tilemapPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); SpriteTileset selectedTileset = ResolveTileset(tilemapPackage.Document, args[2]);
            int? columns = args.Length > 3 ? int.Parse(args[3]) : null; int? rows = args.Length > 4 ? int.Parse(args[4]) : null;
            SpriteLayer newTilemap = TilemapDocumentOperations.AddTilemapLayer(tilemapPackage, selectedTileset.Id, tilemapPackage.Document.Frames[0].Id, "Tilemap", columns, rows); await new SpritePackageSerializer().SaveAsync(tilemapPackage, args[1]); Console.WriteLine(newTilemap.Id); return 0;
        case "tilemap-render":
            if (args.Length < 4) throw new ArgumentException("Usage: tilemap-render <package-path> <layer-id-or-index> <png-path> [frame-index]");
            SpritePackageData renderTilemapPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); SpriteLayer renderLayer = ResolveTilemapLayer(renderTilemapPackage.Document, args[2]); int renderFrameIndex = args.Length > 4 ? int.Parse(args[4]) : 0; SpriteFrame renderFrame = renderTilemapPackage.Document.Frames[renderFrameIndex]; SpriteTilemapCel renderCel = renderLayer.TilemapCels.First(item => item.FrameId == renderFrame.Id); PngCodec.Save(TilemapRasterizer.Render(renderTilemapPackage.Document, renderTilemapPackage.PixelBuffers, renderLayer, renderCel), args[3]); Console.WriteLine(Path.GetFullPath(args[3])); return 0;
        case "slice-add":
            if (args.Length < 7) throw new ArgumentException("Usage: slice-add <package-path> <name> <x> <y> <width> <height> [frame]");
            SpritePackageData slicePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); int sliceFrame = args.Length > 7 ? int.Parse(args[7]) : 0; SpriteSlice addedSlice = SliceDocumentOperations.Add(slicePackage.Document, args[2], new(int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])), sliceFrame); await new SpritePackageSerializer().SaveAsync(slicePackage, args[1]); Console.WriteLine(addedSlice.Id); return 0;
        case "slices-export":
            if (args.Length < 3) throw new ArgumentException("Usage: slices-export <package-path> <json-path>");
            SpritePackageData slicesPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); File.WriteAllText(args[2], SliceDocumentOperations.ExportMetadata(slicesPackage.Document)); Console.WriteLine(Path.GetFullPath(args[2])); return 0;
        case "tile-bridge":
            if (args.Length < 6) throw new ArgumentException("Usage: tile-bridge <package-path> <directory> <name> <tile-width> <tile-height> [mode]");
            SpritePackageData bridgePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); TileStudioBridgeRequest bridge = TileStudioBridge.Export(bridgePackage, args[2], args[3], int.Parse(args[4]), int.Parse(args[5]), args.Length > 6 ? args[6] : "tileset"); Console.WriteLine(bridge.ManifestPath); return 0;
        case "system-assets":
            foreach (MvSystemAssetDefinition definition in MvSystemAssetCatalog.Definitions)
                Console.WriteLine($"{definition.ProfileId}\t{definition.ReservedFileName}\t{definition.StockWidth}x{definition.StockHeight}\t{definition.DisplayName}");
            return 0;
        case "system-template":
            if (args.Length < 3) throw new ArgumentException("Usage: system-template <profile-id> <png-path>");
            MvSystemAssetDefinition templateDefinition = MvSystemAssetCatalog.GetRequired(args[1]);
            PngCodec.Save(MvSystemAssetTemplateFactory.Create(templateDefinition), args[2]);
            Console.WriteLine(Path.GetFullPath(args[2])); return 0;
        case "system-preview":
            if (args.Length < 4) throw new ArgumentException("Usage: system-preview <profile-id> <source-png> <preview-png> [item] [frame] [damage-text]");
            MvSystemAssetDefinition previewDefinition = MvSystemAssetCatalog.GetRequired(args[1]);
            PixelBuffer previewSource = PngCodec.Load(args[2]);
            int previewItem = args.Length > 4 ? int.Parse(args[4]) : 0; int previewFrame = args.Length > 5 ? int.Parse(args[5]) : 0; string previewDamage = args.Length > 6 ? args[6] : "1234";
            PixelBuffer previewPixels = MvSystemAssetPreviewRenderer.Render(previewSource, previewDefinition, new(previewItem, previewFrame, previewDamage));
            PngCodec.Save(previewPixels, args[3]); Console.WriteLine(Path.GetFullPath(args[3])); return 0;
        case "system-regions":
            if (args.Length < 2) throw new ArgumentException("Usage: system-regions <profile-id> [width] [height]");
            MvSystemAssetDefinition regionDefinition = MvSystemAssetCatalog.GetRequired(args[1]);
            int regionWidth = args.Length > 2 ? int.Parse(args[2]) : regionDefinition.StockWidth; int regionHeight = args.Length > 3 ? int.Parse(args[3]) : regionDefinition.StockHeight;
            foreach (MvSystemAssetRegion region in MvSystemAssetCatalog.GetRegions(regionDefinition, regionWidth, regionHeight))
                Console.WriteLine($"{region.Id}\t{region.DisplayName}\t{region.Bounds.X},{region.Bounds.Y},{region.Bounds.Width},{region.Bounds.Height}\titem={region.Index}\tframe={region.Frame}");
            return 0;
        case "system-validate":
            if (args.Length < 5) throw new ArgumentException("Usage: system-validate <profile-id> <filename> <width> <height>");
            MvSystemAssetDefinition validationDefinition = MvSystemAssetCatalog.GetRequired(args[1]);
            IReadOnlyList<MvSystemAssetDiagnostic> systemIssues = MvSystemAssetCatalog.Validate(validationDefinition, args[2], int.Parse(args[3]), int.Parse(args[4]));
            foreach (MvSystemAssetDiagnostic issue in systemIssues) Console.WriteLine($"{issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}");
            return systemIssues.Any(issue => issue.Severity == "error") ? 2 : 0;
        case "pipeline-run":
            if (args.Length < 3) throw new ArgumentException("Usage: pipeline-run <package-path> <pipeline-json> [workspace] [--dry-run]");
            SpritePackageData pipelinePackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            SpriteAutomationPipeline pipeline = System.Text.Json.JsonSerializer.Deserialize<SpriteAutomationPipeline>(File.ReadAllText(args[2]), AutomationJson.Options) ?? throw new InvalidDataException("Pipeline file is empty.");
            bool pipelineDry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase); string pipelineWorkspace = args.Skip(3).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            AutomationRunResult pipelineResult = await new SpriteAutomationEngine().RunAsync(pipeline, CreateAutomationContext(pipelinePackage, args[1], pipelineWorkspace, pipelineDry));
            PrintAutomationResult(pipelineResult); return pipelineResult.Success ? 0 : 2;
        case "script-run":
            if (args.Length < 3) throw new ArgumentException("Usage: script-run <package-path> <script-json> [name=value ...] [--dry-run]");
            SpritePackageData scriptPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); PlayromaSpriteScript script = PlayromaSpriteScript.Load(args[2]);
            Dictionary<string, string> scriptParameters = ParseNamedParameters(args.Skip(3));
            bool scriptDry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase); string scriptWorkspace = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            AutomationRunResult scriptResult = await new SpriteAutomationEngine().RunScriptAsync(script, CreateAutomationContext(scriptPackage, args[1], scriptWorkspace, scriptDry), scriptParameters);
            PrintAutomationResult(scriptResult); return scriptResult.Success ? 0 : 2;
        case "script-template":
            if (args.Length < 2) throw new ArgumentException("Usage: script-template <json-path>"); File.WriteAllText(args[1], SpriteScriptTemplates.Starter); Console.WriteLine(Path.GetFullPath(args[1])); return 0;
        case "preset-run":
            if (args.Length < 3) throw new ArgumentException("Usage: preset-run <package-path> <preset-json> [workspace] [--dry-run]");
            SpritePackageData presetPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); SpriteExportPreset preset = System.Text.Json.JsonSerializer.Deserialize<SpriteExportPreset>(File.ReadAllText(args[2]), AutomationJson.Options) ?? throw new InvalidDataException("Preset file is empty.");
            bool presetDry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase); string presetWorkspace = args.Skip(3).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal)) ?? Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            AutomationRunResult presetResult = await new SpriteAutomationEngine().RunPresetAsync(preset, CreateAutomationContext(presetPackage, args[1], presetWorkspace, presetDry)); PrintAutomationResult(presetResult); return presetResult.Success ? 0 : 2;
        case "atlas":
            if (args.Length < 3) throw new ArgumentException("Usage: atlas <package-path> <png-path> [padding] [power-of-two] [trim] [extrude]");
            SpritePackageData atlasPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]); SpriteExportPreset atlasPreset = new() { Format = "atlas", OutputPath = Path.GetFullPath(args[2]), Padding = args.Length > 3 ? int.Parse(args[3]) : 1, PowerOfTwo = args.Length > 4 && bool.Parse(args[4]), TrimTransparent = args.Length > 5 && bool.Parse(args[5]), Extrude = args.Length > 6 && bool.Parse(args[6]) };
            SpriteAtlasResult atlasResult = SpriteAtlasPacker.Pack(atlasPackage, atlasPreset); PngCodec.Save(atlasResult.Image, args[2]); File.WriteAllText(Path.ChangeExtension(args[2], ".json"), atlasResult.MetadataJson); Console.WriteLine($"{atlasResult.Frames.Count} frames -> {Path.GetFullPath(args[2])}"); return 0;
        case "batch-run":
            if (args.Length < 4) throw new ArgumentException("Usage: batch-run <pipeline-json> <workspace> <package-glob> [--dry-run]");
            SpriteAutomationPipeline batchPipeline = System.Text.Json.JsonSerializer.Deserialize<SpriteAutomationPipeline>(File.ReadAllText(args[1]), AutomationJson.Options) ?? throw new InvalidDataException("Pipeline file is empty.");
            string batchRoot = Path.GetFullPath(args[2]); bool batchDry = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase); int batchFailures = 0;
            foreach (string batchPath in Directory.EnumerateFiles(batchRoot, args[3], SearchOption.AllDirectories)) { SpritePackageData batchPackage = await new SpritePackageSerializer().LoadPackageAsync(batchPath); AutomationRunResult batchResult = await new SpriteAutomationEngine().RunAsync(batchPipeline, CreateAutomationContext(batchPackage, batchPath, batchRoot, batchDry)); Console.WriteLine($"[{(batchResult.Success ? "PASS" : "FAIL")}] {batchPath}"); if (!batchResult.Success) batchFailures++; }
            return batchFailures == 0 ? 0 : 2;
        case "extensions":
            if (args.Length < 2) throw new ArgumentException("Usage: extensions <extension-directory> [settings-json]");
            SpriteExtensionSettings extensionSettings = args.Length > 2 ? SpriteExtensionSettings.Load(args[2]) : new(); foreach (SpriteExtensionDescriptor extension in SpriteExtensionCatalog.Discover(args[1], extensionSettings.Trusted, extensionSettings.Enabled)) Console.WriteLine($"{extension.Manifest.Id}	{extension.Manifest.Version}	trusted={extension.Trusted}	enabled={extension.Enabled}	{extension.Error ?? extension.Manifest.Name}"); return 0;
        case "automation-validate":
            if (args.Length < 2) throw new ArgumentException("Usage: automation-validate <pipeline|script|preset|extension-json>");
            string automationJson = File.ReadAllText(args[1]); string automationSchema = System.Text.Json.JsonDocument.Parse(automationJson).RootElement.GetProperty("schema").GetString() ?? string.Empty;
            switch (automationSchema) { case "playroma-sprite-pipeline/1": _ = System.Text.Json.JsonSerializer.Deserialize<SpriteAutomationPipeline>(automationJson, AutomationJson.Options) ?? throw new InvalidDataException(); break; case "playroma-sprite-script/1": PlayromaSpriteScript.Validate(PlayromaSpriteScript.Parse(automationJson)); break; case "playroma-sprite-export-preset/1": ExportPresetStore.Validate(System.Text.Json.JsonSerializer.Deserialize<SpriteExportPreset>(automationJson, AutomationJson.Options) ?? throw new InvalidDataException()); break; case "playroma-sprite-extension/1": SpriteExtensionCatalog.ValidateManifest(System.Text.Json.JsonSerializer.Deserialize<SpriteExtensionManifest>(automationJson, AutomationJson.Options) ?? throw new InvalidDataException()); break; default: throw new InvalidDataException($"Unknown automation schema '{automationSchema}'."); }
            Console.WriteLine("Automation document is valid."); return 0;
        case "format-list":
            if (args.Length < 3) throw new ArgumentException("Usage: format-list <extension-directory> <settings-json>");
            using (SpriteExtensionRegistry formatRegistry = LoadExtensionRegistry(args[1], args[2]))
                foreach (IPlayromaSpriteFileFormatProvider provider in formatRegistry.FormatProviders) Console.WriteLine($"{provider.Id}	read={provider.CanRead}	write={provider.CanWrite}	{string.Join(',', provider.Extensions)}	{provider.DisplayName}");
            return 0;
        case "format-export":
            if (args.Length < 7) throw new ArgumentException("Usage: format-export <package-path> <extension-directory> <settings-json> <provider-id> <output-path> <overwrite>");
            SpritePackageData customExportPackage = await new SpritePackageSerializer().LoadPackageAsync(args[1]);
            using (SpriteExtensionRegistry customExportRegistry = LoadExtensionRegistry(args[2], args[3]))
            {
                string customExportWorkspace = Path.GetDirectoryName(Path.GetFullPath(args[5]))!;
                SpriteAutomationStep customExportStep = new() { Id = "format", Command = "export-format", Arguments = new() { ["provider"] = System.Text.Json.JsonSerializer.SerializeToElement(args[4]), ["path"] = System.Text.Json.JsonSerializer.SerializeToElement(Path.GetFileName(args[5])), ["overwrite"] = System.Text.Json.JsonSerializer.SerializeToElement(bool.Parse(args[6])) } };
                AutomationRunResult customExportResult = await new SpriteAutomationEngine(customExportRegistry).RunAsync(new SpriteAutomationPipeline { Name = "Custom Format Export", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, Steps = [customExportStep] }, new SpriteAutomationContext { Package = customExportPackage, PackagePath = Path.GetFullPath(args[1]), WorkspaceRoot = customExportWorkspace, GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, Log = Console.WriteLine });
                PrintAutomationResult(customExportResult); return customExportResult.Success ? 0 : 2;
            }
        case "format-import":
            if (args.Length < 7) throw new ArgumentException("Usage: format-import <input-path> <extension-directory> <settings-json> <provider-id> <package-output> <overwrite>");
            using (SpriteExtensionRegistry customImportRegistry = LoadExtensionRegistry(args[2], args[3]))
            {
                string customImportWorkspace = Path.GetDirectoryName(Path.GetFullPath(args[1]))!; SpritePackageData placeholder = SpritePackageData.CreateEmpty("Imported Sprite", 1, 1);
                SpriteAutomationStep customImportStep = new() { Id = "format", Command = "import-format", Arguments = new() { ["provider"] = System.Text.Json.JsonSerializer.SerializeToElement(args[4]), ["path"] = System.Text.Json.JsonSerializer.SerializeToElement(Path.GetFileName(args[1])) } };
                SpriteAutomationContext customImportContext = new() { Package = placeholder, WorkspaceRoot = customImportWorkspace, GrantedCapabilities = SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.LoadExtensions, Log = Console.WriteLine };
                AutomationRunResult customImportResult = await new SpriteAutomationEngine(customImportRegistry).RunAsync(new SpriteAutomationPipeline { Name = "Custom Format Import", RequiredCapabilities = SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.LoadExtensions, Steps = [customImportStep] }, customImportContext);
                PrintAutomationResult(customImportResult); if (!customImportResult.Success) return 2; if (File.Exists(args[5]) && !bool.Parse(args[6])) throw new IOException("Package output already exists."); await new SpritePackageSerializer().SaveAsync(customImportContext.Package, args[5]); return 0;
            }
        case "clean-room-audit":
            string root = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
            CleanRoomAuditResult audit = new CleanRoomAudit().Audit(root);
            foreach (CleanRoomFinding finding in audit.Findings) Console.WriteLine($"ERROR {finding.Code} {finding.Path}: {finding.Message}");
            Console.WriteLine(audit.Success ? "Clean-room audit passed." : "Clean-room audit failed.");
            return audit.Success ? 0 : 3;
        default: throw new ArgumentException($"Unknown command '{args[0]}'.");
    }
}



static SpriteExtensionRegistry LoadExtensionRegistry(string extensionDirectory, string settingsPath)
{
    SpriteExtensionSettings settings = SpriteExtensionSettings.Load(settingsPath);
    SpriteExtensionRegistry registry = new(SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, Console.WriteLine);
    try
    {
        foreach (SpriteExtensionDescriptor descriptor in SpriteExtensionCatalog.Discover(extensionDirectory, settings.Trusted, settings.Enabled).Where(item => item.Trusted && item.Enabled && item.Error is null)) registry.Load(descriptor);
        return registry;
    }
    catch { registry.Dispose(); throw; }
}

static SpriteAutomationContext CreateAutomationContext(SpritePackageData package, string packagePath, string workspace, bool dryRun) => new()
{
    Package = package,
    PackagePath = Path.GetFullPath(packagePath),
    WorkspaceRoot = Path.GetFullPath(workspace),
    OutputRoot = "exports",
    DryRun = dryRun,
    GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.InstallRuntimeAsset,
    Log = Console.WriteLine
};

static void PrintAutomationResult(AutomationRunResult result)
{
    foreach (AutomationDiagnostic diagnostic in result.Diagnostics) Console.WriteLine($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}: {diagnostic.Message}");
    foreach (AutomationOperation operation in result.Operations) Console.WriteLine($"{(operation.Performed ? "DONE" : "PLAN")} {operation.Kind}: {operation.Description} {operation.DestinationPath}");
    Console.WriteLine(result.Success ? "Automation completed successfully." : "Automation completed with errors.");
}

static SpritePackageData CreateAnimationPackage(string name, IReadOnlyList<AnimationFrameImage> frames)
{
    if (frames.Count == 0) throw new InvalidDataException("The animation contains no frames.");
    int width = frames[0].Pixels.Width, height = frames[0].Pixels.Height;
    if (frames.Any(frame => frame.Pixels.Width != width || frame.Pixels.Height != height))
        throw new InvalidDataException("All animation frames must have identical dimensions.");
    SpritePackageData package = SpritePackageData.CreateEmpty(name, width, height);
    SpriteLayer layer = package.Document.Layers[0];
    package.Document.Frames.Clear(); package.Document.PixelContents.Clear(); layer.Cels.Clear(); package.PixelBuffers.Clear();
    for (int index = 0; index < frames.Count; index++)
    {
        SpriteFrame frame = new() { Number = index, DurationMilliseconds = Math.Max(1, frames[index].DurationMilliseconds) };
        SpritePixelContentDescriptor descriptor = new() { Width = width, Height = height };
        package.Document.Frames.Add(frame); package.Document.PixelContents.Add(descriptor);
        package.PixelBuffers[descriptor.Id] = frames[index].Pixels.Clone();
        layer.Cels.Add(new SpriteCel { FrameId = frame.Id, PixelContentId = descriptor.Id });
    }
    if (frames.Count > 1)
        package.Document.Tags.Add(new SpriteAnimationTag { Name = "Animation", FromFrame = 0, ToFrame = frames.Count - 1 });
    SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    return package;
}

static AnimationFrameImage[] RenderAllFrames(SpritePackageData package) =>
    package.Document.Frames.Select((frame, index) => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(package, index), frame.DurationMilliseconds)).ToArray();

static Dictionary<string, string> ParseNamedParameters(IEnumerable<string> values)
{
    Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
    foreach (string value in values)
    {
        if (value.StartsWith("--", StringComparison.Ordinal)) continue;
        int separator = value.IndexOf('=');
        if (separator <= 0) continue;
        parameters[value[..separator]] = value[(separator + 1)..];
    }
    return parameters;
}

static PaletteDitherMode ParseDither(string value) => value.ToLowerInvariant() switch
{
    "ordered" or "bayer" => PaletteDitherMode.OrderedBayer4x4,
    "floyd" or "floyd-steinberg" => PaletteDitherMode.FloydSteinberg,
    _ => PaletteDitherMode.None
};

static void PrintHelp()
{
    Console.WriteLine("Playroma Sprite Studio CLI");
    Console.WriteLine("Commands include pipelines, scripts, presets, atlases, batch processing, and extension discovery.");
    Console.WriteLine("  profiles");
    Console.WriteLine("  new <path> [name] [width] [height]");
    Console.WriteLine("  import-png <png-path> <package-path> [profile-id]");
    Console.WriteLine("  import-sequence <package-path> <png-path> <png-path> [...]");
    Console.WriteLine("  import-gif <gif-path> <package-path> [profile-id]");
    Console.WriteLine("  export-png <package-path> <png-path> [frame-index]");
    Console.WriteLine("  export-sequence <package-path> <directory> [base-name]");
    Console.WriteLine("  export-gif <package-path> <gif-path> [loop-count]");
    Console.WriteLine("  frames <package-path>");
    Console.WriteLine("  validate <profile-id> <folder> <filename> <width> <height>");
    Console.WriteLine("  validate-file <profile-id> <folder> <png-path>");
    Console.WriteLine("  detect <folder> <png-path>");
    Console.WriteLine("  split-sheet <profile-id> <png-path> <package-path> [duration-ms]");
    Console.WriteLine("  assemble-sheet <profile-id> <package-path> <png-path>");
    Console.WriteLine("  install <profile-id> <package-path> <project-root> <filename>");
    Console.WriteLine("  audit-project <project-root>");
    Console.WriteLine("  palette-generate <package-path> <color-count> [name]");
    Console.WriteLine("  palette-import <package-path> <palette-file>");
    Console.WriteLine("  palette-export <package-path> <palette-file>");
    Console.WriteLine("  palette-sort <package-path> <index|hue|saturation|lightness|luminance|red|green|blue|alpha|name> [descending]");
    Console.WriteLine("  color-profile <package-path> <assign|convert> <srgb|linear-srgb|gamma18|gamma24|unspecified>");
    Console.WriteLine("  convert-mode <package-path> <rgba|indexed|grayscale> [colors] [none|ordered|floyd]");
    Console.WriteLine("  effect <package-path> <invert|grayscale|blur|sharpen|pixelate|alpha-cleanup> [amount]");
    Console.WriteLine("  export-png-mode <package-path> <png-path> <rgba|indexed|grayscale> [frame-index]");
    Console.WriteLine("  tileset-import <package-path> <png-path> <tile-width> <tile-height> [name]");
    Console.WriteLine("  tilemap-new <package-path> <tileset-id-or-index> [columns] [rows]");
    Console.WriteLine("  tilemap-render <package-path> <layer-id-or-index> <png-path> [frame-index]");
    Console.WriteLine("  slice-add <package-path> <name> <x> <y> <width> <height> [frame]");
    Console.WriteLine("  slices-export <package-path> <json-path>");
    Console.WriteLine("  tile-bridge <package-path> <directory> <name> <tile-width> <tile-height> [mode]");
    Console.WriteLine("  system-assets");
    Console.WriteLine("  system-template <profile-id> <png-path>");
    Console.WriteLine("  system-preview <profile-id> <source-png> <preview-png> [item] [frame] [damage-text]");
    Console.WriteLine("  system-regions <profile-id> [width] [height]");
    Console.WriteLine("  system-validate <profile-id> <filename> <width> <height>");
    Console.WriteLine("  pipeline-run <package-path> <pipeline-json> [workspace] [--dry-run]");
    Console.WriteLine("  script-run <package-path> <script-json> [name=value ...] [--dry-run]");
    Console.WriteLine("  script-template <json-path>");
    Console.WriteLine("  preset-run <package-path> <preset-json> [workspace] [--dry-run]");
    Console.WriteLine("  atlas <package-path> <png-path> [padding] [power-of-two] [trim] [extrude]");
    Console.WriteLine("  batch-run <pipeline-json> <workspace> <package-glob> [--dry-run]");
    Console.WriteLine("  extensions <extension-directory> [settings-json]");
    Console.WriteLine("  automation-validate <automation-json>");
    Console.WriteLine("  format-list <extension-directory> <settings-json>");
    Console.WriteLine("  format-export <package-path> <extension-directory> <settings-json> <provider-id> <output-path> <overwrite>");
    Console.WriteLine("  format-import <input-path> <extension-directory> <settings-json> <provider-id> <package-output> <overwrite>");
    Console.WriteLine("  clean-room-audit [repository-root]");
}

static SpriteTileset ResolveTileset(SpriteDocument document, string value)
{
    if (Guid.TryParse(value, out Guid id)) return document.Tilesets.First(item => item.Id == id);
    int index = int.Parse(value); return document.Tilesets[index];
}

static SpriteLayer ResolveTilemapLayer(SpriteDocument document, string value)
{
    if (Guid.TryParse(value, out Guid id)) return document.Layers.First(item => item.Id == id && item.Kind == SpriteLayerKind.Tilemap);
    int index = int.Parse(value); return document.Layers.Where(item => item.Kind == SpriteLayerKind.Tilemap).ElementAt(index);
}

static class CliPackageExtensions
{
    public static PixelBuffer ActiveBuffer(this SpritePackageData package)
    {
        SpritePixelContentDescriptor descriptor = package.Document.PixelContents.First();
        return package.GetRequired(descriptor.Id);
    }
}
