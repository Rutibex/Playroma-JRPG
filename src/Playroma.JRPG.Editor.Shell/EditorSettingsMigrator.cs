using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.JRPG.Editor.Shell;

/// <summary>
/// Upgrades editor settings written by the retired docked workspace to the dedicated
/// multi-window schema. Migration is intentionally one-way: old properties are read for
/// compatibility but are never written again.
/// </summary>
public sealed class EditorSettingsMigrator
{
    public const int CurrentVersion = 9;
    public const string LegacyBackupFileName = "settings.pre-phase12-v8.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public EditorSettingsMigrationResult Migrate(JsonDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        JsonElement root = source.RootElement;
        int sourceVersion = ReadInt(root, "Version") ?? 1;
        EditorSettings settings = JsonSerializer.Deserialize<EditorSettings>(root.GetRawText(), SerializerOptions)
            ?? new EditorSettings();
        List<string> diagnostics = [];

        if (sourceVersion > CurrentVersion)
        {
            diagnostics.Add($"Settings version {sourceVersion} is newer than this editor. Known fields were loaded without rewriting the file.");
            return new EditorSettingsMigrationResult(
                settings with { Version = sourceVersion },
                sourceVersion,
                Migrated: false,
                diagnostics);
        }

        bool containedLegacyShellState = HasProperty(root, "Layout")
            || HasProperty(root, "MainWindowBounds")
            || HasProperty(root, "MainWindowMaximized");
        List<WindowPlacement> placements = settings.EffectiveWindowPlacements.ToList();
        bool hasMapEditorPlacement = placements.Any(item =>
            string.Equals(item.PlacementId, "MapEditor", StringComparison.OrdinalIgnoreCase));

        ShellBounds? legacyBounds = ReadBounds(root, "MainWindowBounds");
        bool legacyMaximized = ReadBool(root, "MainWindowMaximized") ?? false;
        if (!hasMapEditorPlacement && legacyBounds is { Width: > 0, Height: > 0 })
        {
            placements.Add(new WindowPlacement(
                "MapEditor",
                NormalizeBounds(legacyBounds),
                legacyMaximized));
            diagnostics.Add("Converted the retired main-window bounds into the Map Editor placement.");
        }

        MapEditorLayoutSettings mapLayout = settings.EffectiveMapEditorLayout;
        if (!HasProperty(root, "MapEditorLayout") && TryReadLeftProportion(root, out double leftProportion))
        {
            int sourceWidth = legacyBounds?.Width > 0 ? legacyBounds.Width : 1280;
            int leftPanelWidth = Math.Clamp((int)Math.Round(sourceWidth * leftProportion), 220, 520);
            mapLayout = mapLayout with { LeftPanelWidth = leftPanelWidth };
            diagnostics.Add("Converted the retired left dock proportion into the Map Editor navigation width.");
        }

        bool migrated = sourceVersion < CurrentVersion || containedLegacyShellState;
        EditorSettings upgraded = settings with
        {
            WindowPlacements = placements,
            MapEditorLayout = mapLayout,
            Version = CurrentVersion
        };
        if (migrated)
        {
            diagnostics.Add($"Upgraded editor settings from version {sourceVersion} to version {CurrentVersion}.");
        }
        return new EditorSettingsMigrationResult(upgraded, sourceVersion, migrated, diagnostics);
    }

    private static ShellBounds NormalizeBounds(ShellBounds bounds) => new(
        bounds.X,
        bounds.Y,
        Math.Clamp(bounds.Width, 640, 16_384),
        Math.Clamp(bounds.Height, 480, 16_384));

    private static bool TryReadLeftProportion(JsonElement root, out double value)
    {
        value = 0;
        if (!TryGetProperty(root, "Layout", out JsonElement layout)
            || layout.ValueKind != JsonValueKind.Object
            || !TryGetProperty(layout, "LeftProportion", out JsonElement proportion)
            || !proportion.TryGetDouble(out value))
        {
            return false;
        }
        value = Math.Clamp(value, 0.10, 0.50);
        return true;
    }

    private static ShellBounds? ReadBounds(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
            return null;
        int? x = ReadInt(element, "X");
        int? y = ReadInt(element, "Y");
        int? width = ReadInt(element, "Width");
        int? height = ReadInt(element, "Height");
        return x.HasValue && y.HasValue && width.HasValue && height.HasValue
            ? new ShellBounds(x.Value, y.Value, width.Value, height.Value)
            : null;
    }

    private static int? ReadInt(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static bool? ReadBool(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool HasProperty(JsonElement element, string name) => TryGetProperty(element, name, out _);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}

public sealed record EditorSettingsMigrationResult(
    EditorSettings Settings,
    int SourceVersion,
    bool Migrated,
    IReadOnlyList<string> Diagnostics);
