using System.Security.Cryptography;
using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Resilience;

public sealed class SpriteRecoverySnapshot
{
    public string Schema { get; set; } = "playroma-sprite-recovery/1";
    public Guid SnapshotId { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PackageFileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string? OriginalPackagePath { get; set; }
    public string? RuntimeExportPath { get; set; }
    public string Reason { get; set; } = "autosave";
    public long PackageBytes { get; set; }
}

public sealed class SpriteRecoveryService
{
    private readonly SpritePackageSerializer serializer = new();
    public string RootDirectory { get; }

    public SpriteRecoveryService(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
    }

    public async Task<SpriteRecoverySnapshot> CaptureAsync(
        SpritePackageData package,
        string? originalPackagePath,
        string? runtimeExportPath,
        string reason,
        int retention,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        string documentDirectory = Path.Combine(RootDirectory, package.Document.Id.ToString("N"));
        Directory.CreateDirectory(documentDirectory);
        Guid snapshotId = Guid.NewGuid();
        string baseName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{snapshotId:N}";
        string packagePath = Path.Combine(documentDirectory, baseName + ".recovery.playromasprite");
        await serializer.SaveAsync(package.DeepClone(), packagePath, cancellationToken);
        string hash = await ComputeSha256Async(packagePath, cancellationToken);
        FileInfo info = new(packagePath);
        SpriteRecoverySnapshot snapshot = new()
        {
            SnapshotId = snapshotId,
            DocumentId = package.Document.Id,
            DocumentName = package.Document.Name,
            CapturedAt = DateTimeOffset.UtcNow,
            PackageFileName = info.Name,
            Sha256 = hash,
            OriginalPackagePath = originalPackagePath,
            RuntimeExportPath = runtimeExportPath,
            Reason = string.IsNullOrWhiteSpace(reason) ? "autosave" : reason.Trim(),
            PackageBytes = info.Length
        };
        ResilienceJson.WriteAtomic(Path.Combine(documentDirectory, baseName + ".recovery.json"), snapshot);
        Prune(package.Document.Id, Math.Clamp(retention, 2, 100));
        return snapshot;
    }

    public IReadOnlyList<SpriteRecoverySnapshot> ListSnapshots(Guid? documentId = null)
    {
        IEnumerable<string> manifests = documentId is Guid id
            ? Directory.Exists(Path.Combine(RootDirectory, id.ToString("N"))) ? Directory.EnumerateFiles(Path.Combine(RootDirectory, id.ToString("N")), "*.recovery.json") : []
            : Directory.EnumerateFiles(RootDirectory, "*.recovery.json", SearchOption.AllDirectories);
        List<SpriteRecoverySnapshot> result = [];
        foreach (string manifestPath in manifests)
        {
            try
            {
                SpriteRecoverySnapshot? snapshot = ResilienceJson.Read<SpriteRecoverySnapshot>(manifestPath);
                if (snapshot is null || snapshot.Schema != "playroma-sprite-recovery/1") continue;
                string packagePath = ResolvePackagePath(snapshot);
                if (!File.Exists(packagePath)) continue;
                result.Add(snapshot);
            }
            catch { }
        }
        return result.OrderByDescending(item => item.CapturedAt).ToArray();
    }

    public async Task<SpritePackageData> LoadAsync(SpriteRecoverySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string path = ResolvePackagePath(snapshot);
        string actual = await ComputeSha256Async(path, cancellationToken);
        if (!actual.Equals(snapshot.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery snapshot checksum does not match its manifest.");
        return await serializer.LoadPackageAsync(path, cancellationToken);
    }

    public void Delete(SpriteRecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string packagePath = ResolvePackagePath(snapshot);
        string manifestPath = Path.ChangeExtension(packagePath, ".json");
        if (File.Exists(packagePath)) File.Delete(packagePath);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
    }

    public int Prune(Guid documentId, int retain)
    {
        SpriteRecoverySnapshot[] snapshots = ListSnapshots(documentId).ToArray();
        int removed = 0;
        foreach (SpriteRecoverySnapshot snapshot in snapshots.Skip(Math.Max(0, retain))) { Delete(snapshot); removed++; }
        return removed;
    }

    public int DeleteAll(Guid? documentId = null)
    {
        SpriteRecoverySnapshot[] snapshots = ListSnapshots(documentId).ToArray();
        foreach (SpriteRecoverySnapshot snapshot in snapshots) Delete(snapshot);
        return snapshots.Length;
    }

    private string ResolvePackagePath(SpriteRecoverySnapshot snapshot)
    {
        string directory = Path.Combine(RootDirectory, snapshot.DocumentId.ToString("N"));
        string candidate = Path.GetFullPath(Path.Combine(directory, snapshot.PackageFileName));
        if (!candidate.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Recovery package path escapes its document directory.");
        return candidate;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class SpriteEditorSessionLease : IDisposable
{
    private sealed class LeaseRecord
    {
        public LeaseRecord() { }
        public string Schema { get; set; } = "playroma-sprite-session/1";
        public int ProcessId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public bool CleanExit { get; set; }
    }

    private readonly string path;
    private bool disposed;
    public bool PreviousSessionWasUnclean { get; }

    public SpriteEditorSessionLease(string path)
    {
        this.path = Path.GetFullPath(path);
        LeaseRecord? previous = null;
        try { previous = ResilienceJson.Read<LeaseRecord>(this.path); } catch { }
        PreviousSessionWasUnclean = previous is not null && !previous.CleanExit;
        ResilienceJson.WriteAtomic(this.path, new LeaseRecord { ProcessId = Environment.ProcessId, StartedAt = DateTimeOffset.UtcNow, CleanExit = false });
    }

    public void MarkCleanExit()
    {
        if (disposed) return;
        ResilienceJson.WriteAtomic(path, new LeaseRecord { ProcessId = Environment.ProcessId, StartedAt = DateTimeOffset.UtcNow, CleanExit = true });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
