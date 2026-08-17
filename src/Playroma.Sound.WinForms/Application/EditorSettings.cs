using System.Text.Json;

namespace Playroma.Sound.WinForms.Application;

public sealed class EditorSettings
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string LastProjectPath { get; set; } = "";
    public string LastAudioDeviceId { get; set; } = "";
    public float MasterGain { get; set; } = 1f;
    public int AutosaveSeconds { get; set; } = 120;
    public int WindowX { get; set; } = 80;
    public int WindowY { get; set; } = 80;
    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
    public int ExplorerWidth { get; set; } = 270;
    public int InspectorWidth { get; set; } = 320;
    public int DiagnosticsHeight { get; set; } = 210;

    public void Normalize()
    {
        FormatVersion = CurrentFormatVersion;
        MasterGain = float.IsFinite(MasterGain) ? Math.Clamp(MasterGain, 0f, 2f) : 1f;
        AutosaveSeconds = Math.Clamp(AutosaveSeconds, 15, 3600);
        WindowWidth = Math.Clamp(WindowWidth, 900, 7680);
        WindowHeight = Math.Clamp(WindowHeight, 600, 4320);
        ExplorerWidth = Math.Clamp(ExplorerWidth, 180, 800);
        InspectorWidth = Math.Clamp(InspectorWidth, 220, 900);
        DiagnosticsHeight = Math.Clamp(DiagnosticsHeight, 120, 700);
    }
}

public sealed class EditorSettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public EditorSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public EditorSettings Load()
    {
        if (!File.Exists(_path)) return new EditorSettings();
        try
        {
            EditorSettings settings = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(_path), _jsonOptions) ?? new EditorSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _ = exception;
            return new EditorSettings();
        }
    }

    public void Save(EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, _jsonOptions));
        File.Move(temporaryPath, _path, true);
    }
}
