using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Autotiles.Rendering;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Autotiles.Validation;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Export;
using Playroma.Tile.RpgMakerMV.Import;
using Playroma.Tile.RpgMakerMV.Validation;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Integration;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Procedural;
using Playroma.Tile.Materials.Recipes;
using Playroma.Tile.Materials.Serialization;
using Playroma.Tile.Materials.Services;
using Playroma.Tile.Materials.Validation;
using Playroma.Tile.Tilesets.Export;
using Playroma.Tile.Tilesets.Integration;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Rendering;
using Playroma.Tile.Tilesets.Services;
using Playroma.Tile.Tilesets.Validation;
using Playroma.Tile.RpgMakerMV.Imaging;

namespace Playroma.Tile.SmokeTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        var sampleDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) : FindSampleDirectory();
        if (!Directory.Exists(sampleDirectory))
        {
            Console.Error.WriteLine($"Sample directory not found: {sampleDirectory}");
            return 2;
        }

        var testRoot = Path.Combine(Path.GetTempPath(), "PlayromaTileSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var projectPath = Path.Combine(testRoot, "Smoke.playromatile.json");
            var project = ProjectSerializer.CreateNew(projectPath, "Smoke Test");
            var importer = new MvImportService();
            var importResult = importer.ImportFolder(project, sampleDirectory, recursive: false);
            ProjectSerializer.Save(project);

            if (importResult.ImportedSheets.Count != 9)
                throw new InvalidOperationException("Expected all nine MV sheet slots to import.");

            var validator = new MvValidator();
            var issues = validator.ValidateProject(project);
            if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
                throw new InvalidOperationException("Phase 1 validation produced errors.");

            TestShapeResolver();
            TestAutotileAuthoring(project);
            TestAllAutotileKinds();
            TestInverseCompiler(project);
            TestMaterialLaboratory(project);
            TestTilesetConstruction(project, testRoot);

            ProjectSerializer.Save(project);
            var reloaded = ProjectSerializer.Load(projectPath);
            if (reloaded.FormatVersion != 4 || reloaded.Autotiles.Count != 2 || reloaded.Materials.Count != 1 || reloaded.TilesetConstructions.Count != 1)
                throw new InvalidOperationException("Phase 4 project data did not round-trip.");

            var exportDirectory = Path.Combine(testRoot, "Export");
            var exporter = new MvExportService();
            var tileSet = project.TileSets.Single();
            var exportResult = exporter.ExportTileSet(project, tileSet, exportDirectory,
                new MvExportOptions { OverwriteExistingFiles = true });
            if (exportResult.ExportedFiles.Count < 19)
                throw new InvalidOperationException("The export did not write all MV files.");

            Console.WriteLine("Playroma Tile Phase 4 smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(testRoot, recursive: true); }
            catch { /* Preserve output when Windows still has a transient image handle. */ }
        }
    }

    private static void TestShapeResolver()
    {
        var resolver = new AutotileShapeResolver();
        var floorShapes = new HashSet<int>();
        var wallShapes = new HashSet<int>();
        var waterfallShapes = new HashSet<int>();
        for (var mask = 0; mask <= byte.MaxValue; mask++)
        {
            floorShapes.Add(resolver.Resolve(AutotileFamily.Floor, (NeighborMask)mask));
            wallShapes.Add(resolver.Resolve(AutotileFamily.Wall, (NeighborMask)mask));
            waterfallShapes.Add(resolver.Resolve(AutotileFamily.Waterfall, (NeighborMask)mask));
        }
        if (floorShapes.Count != 47 || floorShapes.Contains(47) || !floorShapes.Contains(46))
            throw new InvalidOperationException("Automatic floor masks must cover MV shapes 0–46; shape 47 is fixed placement.");
        if (wallShapes.Count != 16 || waterfallShapes.Count != 4)
            throw new InvalidOperationException("Wall or waterfall shape coverage is incomplete.");
    }

    private static void TestAutotileAuthoring(TileProject project)
    {
        var definition = new AutotileDefinition { Name = "Smoke Grass", Kind = AutotileKind.A2Floor };
        using var source = AutotileTemplateFactory.CreateDefault(definition.Kind);
        var reference = AutotileSerializer.SaveNew(project, definition, source);
        if (!File.Exists(ProjectPath.Resolve(project, reference.RelativeDefinitionPath)))
            throw new InvalidOperationException("The autotile definition was not written.");

        var autotileValidator = new AutotileValidator();
        if (autotileValidator.Validate(definition, source).Any(x => x.Severity == AutotileIssueSeverity.Error))
            throw new InvalidOperationException("The generated A2 template failed validation.");

        var firstQuarter = AutotileQuarterCatalog.Locate(definition.Kind, Point.Empty);
        if (firstQuarter.Bounds != new Rectangle(0, 0, 24, 24) || firstQuarter.Frame != 0)
            throw new InvalidOperationException("Quarter-component addressing is incorrect.");

        using var material = new Bitmap(48, 48);
        using (var graphics = Graphics.FromImage(material))
        {
            graphics.Clear(Color.ForestGreen);
            graphics.FillRectangle(Brushes.LightGreen, 0, 0, 24, 24);
        }
        using var composed = new AutotileMaterialComposer().Compose(definition.Kind, material);
        if (composed.Size != AutotileKindInfo.GetSourceSize(definition.Kind))
            throw new InvalidOperationException("Material composition produced the wrong source dimensions.");
        if (autotileValidator.Validate(definition, composed).Any(x => x.Severity == AutotileIssueSeverity.Error))
            throw new InvalidOperationException("The material-composed A2 source failed validation.");

        var renderer = new MvAutotileRenderer();
        for (var shape = 0; shape < 48; shape++)
        {
            using var tile = renderer.RenderShape(source, definition.Kind, shape);
            if (tile.Size != new Size(48, 48)) throw new InvalidOperationException("A rendered shape has the wrong dimensions.");
        }
        using var torture = new AutotileTortureTestRenderer().Render(source, definition.Kind);
        if (torture.Width != 20 * 48 || torture.Height != 15 * 48)
            throw new InvalidOperationException("The torture-test renderer returned the wrong dimensions.");
    }

    private static void TestAllAutotileKinds()
    {
        var validator = new AutotileValidator();
        var renderer = new MvAutotileRenderer();
        using var material = new Bitmap(48, 48);
        using (var graphics = Graphics.FromImage(material))
        {
            graphics.Clear(Color.SlateGray);
            graphics.FillEllipse(Brushes.LightGray, 4, 4, 40, 40);
        }

        foreach (var kind in Enum.GetValues<AutotileKind>())
        {
            using var source = AutotileTemplateFactory.CreateDefault(kind);
            if (source.Size != AutotileKindInfo.GetSourceSize(kind))
                throw new InvalidOperationException($"{kind} template has incorrect dimensions.");
            var definition = new AutotileDefinition { Name = kind.ToString(), Kind = kind };
            if (validator.Validate(definition, source).Any(x => x.Severity == AutotileIssueSeverity.Error))
                throw new InvalidOperationException($"{kind} template failed structural validation.");

            var shapeCount = AutotileKindInfo.GetFamily(kind) switch
            {
                AutotileFamily.Floor => 48,
                AutotileFamily.Wall => 16,
                _ => 4
            };
            for (var frame = 0; frame < AutotileKindInfo.GetFrameCount(kind); frame++)
            {
                for (var shape = 0; shape < shapeCount; shape++)
                {
                    using var tile = renderer.RenderShape(source, kind, shape, frame);
                    if (tile.Size != new Size(48, 48))
                        throw new InvalidOperationException($"{kind} frame {frame} shape {shape} is not 48 × 48.");
                }
                using var atlas = renderer.RenderShapeAtlas(source, kind, frame);
                if (atlas.Width < 48 || atlas.Height < 48)
                    throw new InvalidOperationException($"{kind} shape atlas is empty.");
            }

            using var composed = new AutotileMaterialComposer().Compose(kind, material);
            if (composed.Size != source.Size)
                throw new InvalidOperationException($"{kind} material composition has incorrect dimensions.");
            var last = AutotileQuarterCatalog.Locate(kind, new Point(source.Width - 1, source.Height - 1));
            if (last.Bounds.Right > source.Width || last.Bounds.Bottom > source.Height)
                throw new InvalidOperationException($"{kind} quarter catalogue escaped the source block.");
        }
    }

    private static void TestInverseCompiler(TileProject project)
    {
        var tileSet = project.TileSets.Single();
        var sheet = tileSet.Sheets.Single(x => x.Slot == MvSheetSlot.A2);
        var path = ProjectPath.Resolve(project, sheet.RelativeImagePath);
        using var bitmap = AutotileBitmap.LoadEditable(path);
        var importer = new MvAutotileInverseImporter();
        var extracted = importer.Extract(bitmap, MvSheetSlot.A2, 0, "Round Trip");
        using var source = extracted.Source;
        var originalHash = AutotileBitmap.ComputeSha256(source);
        var metadata = sheet.Regions.Single(x => x.Index == 0);
        var compiler = new MvAutotileCompiler();
        using var destination = (Bitmap)bitmap.Clone();
        compiler.CompileIntoSheet(destination, metadata, MvSheetSlot.A2, 0, extracted.Definition, source);
        using var recrop = AutotileBitmap.Crop(destination, new Rectangle(0, 0, 96, 144));
        if (!string.Equals(originalHash, AutotileBitmap.ComputeSha256(recrop), StringComparison.Ordinal))
            throw new InvalidOperationException("Inverse import and recompilation were not pixel-identical.");
    }


    private static void TestMaterialLaboratory(TileProject project)
    {
        var catalog = new MaterialPresetCatalog();
        var recipe = catalog.Create("Grass", MaterialStyle.SharpPixelArt, MaterialCondition.Overgrown, 12345);
        var renderer = new MaterialRenderService();
        using var first = renderer.Render(recipe, 96, 96, 12345, MaterialQuality.Final);
        using var second = renderer.Render(recipe, 96, 96, 12345, MaterialQuality.Final);
        if (first.BaseColor is null || second.BaseColor is null)
            throw new InvalidOperationException("A built-in material did not produce BaseColor.");
        var hashA = MaterialGraphEvaluator.ComputeBufferHash(first.BaseColor);
        var hashB = MaterialGraphEvaluator.ComputeBufferHash(second.BaseColor);
        if (!string.Equals(hashA, hashB, StringComparison.Ordinal))
            throw new InvalidOperationException("Material evaluation is not deterministic.");
        if (new MaterialValidator().Validate(first).Any(x => x.Severity == MaterialIssueSeverity.Error))
            throw new InvalidOperationException("The built-in grass material failed validation.");

        var reference = MaterialSerializer.SaveNew(project, recipe, first.BaseColor);
        var loaded = MaterialSerializer.Load(project, reference);
        using var loadedResult = renderer.Render(loaded, 96, 96, 12345, MaterialQuality.Final);
        if (loadedResult.BaseColor is null || MaterialGraphEvaluator.ComputeBufferHash(loadedResult.BaseColor) != hashA)
            throw new InvalidOperationException("A saved material recipe did not reproduce its pixels.");

        var reaction = catalog.Create("Reaction Organic", seed: 77);
        using var reactionResult = renderer.Render(reaction, 48, 48, 77, MaterialQuality.Draft);
        if (reactionResult.BaseColor is null)
            throw new InvalidOperationException("Reaction diffusion material did not render.");

        var exemplar = new Playroma.Tile.Materials.Buffers.ColorBuffer(32, 32);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
            exemplar[x, y] = new Rgba32((byte)(x * 8), (byte)(y * 8), (byte)((x + y) * 4));
        var quilt = TextureSynthesisAlgorithms.Quilt(exemplar, 64, 64, 12, 3, 91, CancellationToken.None);
        var seamless = TextureSynthesisAlgorithms.MakeSeamless(quilt, 6, false);
        var pixel = PixelArtProcessor.Process(seamless, 48, 48, 16, DitherMode.Bayer4, true, true);
        if (pixel.Width != 48 || pixel.Height != 48)
            throw new InvalidOperationException("The exemplar/pixel-art pipeline returned incorrect dimensions.");
        var wang = new WangTileBaker().Bake(pixel, 24, 2, 1, 42);
        var wangPreview = new WangTileBaker().RenderPreview(wang, 6, 5, 42);
        if (wang.Tiles.Count != 16 || wangPreview.Width != 144 || wangPreview.Height != 120)
            throw new InvalidOperationException("The Wang tile baker returned an invalid set or preview.");

        using var animated = renderer.Render(catalog.Create("Water", seed: 88), 96, 96, 88, MaterialQuality.Final, frameCount: 3);
        var adapter = new MaterialAutotileAdapter();
        var created = adapter.CreateAutotile(catalog.Create("Water", seed: 88), animated,
            new MaterialAutotileSettings { Mode = MaterialAdapterMode.AnimatedWater });
        using (created.Source)
        {
            if (created.Source.Size != AutotileKindInfo.GetSourceSize(AutotileKind.A1Water))
                throw new InvalidOperationException("Material-to-autotile conversion returned incorrect dimensions.");
            AutotileSerializer.SaveNew(project, created.Definition, created.Source);
        }
    }


    private static void TestTilesetConstruction(TileProject project, string testRoot)
    {
        var tileSet = project.TileSets.Single();
        new TilesetConstructionService().EnsureCompleteSheets(project, tileSet);
        var assembly = TilesetSerializer.LoadOrCreate(project, tileSet);
        assembly.TemplateKind = TilesetTemplateKind.Dungeon;
        var firstA2 = assembly.Assets.First(x => x.Sheet == MvSheetSlot.A2 && x.LogicalSlotIndex == 0);
        firstA2.Name = "Dungeon Stone Floor";
        firstA2.Category = "floor, stone";
        var firstA4 = assembly.Assets.First(x => x.Sheet == MvSheetSlot.A4 && x.LogicalSlotIndex == 0);
        firstA4.Name = "Dungeon Wall";
        firstA4.Category = "wall, dungeon";
        var metadata = new TilesetConstructionService().GetOrCreateMetadata(assembly, MvSheetSlot.A4, 0);
        metadata.Metadata.Passage = PassageMode.Impassable;
        metadata.Metadata.SemanticType = "wall";
        if ((MvTileFlagCodec.Encode(metadata.Metadata) & 0x0f) != 0x0f)
            throw new InvalidOperationException("MV flag encoding did not mark an impassable tile.");
        if (MvTileFlagCodec.Decode(MvTileFlagCodec.Encode(metadata.Metadata)).Passage != PassageMode.Impassable)
            throw new InvalidOperationException("MV flag encoding did not round-trip.");

        var objectSource = Path.Combine(testRoot, "SmokeObject.png");
        using (var image = new Bitmap(96, 96))
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(Brushes.SaddleBrown, 18, 12, 60, 78);
            image.Save(objectSource);
        }
        var obj = new OrdinaryTileService().ImportObject(project, objectSource, "Smoke Statue", TileObjectCategory.Structure);
        assembly.Objects.Add(obj);
        using var b = new Bitmap(768, 768);
        using var c = new Bitmap(768, 768);
        using var d = new Bitmap(768, 768);
        using var e = new Bitmap(768, 768);
        var packingSheets = new Dictionary<MvSheetSlot, Bitmap>
        {
            [MvSheetSlot.B] = b, [MvSheetSlot.C] = c, [MvSheetSlot.D] = d, [MvSheetSlot.E] = e
        };
        var plan = new TilesetPacker().CreatePlan(assembly, packingSheets, PackingMode.Compact);
        if (plan.Placements.Count != 1 || plan.Placements[0].Sheet != MvSheetSlot.B)
            throw new InvalidOperationException("The ordinary-object packer did not place the synthetic object.");
        new TilesetPacker().ApplyPlan(assembly, plan, packingSheets,
            x => BitmapFile.LoadEditable(ProjectPath.Resolve(project, x.RelativeImagePath)));
        if (assembly.Assets.All(x => x.SourceAssetId != obj.Id))
            throw new InvalidOperationException("Packed object bindings were not recorded.");

        var template = new ConstructionTemplateCatalog().Get(TilesetTemplateKind.Dungeon);
        var completeness = new CompletenessAnalyzer().Analyze(assembly, template);
        if (completeness.RequiredTotal == 0 || completeness.Score <= 0)
            throw new InvalidOperationException("Completeness analysis returned no usable result.");

        var map = TestMapDefinition.CreateDefault(4, 3);
        map.Set(TestMapLayer.Lower1, 0, 0, TileAddressCodec.Encode(MvSheetSlot.A5, 0));
        map.Set(TestMapLayer.Upper1, 1, 1, TileAddressCodec.Encode(MvSheetSlot.B, 0));
        var realSheets = tileSet.Sheets.ToDictionary(x => x.Slot,
            x => BitmapFile.LoadEditable(ProjectPath.Resolve(project, x.RelativeImagePath)));
        try
        {
            using var rendered = new FullTestMapRenderer().Render(map, realSheets, assembly, true, true, true);
            if (rendered.Size != new Size(192, 144))
                throw new InvalidOperationException("The Phase 4 full test-map renderer returned the wrong dimensions.");
            var issues = new TilesetValidator().Validate(project, tileSet, assembly, realSheets);
            if (issues.Any(x => x.Severity == TilesetIssueSeverity.Error))
                throw new InvalidOperationException("Phase 4 tileset validation produced errors: " + string.Join("; ", issues.Select(x => x.Message)));

            TilesetSerializer.Save(project, TilesetSerializer.GetReference(project, assembly), assembly);
            var publication = Path.Combine(testRoot, "Phase4Package");
            var export = new TilesetPublicationExporter().Export(project, tileSet, assembly, publication, realSheets);
            if (export.ExportedFiles.Count < 15 || !File.Exists(Path.Combine(publication, "Documentation", "TileCatalogue.html")))
                throw new InvalidOperationException("The Phase 4 publication exporter did not produce the complete package.");
        }
        finally
        {
            foreach (var image in realSheets.Values) image.Dispose();
        }

        var mvProject = Path.Combine(testRoot, "MvProject");
        Directory.CreateDirectory(Path.Combine(mvProject, "data"));
        Directory.CreateDirectory(Path.Combine(mvProject, "img", "tilesets"));
        File.WriteAllText(Path.Combine(mvProject, "data", "Tilesets.json"), "[null]");
        var patch = new MvProjectIntegrationService().Patch(project, tileSet, assembly, mvProject, null, true, true);
        if (patch.RecordId != 1 || !File.Exists(patch.UpdatedTilesetsJson) || !Directory.Exists(patch.BackupDirectory))
            throw new InvalidOperationException("RPG Maker MV project patching failed.");

        // Exercise the jRPG database bridge independently from the source project: load the exact
        // database record and its A1-E images into a fresh authoring project, then write it back.
        var bridgeProjectPath = Path.Combine(testRoot, "DatabaseBridge", "Bridge.playromatile.json");
        var bridgeProject = ProjectSerializer.CreateNew(bridgeProjectPath, "Database Bridge");
        var bridge = new MvDatabaseTilesetService();
        var imported = bridge.ImportRecords(bridgeProject, mvProject, new[] { patch.RecordId });
        if (imported.Warnings.Count != 0 || imported.ImportedTileSets.Count != 1)
            throw new InvalidOperationException("The database bridge did not import the patched tileset cleanly.");
        var linked = imported.ImportedTileSets.Single();
        if (linked.MvDatabaseRecordId != patch.RecordId || linked.Sheets.Count != tileSet.Sheets.Count)
            throw new InvalidOperationException("Database record linkage or A1-E sheet mapping was not preserved.");
        linked.Name = "Smoke Database Round Trip";
        var bridgedExport = bridge.ExportTileSet(bridgeProject, linked, mvProject, patch.RecordId, false, true);
        if (bridgedExport.RecordId != patch.RecordId || bridgedExport.CopiedImages.Count != linked.Sheets.Count)
            throw new InvalidOperationException("The database bridge did not export the linked tileset.");
        var updatedDatabase = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(mvProject, "data", "Tilesets.json"))) as System.Text.Json.Nodes.JsonArray;
        if (updatedDatabase?[patch.RecordId]?["name"]?.GetValue<string>() != linked.Name)
            throw new InvalidOperationException("The database bridge did not update the selected Tilesets.json record.");
    }

    private static string FindSampleDirectory()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Samples", "SyntheticMV");
            if (Directory.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return Path.Combine(Environment.CurrentDirectory, "Samples", "SyntheticMV");
    }
}
