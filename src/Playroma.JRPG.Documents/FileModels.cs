namespace Playroma.JRPG.Documents;

public sealed record FileMetadata(
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes = FileAttributes.Normal);

public sealed record ContentFingerprint(
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string? Sha256)
{
    public bool HasStrongHash => !string.IsNullOrWhiteSpace(Sha256);
}

public enum ProjectFileKind
{
    Unknown,
    ProjectMarker,
    Database,
    Map,
    PluginManifest,
    PluginScript,
    RuntimeScript,
    RuntimeLibrary,
    RuntimeConfiguration,
    Html,
    Image,
    Audio,
    Movie,
    Font,
    EncryptedAsset,
    Executable,
    SaveData
}

public sealed record ProjectFileEntry(
    string RelativePath,
    ProjectFileKind Kind,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Extension,
    ContentFingerprint Fingerprint);

public sealed record ProjectFileInventory(
    IReadOnlyList<ProjectFileEntry> Files,
    long TotalBytes)
{
    public int Count => Files.Count;

    public IEnumerable<ProjectFileEntry> OfKind(ProjectFileKind kind) =>
        Files.Where(file => file.Kind == kind);

    public ProjectFileEntry? Find(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Files.FirstOrDefault(file => string.Equals(
            Normalize(file.RelativePath),
            Normalize(relativePath),
            StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}
