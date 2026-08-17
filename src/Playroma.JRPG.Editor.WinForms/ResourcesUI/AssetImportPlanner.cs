using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Editor.WinForms.ResourcesUI;

public enum AssetImportCandidateAction
{
    Import,
    ReplaceExisting,
    AutoRename,
    Skip,
    ResolveConflict
}

public sealed class AssetImportCandidate
{
    public required string SourcePath { get; init; }
    public required string Extension { get; init; }
    public required long Size { get; init; }
    public required AssetMediaKind MediaKind { get; init; }
    public AssetCategory Category { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public AssetImportCandidateAction Action { get; set; }
    public string? ExistingRelativePath { get; set; }
    public string DestinationDisplay { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

public static class AssetImportPlanner
{
    public static IReadOnlyList<string> EnumerateSupportedFiles(string folder, bool recursive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder)) return [];
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        try
        {
            return Directory.EnumerateFiles(folder, "*", options)
                .Where(path => AssetCategoryRegistry.ResolveMediaKind(Path.GetExtension(path)) != AssetMediaKind.Unknown)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    public static AssetImportCandidate CreateCandidate(string sourcePath, AssetCategory suggestedCategory)
    {
        string fullPath = Path.GetFullPath(sourcePath);
        FileInfo info = new(fullPath);
        string extension = info.Extension;
        AssetMediaKind media = AssetCategoryRegistry.ResolveMediaKind(extension);
        AssetCategory category = suggestedCategory != AssetCategory.Unknown && IsMediaCompatible(suggestedCategory, media)
            ? suggestedCategory
            : GuessCategory(fullPath, media);
        return new AssetImportCandidate
        {
            SourcePath = fullPath,
            Extension = extension,
            Size = info.Exists ? info.Length : 0,
            MediaKind = media,
            Category = category,
            LogicalName = DeriveLogicalName(fullPath, category),
            Action = AssetImportCandidateAction.Import
        };
    }

    public static void ValidateAll(MvProjectSession session, IReadOnlyList<AssetImportCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidates);
        Dictionary<string, List<AssetImportCandidate>> destinations = new(StringComparer.OrdinalIgnoreCase);

        foreach (AssetImportCandidate candidate in candidates)
        {
            ValidateSingle(session, candidate);
            if (!candidate.IsValid || candidate.Action == AssetImportCandidateAction.Skip) continue;
            string key = candidate.Category + ":" + candidate.LogicalName + candidate.Extension.ToLowerInvariant();
            if (!destinations.TryGetValue(key, out List<AssetImportCandidate>? group))
            {
                group = [];
                destinations.Add(key, group);
            }
            group.Add(candidate);
        }

        foreach (List<AssetImportCandidate> group in destinations.Values.Where(group => group.Count > 1))
        {
            foreach (AssetImportCandidate candidate in group)
            {
                candidate.IsValid = false;
                candidate.Status = "Duplicate destination within this batch. Rename or skip one of the files.";
            }
        }
    }

    public static AssetImportPlan BuildPlan(IReadOnlyList<AssetImportCandidate> candidates)
    {
        AssetImportPlanItem[] items = candidates
            .Where(candidate => candidate.IsValid && candidate.Action != AssetImportCandidateAction.Skip)
            .Select(candidate => new AssetImportPlanItem(
                candidate.SourcePath,
                new AssetKey(candidate.Category, candidate.LogicalName),
                candidate.Action == AssetImportCandidateAction.ReplaceExisting
                    ? AssetImportConflictAction.Replace
                    : AssetImportConflictAction.Import,
                candidate.ExistingRelativePath))
            .ToArray();
        return new AssetImportPlan(items, $"Import {items.Length:N0} asset(s)");
    }

    public static string CreateAutomaticName(
        MvProjectSession session,
        AssetImportCandidate candidate,
        IEnumerable<AssetImportCandidate> peers)
    {
        string baseName = candidate.LogicalName;
        HashSet<string> peerNames = peers
            .Where(peer => !ReferenceEquals(peer, candidate) && peer.Category == candidate.Category)
            .Select(peer => peer.LogicalName + peer.Extension.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int suffix = 2; suffix < 100_000; suffix++)
        {
            string proposed = $"{baseName}_{suffix}";
            AssetKey key = new(candidate.Category, proposed);
            bool exists = session.Assets.Find(key)?.Variants.Any(variant =>
                variant.Extension.Equals(candidate.Extension, StringComparison.OrdinalIgnoreCase)
                && variant.Status != AssetStatus.PendingDeletion) == true;
            if (!exists && !peerNames.Contains(proposed + candidate.Extension.ToLowerInvariant())) return proposed;
        }
        throw new InvalidOperationException("Could not generate a unique asset name.");
    }

    private static void ValidateSingle(MvProjectSession session, AssetImportCandidate candidate)
    {
        candidate.IsValid = false;
        candidate.ExistingRelativePath = null;
        candidate.DestinationDisplay = string.Empty;
        candidate.Status = string.Empty;

        if (candidate.Action == AssetImportCandidateAction.Skip)
        {
            candidate.IsValid = true;
            candidate.Status = "Skipped";
            return;
        }
        if (!File.Exists(candidate.SourcePath))
        {
            candidate.Status = "Source file not found.";
            return;
        }
        FileInfo source = new(candidate.SourcePath);
        if (source.Length <= 0)
        {
            candidate.Status = "Source file is empty.";
            return;
        }
        if (AssetCategoryRegistry.IsEncryptedExtension(candidate.Extension))
        {
            candidate.Status = "Encrypted deployment assets cannot be imported as editable project sources.";
            return;
        }
        if (candidate.MediaKind == AssetMediaKind.Unknown)
        {
            candidate.Status = "Unsupported file extension.";
            return;
        }
        if (!IsMediaCompatible(candidate.Category, candidate.MediaKind))
        {
            candidate.Status = $"{candidate.MediaKind} files cannot be imported into {candidate.Category}.";
            return;
        }
        if (!TryValidateLogicalName(candidate.LogicalName, out string? error))
        {
            candidate.Status = error ?? "Invalid logical name.";
            return;
        }
        string logicalExtension = Path.GetExtension(candidate.LogicalName);
        if (AssetCategoryRegistry.ResolveMediaKind(logicalExtension) != AssetMediaKind.Unknown
            || AssetCategoryRegistry.IsEncryptedExtension(logicalExtension))
        {
            candidate.Status = "Logical name must omit the asset file extension.";
            return;
        }

        AssetKey key;
        try { key = new AssetKey(candidate.Category, candidate.LogicalName); }
        catch (ArgumentException exception)
        {
            candidate.Status = exception.Message;
            return;
        }
        candidate.DestinationDisplay = AssetCategoryRegistry.BuildRuntimeRelativePath(key, candidate.Extension);
        AssetRecord? conflict = session.Assets.Find(key);
        AssetVariant? sameExtension = conflict?.Variants.FirstOrDefault(variant =>
            variant.Extension.Equals(candidate.Extension, StringComparison.OrdinalIgnoreCase)
            && variant.Status != AssetStatus.PendingDeletion);

        if (sameExtension is not null)
        {
            candidate.ExistingRelativePath = sameExtension.RelativePath;
            switch (candidate.Action)
            {
                case AssetImportCandidateAction.ReplaceExisting:
                    candidate.IsValid = true;
                    candidate.Status = $"Will replace {sameExtension.RelativePath}.";
                    return;
                case AssetImportCandidateAction.AutoRename:
                    candidate.IsValid = false;
                    candidate.Status = "Apply automatic rename to resolve this conflict.";
                    return;
                case AssetImportCandidateAction.ResolveConflict:
                case AssetImportCandidateAction.Import:
                    candidate.Action = AssetImportCandidateAction.ResolveConflict;
                    candidate.Status = "Destination exists. Choose Replace Existing, Auto Rename, or Skip.";
                    return;
            }
        }

        if (candidate.Action == AssetImportCandidateAction.ReplaceExisting)
        {
            candidate.Status = "There is no matching existing variant to replace.";
            return;
        }
        if (candidate.Action == AssetImportCandidateAction.ResolveConflict)
        {
            candidate.Action = AssetImportCandidateAction.Import;
        }
        candidate.IsValid = true;
        candidate.Status = conflict is null ? "Ready to import." : "Ready to add as another extension variant.";
    }

    public static bool TryValidateLogicalName(string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value)) { error = "Logical name is required."; return false; }
        if (Path.IsPathRooted(value)) { error = "Logical name must be project-relative."; return false; }
        string normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0) { error = "Logical name is empty."; return false; }
        const string portableInvalid = "<>:\"|?*";
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") { error = "Logical name contains a traversal segment."; return false; }
            if (segment.Any(character => character < 32 || portableInvalid.Contains(character))) { error = $"'{segment}' contains a non-portable filename character."; return false; }
            if (segment.EndsWith(' ') || segment.EndsWith('.')) { error = $"'{segment}' may not end in a space or period."; return false; }
            if (reserved.Contains(segment.Split('.')[0], StringComparer.OrdinalIgnoreCase)) { error = $"'{segment}' is reserved by Windows."; return false; }
        }
        return true;
    }

    public static AssetCategory GuessCategory(string sourcePath, AssetMediaKind media)
    {
        string[] directories = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        for (int index = directories.Length - 1; index >= 0; index--)
        {
            string folder = directories[index];
            foreach ((AssetCategory category, string directory) in AssetCategoryRegistry.Directories)
            {
                if (string.Equals(Path.GetFileName(directory), folder, StringComparison.OrdinalIgnoreCase)
                    && IsMediaCompatible(category, media)) return category;
            }
        }
        return media switch
        {
            AssetMediaKind.Audio => AssetCategory.SoundEffects,
            AssetMediaKind.Font => AssetCategory.Fonts,
            AssetMediaKind.Movie => AssetCategory.Movies,
            _ => AssetCategory.Pictures
        };
    }

    public static string DeriveLogicalName(string sourcePath, AssetCategory category)
    {
        string normalized = Path.GetFullPath(sourcePath).Replace('\\', '/');
        if (AssetCategoryRegistry.Directories.TryGetValue(category, out string? categoryDirectory))
        {
            string marker = "/" + categoryDirectory.Trim('/').Replace('\\', '/') + "/";
            int index = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string relative = normalized[(index + marker.Length)..];
                string extension = Path.GetExtension(relative);
                string withoutExtension = extension.Length == 0 ? relative : relative[..^extension.Length];
                if (!string.IsNullOrWhiteSpace(withoutExtension)) return withoutExtension;
            }
        }
        return Path.GetFileNameWithoutExtension(normalized);
    }

    public static bool IsMediaCompatible(AssetCategory category, AssetMediaKind media) => category switch
    {
        AssetCategory.BackgroundMusic or AssetCategory.BackgroundSounds or AssetCategory.MusicEffects or AssetCategory.SoundEffects => media == AssetMediaKind.Audio,
        AssetCategory.Fonts => media == AssetMediaKind.Font,
        AssetCategory.Movies => media == AssetMediaKind.Movie,
        AssetCategory.Unknown => false,
        _ => media == AssetMediaKind.Image
    };
}
