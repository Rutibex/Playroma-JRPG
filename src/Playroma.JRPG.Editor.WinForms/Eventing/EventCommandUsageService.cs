using System.Text.Json;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed record EventCommandUsageSettings(
    IReadOnlyList<int>? FavoriteCommandCodes = null,
    IReadOnlyList<int>? RecentCommandCodes = null,
    int LastSelectedPage = 0);

/// <summary>
/// Stores palette favorites and recent commands as application preferences. Project data is never
/// modified by this service, and settings failures never block event editing.
/// </summary>
public sealed class EventCommandUsageService
{
    private const int MaximumRecent = 20;
    private const int MaximumFavorites = 24;
    private readonly string _settingsPath;
    private readonly List<int> _favorites = [];
    private readonly List<int> _recent = [];

    public EventCommandUsageService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Playroma",
            "PlayromaJRPG",
            "event-command-usage.json");
        Load();
    }

    public IReadOnlyList<int> Favorites => _favorites.ToArray();
    public IReadOnlyList<int> Recent => _recent.ToArray();
    public int LastSelectedPage { get; set; }

    public bool IsFavorite(int code) => _favorites.Contains(code);

    public void ToggleFavorite(int code)
    {
        if (_favorites.Remove(code))
        {
            Save();
            return;
        }
        _favorites.Insert(0, code);
        if (_favorites.Count > MaximumFavorites) _favorites.RemoveRange(MaximumFavorites, _favorites.Count - MaximumFavorites);
        Save();
    }

    public void RecordUse(int code)
    {
        _recent.Remove(code);
        _recent.Insert(0, code);
        if (_recent.Count > MaximumRecent) _recent.RemoveRange(MaximumRecent, _recent.Count - MaximumRecent);
        Save();
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            EventCommandUsageSettings settings = new(_favorites, _recent, LastSelectedPage);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            EventCommandUsageSettings? settings = JsonSerializer.Deserialize<EventCommandUsageSettings>(File.ReadAllText(_settingsPath));
            if (settings is null) return;
            foreach (int code in settings.FavoriteCommandCodes ?? [])
            {
                if (!_favorites.Contains(code) && _favorites.Count < MaximumFavorites) _favorites.Add(code);
            }
            foreach (int code in settings.RecentCommandCodes ?? [])
            {
                if (!_recent.Contains(code) && _recent.Count < MaximumRecent) _recent.Add(code);
            }
            LastSelectedPage = Math.Clamp(settings.LastSelectedPage, 0, 4);
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
