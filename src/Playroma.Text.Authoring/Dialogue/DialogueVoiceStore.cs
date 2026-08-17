using System.Text.Json;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring.Dialogue;

public sealed class JsonDialogueVoiceProfileStore : IDialogueVoiceResolver
{
    private readonly string _directory;

    public JsonDialogueVoiceProfileStore(string directory)
    {
        _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public async Task<IReadOnlyList<(DialogueVoiceProfile Profile, string Path)>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var result = new List<(DialogueVoiceProfile, string)>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.playvoice.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                DialogueVoiceProfile? profile = JsonSerializer.Deserialize<DialogueVoiceProfile>(json, RecipeJson.Options);
                if (profile is not null && DialogueVoiceProfileValidator.Validate(profile).Count == 0) result.Add((profile, path));
            }
            catch (JsonException) { /* invalid project voice files are skipped; Voice Lab can surface them when opened directly */ }
        }
        return result;
    }

    public async ValueTask<DialogueVoiceProfile?> ResolveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Select(x => x.Profile).LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> SaveAsync(DialogueVoiceProfile profile, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = DialogueVoiceProfileValidator.Validate(profile);
        if (errors.Count != 0) throw new InvalidOperationException("Voice profile is invalid: " + string.Join(" ", errors));
        Directory.CreateDirectory(_directory);
        string path = string.IsNullOrWhiteSpace(existingPath)
            ? Path.Combine(_directory, SafeFileName(profile.Id) + ".playvoice.json")
            : Path.GetFullPath(existingPath);
        if (!IsUnderDirectory(path, _directory)) throw new InvalidOperationException("Voice profiles must be saved inside the project's PlayromaText/voices directory.");
        string json = JsonSerializer.Serialize(profile, RecipeJson.Options);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c)).ToArray();
        string text = new(chars);
        while (text.Contains("--", StringComparison.Ordinal)) text = text.Replace("--", "-", StringComparison.Ordinal);
        return text.Trim('-', '.');
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DialogueVoiceAssignmentStore
{
    private readonly string _path;

    public DialogueVoiceAssignmentStore(string path)
    {
        _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task<DialogueVoiceAssignmentDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new(DialogueVoiceAssignmentDocument.CurrentVersion, []);
        try
        {
            string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<DialogueVoiceAssignmentDocument>(json, RecipeJson.Options)
                ?? new(DialogueVoiceAssignmentDocument.CurrentVersion, []);
        }
        catch (JsonException) { return new(DialogueVoiceAssignmentDocument.CurrentVersion, []); }
    }

    public async Task SaveAsync(DialogueVoiceAssignmentDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        string json = JsonSerializer.Serialize(document, RecipeJson.Options);
        await File.WriteAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DialogueVoiceAssignment?> FindAsync(string targetKey, CancellationToken cancellationToken = default)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return document.Assignments.LastOrDefault(x => x.TargetKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(DialogueVoiceAssignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var list = document.Assignments.Where(x => !x.TargetKey.Equals(assignment.TargetKey, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(assignment with { UpdatedUtc = DateTimeOffset.UtcNow });
        await SaveAsync(new(DialogueVoiceAssignmentDocument.CurrentVersion, list), cancellationToken).ConfigureAwait(false);
    }
}
