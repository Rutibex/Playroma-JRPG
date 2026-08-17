using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.Editor.Shell;

public interface ISettingsService
{
    string SettingsPath { get; }
    EditorSettings Current { get; }
    event EventHandler? Changed;
    ValueTask LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Update(Func<EditorSettings, EditorSettings> update);
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly EditorSettingsMigrator _migrator = new();

    public JsonSettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(AppDataPaths.Root, "settings.json");
    }

    public string SettingsPath { get; }
    public EditorSettings Current { get; private set; } = new();
    public IReadOnlyList<string> MigrationDiagnostics { get; private set; } = [];
    public event EventHandler? Changed;

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            byte[] originalBytes = await File.ReadAllBytesAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            using JsonDocument source = JsonDocument.Parse(originalBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            EditorSettingsMigrationResult result = _migrator.Migrate(source);
            Current = result.Settings;
            MigrationDiagnostics = result.Diagnostics;
            if (result.Migrated)
            {
                await PreserveLegacyBackupAsync(originalBytes, cancellationToken).ConfigureAwait(false);
                await WriteSettingsAtomicAsync(Current, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException)
        {
            string invalidPath = SettingsPath + ".invalid-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            File.Move(SettingsPath, invalidPath, overwrite: false);
            Current = new EditorSettings();
            MigrationDiagnostics = ["The settings file was invalid and was moved aside. Default settings were loaded."];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
        WriteSettingsAtomicAsync(Current with { Version = EditorSettingsMigrator.CurrentVersion }, cancellationToken);

    public void Update(Func<EditorSettings, EditorSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Current = update(Current) with { Version = EditorSettingsMigrator.CurrentVersion };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask PreserveLegacyBackupAsync(byte[] originalBytes, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(SettingsPath) is { Length: > 0 } value ? value : ".";
        Directory.CreateDirectory(directory);
        string backup = Path.Combine(directory, EditorSettingsMigrator.LegacyBackupFileName);
        if (!File.Exists(backup))
        {
            await File.WriteAllBytesAsync(backup, originalBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteSettingsAtomicAsync(EditorSettings settings, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(SettingsPath) is { Length: > 0 } value ? value : ".";
        Directory.CreateDirectory(directory);
        string temporary = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, SettingsPath, overwrite: true);
            Current = settings;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public interface IRecentProjectService
{
    IReadOnlyList<RecentProject> RecentProjects { get; }
    event EventHandler? Changed;
    ValueTask LoadAsync(CancellationToken cancellationToken = default);
    ValueTask RecordAsync(RecentProject project, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(string projectRoot, CancellationToken cancellationToken = default);
    ValueTask RelocateAsync(string oldProjectRoot, string newProjectRoot, CancellationToken cancellationToken = default);
}

public sealed class RecentProjectService : IRecentProjectService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<RecentProject> _items = [];

    public RecentProjectService(string? path = null) => _path = path ?? Path.Combine(AppDataPaths.Root, "recent-projects.json");

    public event EventHandler? Changed;

    public IReadOnlyList<RecentProject> RecentProjects => _items
        .Select(item => item with { PathExists = Directory.Exists(item.ProjectRoot) })
        .OrderByDescending(item => item.LastOpenedUtc)
        .ToArray();

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        _items.Clear();
        if (!File.Exists(_path)) return;
        await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        List<RecentProject>? loaded = await JsonSerializer.DeserializeAsync<List<RecentProject>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (loaded is not null) _items.AddRange(loaded);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask RecordAsync(RecentProject project, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => string.Equals(item.ProjectRoot, project.ProjectRoot, PathComparison));
        _items.Add(project);
        if (_items.Count > 20)
        {
            _items.Sort((left, right) => right.LastOpenedUtc.CompareTo(left.LastOpenedUtc));
            _items.RemoveRange(20, _items.Count - 20);
        }
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask RemoveAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => string.Equals(item.ProjectRoot, projectRoot, PathComparison));
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask RelocateAsync(
        string oldProjectRoot,
        string newProjectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(newProjectRoot);
        string normalized = Path.GetFullPath(newProjectRoot);
        int index = _items.FindIndex(item => string.Equals(item.ProjectRoot, oldProjectRoot, PathComparison));
        if (index < 0) return;
        RecentProject existing = _items[index];
        _items[index] = existing with
        {
            ProjectRoot = normalized,
            Title = existing.Title ?? Path.GetFileName(normalized),
            PathExists = Directory.Exists(normalized)
        };
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using FileStream stream = new(_path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, _items, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Playroma", "JRPG");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Recovery => Path.Combine(Root, "recovery");
    public static string WorkspaceState => Path.Combine(Root, "workspace-state");
    public static string Cache => Path.Combine(Root, "cache");
}
