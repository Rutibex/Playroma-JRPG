using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Export;
using Playroma.Tile.Tilesets.Models;
using Playroma.Tile.Tilesets.Rendering;
using Playroma.Tile.Tilesets.Validation;

namespace Playroma.Tile.Tilesets.Export;

public sealed class TilesetPublicationExporter
{
    public TilesetExportResult Export(TileProject project, TileSetDefinition tileSet,
        TilesetAssemblyDefinition definition, string destinationDirectory,
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheetImages)
    {
        var validator = new TilesetValidator();
        var issues = validator.Validate(project, tileSet, definition, sheetImages);
        if (definition.Export.BlockOnErrors && issues.Any(x => x.Severity == TilesetIssueSeverity.Error))
            throw new InvalidOperationException("Export stopped because tileset validation found errors:\n" +
                                                string.Join("\n", issues.Where(x => x.Severity == TilesetIssueSeverity.Error).Select(x => "• " + x.Message)));

        var result = new TilesetExportResult { DestinationDirectory = destinationDirectory };
        var tilesetDirectory = Path.Combine(destinationDirectory, "Tilesets");
        var previewDirectory = Path.Combine(destinationDirectory, "Preview");
        var docsDirectory = Path.Combine(destinationDirectory, "Documentation");
        var metadataDirectory = Path.Combine(destinationDirectory, "Metadata");
        Directory.CreateDirectory(tilesetDirectory);
        Directory.CreateDirectory(previewDirectory);
        Directory.CreateDirectory(docsDirectory);
        Directory.CreateDirectory(metadataDirectory);

        var mv = new MvExportService().ExportTileSet(project, tileSet, tilesetDirectory,
            new MvExportOptions { OverwriteExistingFiles = true, IncludeCatalogTextFiles = definition.Export.IncludeCatalogues, IncludeExportReport = true });
        result.ExportedFiles.AddRange(mv.ExportedFiles);
        result.Warnings.AddRange(mv.Warnings);

        var manifestPath = Path.Combine(metadataDirectory, $"{Safe(tileSet.Name)}.manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            format = "Playroma Tile Tileset Package",
            version = 4,
            project = project.Name,
            tileset = tileSet.Name,
            definition.Theme,
            definition.TemplateKind,
            definition.Revision,
            exportedUtc = DateTimeOffset.UtcNow,
            sheets = tileSet.Sheets.OrderBy(x => x.Slot).Select(x => new { x.Slot, x.FileName, x.PixelWidth, x.PixelHeight }),
            assets = definition.Assets,
            objects = definition.Objects,
            metadata = definition.Metadata,
            validation = issues
        }, new JsonSerializerOptions { WriteIndented = true }));
        result.ExportedFiles.Add(manifestPath);

        var metadataPath = Path.Combine(metadataDirectory, $"{Safe(tileSet.Name)}.tileset.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = true }));
        result.ExportedFiles.Add(metadataPath);

        if (definition.Export.IncludeContactSheet)
        {
            var contactPath = Path.Combine(previewDirectory, $"{Safe(tileSet.Name)}_ContactSheet.png");
            using var contact = CreateContactSheet(tileSet, sheetImages);
            contact.Save(contactPath, ImageFormat.Png);
            result.ExportedFiles.Add(contactPath);
        }
        if (definition.Export.IncludeShowcaseScene)
        {
            var map = new ShowcaseSceneBuilder().Build(definition);
            using var beauty = new FullTestMapRenderer().Render(map, sheetImages, definition, drawGrid: false, drawPlayer: false);
            var package = new ShowcaseSceneBuilder().Export(previewDirectory, map, beauty, definition);
            result.ExportedFiles.AddRange(new[] { package.BeautyPath, package.GridPath, package.LabelsPath, package.SemanticPath, package.ObjectIdsPath, package.ManifestPath });
        }

        var readme = Path.Combine(docsDirectory, "README.txt");
        File.WriteAllText(readme, CreateReadme(project, tileSet, definition, issues), new UTF8Encoding(true));
        result.ExportedFiles.Add(readme);
        var setup = Path.Combine(docsDirectory, "SetupGuide.html");
        File.WriteAllText(setup, CreateSetupGuide(tileSet, definition), Encoding.UTF8);
        result.ExportedFiles.Add(setup);
        if (definition.Export.IncludeHtmlCatalogue)
        {
            var catalogue = Path.Combine(docsDirectory, "TileCatalogue.html");
            File.WriteAllText(catalogue, CreateCatalogue(definition), Encoding.UTF8);
            result.ExportedFiles.Add(catalogue);
        }
        var license = Path.Combine(docsDirectory, "License.txt");
        File.WriteAllText(license, string.IsNullOrWhiteSpace(definition.Export.LicenseText)
            ? "No licence text was supplied. The publisher must add the appropriate licence before distribution."
            : definition.Export.LicenseText, Encoding.UTF8);
        result.ExportedFiles.Add(license);
        var credits = Path.Combine(docsDirectory, "Credits.txt");
        File.WriteAllText(credits, $"Tileset: {tileSet.Name}\nAuthor: {definition.Export.Author}\n{definition.Export.CreditsText}", Encoding.UTF8);
        result.ExportedFiles.Add(credits);
        return result;
    }

    private static Bitmap CreateContactSheet(TileSetDefinition tileSet, IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets)
    {
        const int thumbWidth = 384;
        var ordered = tileSet.Sheets.OrderBy(x => x.Slot).ToArray();
        var rows = (ordered.Length + 2) / 3;
        var output = new Bitmap(3 * (thumbWidth + 24), rows * 330, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(32, 32, 34));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        using var titleFont = new Font(FontFamily.GenericSansSerif, 13f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        for (var i = 0; i < ordered.Length; i++)
        {
            if (!sheets.TryGetValue(ordered[i].Slot, out var image)) continue;
            var column = i % 3;
            var row = i / 3;
            var x = column * (thumbWidth + 24) + 12;
            var y = row * 330 + 12;
            graphics.DrawString(ordered[i].Slot.ToString(), titleFont, brush, x, y);
            var scale = Math.Min(thumbWidth / (double)image.Width, 275d / image.Height);
            graphics.DrawImage(image, new Rectangle(x, y + 28, (int)(image.Width * scale), (int)(image.Height * scale)));
        }
        return output;
    }

    private static string CreateReadme(TileProject project, TileSetDefinition tileSet, TilesetAssemblyDefinition definition,
        IReadOnlyList<TilesetIssue> issues)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PLAYROMA TILE — TILESET PACKAGE");
        builder.AppendLine(new string('=', 36));
        builder.AppendLine($"Project: {project.Name}");
        builder.AppendLine($"Tileset: {tileSet.Name}");
        builder.AppendLine($"Theme: {definition.Theme}");
        builder.AppendLine($"Construction profile: {definition.TemplateKind}");
        builder.AppendLine($"Author: {definition.Export.Author}");
        builder.AppendLine($"Exported UTC: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();
        builder.AppendLine("Copy the PNG files from Tilesets into your RPG Maker MV project's img/tilesets folder, then configure the tileset record using the included setup guide.");
        builder.AppendLine();
        builder.AppendLine("Validation summary:");
        builder.AppendLine($"  Errors: {issues.Count(x => x.Severity == TilesetIssueSeverity.Error)}");
        builder.AppendLine($"  Warnings: {issues.Count(x => x.Severity == TilesetIssueSeverity.Warning)}");
        return builder.ToString();
    }

    private static string CreateSetupGuide(TileSetDefinition tileSet, TilesetAssemblyDefinition definition)
    {
        var rows = string.Join("", tileSet.Sheets.OrderBy(x => x.Slot).Select(x => $"<tr><td>{x.Slot}</td><td>{WebUtility.HtmlEncode(x.FileName)}</td><td>{x.PixelWidth}×{x.PixelHeight}</td></tr>"));
        return $$"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>{{WebUtility.HtmlEncode(tileSet.Name)}} setup</title>
                <style>
                    body { font: 16px sans-serif; max-width: 900px; margin: 40px auto; line-height: 1.5; }
                    table { border-collapse: collapse; width: 100%; }
                    td, th { border: 1px solid #aaa; padding: 8px; }
                    code { background: #eee; padding: 2px 5px; }
                </style>
            </head>
            <body>
                <h1>{{WebUtility.HtmlEncode(tileSet.Name)}}</h1>
                <p>Copy the images into <code>img/tilesets</code>, open the RPG Maker MV Database, create or select a Tileset record, and assign each sheet to its matching slot.</p>
                <table>
                    <thead><tr><th>Slot</th><th>File</th><th>Dimensions</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                </table>
                <p>Construction profile: {{definition.TemplateKind}}. Metadata is included in the package JSON and can also be written automatically with Playroma Tile's MV project integration command.</p>
            </body>
            </html>
            """;
    }

    private static string CreateCatalogue(TilesetAssemblyDefinition definition)
    {
        var rows = string.Join("", definition.Assets.OrderBy(x => x.Sheet).ThenBy(x => x.LogicalSlotIndex).Select(x =>
            $"<tr><td>{x.Sheet}</td><td>{x.LogicalSlotIndex + 1}</td><td>{WebUtility.HtmlEncode(x.Name)}</td><td>{WebUtility.HtmlEncode(x.Category)}</td><td>{x.SourceKind}</td></tr>"));
        return $$"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>Tile Catalogue</title>
                <style>
                    body { font: 15px sans-serif; max-width: 1100px; margin: 32px auto; }
                    table { border-collapse: collapse; width: 100%; }
                    td, th { border: 1px solid #bbb; padding: 6px; text-align: left; }
                    th { background: #eee; }
                </style>
            </head>
            <body>
                <h1>{{WebUtility.HtmlEncode(definition.Name)}} — Tile Catalogue</h1>
                <table>
                    <thead><tr><th>Sheet</th><th>Slot</th><th>Name</th><th>Category</th><th>Source</th></tr></thead>
                    <tbody>{{rows}}</tbody>
                </table>
            </body>
            </html>
            """;
    }

    private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
