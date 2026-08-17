using System.IO.Compression;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;

namespace Playroma.Tile.RpgMakerMV.Import;

public sealed class MvImportService
{
    public MvImportResult ImportFolder(TileProject project, string folderPath, bool recursive = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pngFiles = Directory.EnumerateFiles(folderPath, "*.png", option).ToArray();
        return ImportFiles(project, pngFiles);
    }

    public MvImportResult ImportArchive(TileProject project, string archivePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PlayromaTile", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            ExtractZipSafely(archivePath, temporaryDirectory);
            return ImportFolder(project, temporaryDirectory, true);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    public MvImportResult ImportFiles(TileProject project, IEnumerable<string> pngPaths)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pngPaths);
        if (string.IsNullOrWhiteSpace(project.ProjectDirectory))
        {
            throw new InvalidOperationException("The project must be saved before files can be imported.");
        }

        var result = new MvImportResult();
        foreach (var sourcePath in pngPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(sourcePath))
            {
                result.SkippedFiles.Add($"Missing file: {sourcePath}");
                continue;
            }

            if (!MvFileNameParser.TryParse(sourcePath, out var parsed) || parsed is null)
            {
                result.SkippedFiles.Add($"Unrecognized MV sheet filename: {Path.GetFileName(sourcePath)}");
                continue;
            }

            try
            {
                var sheet = ImportOne(project, sourcePath, parsed, result.Warnings);
                result.ImportedSheets.Add(sheet);
            }
            catch (Exception ex)
            {
                result.SkippedFiles.Add($"{Path.GetFileName(sourcePath)}: {ex.Message}");
            }
        }

        return result;
    }

    private static TileSheetDefinition ImportOne(
        TileProject project,
        string sourcePath,
        ParsedMvFileName parsed,
        ICollection<string> warnings)
    {
        var tileSet = FindAvailableTileSet(project, parsed.SetName, parsed.Slot);
        var sheetsDirectory = Path.Combine(project.ProjectDirectory, "Sheets");
        var catalogsDirectory = Path.Combine(project.ProjectDirectory, "Catalogues");
        Directory.CreateDirectory(sheetsDirectory);
        Directory.CreateDirectory(catalogsDirectory);

        var destinationFileName = FindUniqueFileName(sheetsDirectory, Path.GetFileName(sourcePath));
        var destinationPath = Path.Combine(sheetsDirectory, destinationFileName);
        File.Copy(sourcePath, destinationPath, overwrite: false);

        var sourceCatalogPath = Path.ChangeExtension(sourcePath, ".txt");
        string? relativeCatalogPath = null;
        IReadOnlyList<CatalogEntry> catalogEntries = Array.Empty<CatalogEntry>();
        if (File.Exists(sourceCatalogPath))
        {
            var pairedCatalogName = Path.ChangeExtension(destinationFileName, ".txt");
            var catalogFileName = FindUniqueFileName(catalogsDirectory, pairedCatalogName);
            var destinationCatalogPath = Path.Combine(catalogsDirectory, catalogFileName);
            File.Copy(sourceCatalogPath, destinationCatalogPath, overwrite: false);
            relativeCatalogPath = Path.GetRelativePath(project.ProjectDirectory, destinationCatalogPath);
            catalogEntries = CatalogTextParser.Parse(sourceCatalogPath);
        }

        var size = BitmapFile.ReadSize(sourcePath);
        var logicalRegions = MvLayoutCatalog.GetLogicalRegions(parsed.Slot);
        var regionMetadata = new List<TileRegionMetadata>(logicalRegions.Count);
        for (var index = 0; index < logicalRegions.Count; index++)
        {
            var catalog = index < catalogEntries.Count ? catalogEntries[index] : null;
            regionMetadata.Add(new TileRegionMetadata
            {
                Index = index,
                Name = string.IsNullOrWhiteSpace(catalog?.PrimaryName)
                    ? logicalRegions[index].DefaultName
                    : catalog!.PrimaryName,
                AlternateName = catalog?.AlternateName ?? string.Empty
            });
        }

        if (catalogEntries.Count > 0 && catalogEntries.Count != logicalRegions.Count)
        {
            warnings.Add(
                $"{Path.GetFileName(sourcePath)} has {catalogEntries.Count} catalogue entries; " +
                $"{parsed.Slot} expects {logicalRegions.Count} logical regions.");
        }

        var sheet = new TileSheetDefinition
        {
            Slot = parsed.Slot,
            DisplayName = parsed.FileNameWithoutExtension,
            FileName = destinationFileName,
            RelativeImagePath = Path.GetRelativePath(project.ProjectDirectory, destinationPath),
            RelativeCatalogPath = relativeCatalogPath,
            SourceFileName = Path.GetFileName(sourcePath),
            SourceSha256 = BitmapFile.ComputeSha256(sourcePath),
            PixelWidth = size.Width,
            PixelHeight = size.Height,
            Regions = regionMetadata
        };

        tileSet.Sheets.Add(sheet);
        return sheet;
    }

    private static TileSetDefinition FindAvailableTileSet(TileProject project, string requestedName, MvSheetSlot slot)
    {
        var exact = project.TileSets.FirstOrDefault(set =>
            string.Equals(set.Name, requestedName, StringComparison.OrdinalIgnoreCase) &&
            set.Sheets.All(sheet => sheet.Slot != slot));
        if (exact is not null)
        {
            return exact;
        }

        if (project.TileSets.All(set => !string.Equals(set.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
        {
            var created = new TileSetDefinition { Name = requestedName };
            project.TileSets.Add(created);
            return created;
        }

        var suffix = 2;
        while (true)
        {
            var candidateName = $"{requestedName} {suffix}";
            var candidate = project.TileSets.FirstOrDefault(set =>
                string.Equals(set.Name, candidateName, StringComparison.OrdinalIgnoreCase) &&
                set.Sheets.All(sheet => sheet.Slot != slot));
            if (candidate is not null)
            {
                return candidate;
            }

            if (project.TileSets.All(set => !string.Equals(set.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                var created = new TileSetDefinition { Name = candidateName };
                project.TileSets.Add(created);
                return created;
            }

            suffix++;
        }
    }

    private static string FindUniqueFileName(string directory, string requestedFileName)
    {
        var sanitizedStem = ProjectPath.SanitizeFileName(Path.GetFileNameWithoutExtension(requestedFileName));
        var extension = Path.GetExtension(requestedFileName);
        var candidate = sanitizedStem + extension;
        var suffix = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{sanitizedStem}_{suffix}{extension}";
            suffix++;
        }

        return candidate;
    }

    private static void ExtractZipSafely(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The archive contains a path outside its extraction directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary extraction data is best-effort cleanup only.
        }
    }
}
