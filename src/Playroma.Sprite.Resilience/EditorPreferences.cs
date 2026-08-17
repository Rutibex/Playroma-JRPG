namespace Playroma.Sprite.Resilience;

public enum SpriteUiTheme { System, Light, Dark, HighContrast }

public sealed class SpriteShortcutBinding
{
    public string Command { get; set; } = string.Empty;
    public string Gesture { get; set; } = string.Empty;
    public string Scope { get; set; } = "global";
    public bool Enabled { get; set; } = true;
}

public sealed class SpriteEditorPreferences
{
    public string Schema { get; set; } = "playroma-sprite-preferences/1";
    public SpriteUiTheme Theme { get; set; } = SpriteUiTheme.System;
    public double UiScale { get; set; } = 1d;
    public double CanvasScale { get; set; } = 1d;
    public double FontScale { get; set; } = 1d;
    public bool HighContrast { get; set; }
    public bool ReduceMotion { get; set; }
    public bool ConfirmDestructiveOperations { get; set; } = true;
    public int AutosaveSeconds { get; set; } = 60;
    public int RecoverySnapshotsPerDocument { get; set; } = 12;
    public long UndoMemoryLimitBytes { get; set; } = 512L * 1024L * 1024L;
    public int BackgroundExportConcurrency { get; set; } = 2;
    public bool WarnOnLargeDocuments { get; set; } = true;
    public string ActiveWorkspaceId { get; set; } = "default";
    public long LargeDocumentPixelThreshold { get; set; } = 16_777_216;
    public List<SpriteShortcutBinding> Shortcuts { get; set; } = DefaultShortcuts();

    public static SpriteEditorPreferences Default() => new();

    public static List<SpriteShortcutBinding> DefaultShortcuts() =>
    [
        Bind("file.new", "Ctrl+N"), Bind("file.open", "Ctrl+O"), Bind("file.save", "Ctrl+S"), Bind("file.saveAs", "Ctrl+Shift+S"),
        Bind("edit.undo", "Ctrl+Z"), Bind("edit.redo", "Ctrl+Y"), Bind("edit.copy", "Ctrl+C"), Bind("edit.cut", "Ctrl+X"), Bind("edit.paste", "Ctrl+V"),
        Bind("selection.all", "Ctrl+A"), Bind("selection.clear", "Ctrl+D"), Bind("selection.invert", "Ctrl+Shift+I"),
        Bind("animation.play", "Space"), Bind("frame.previous", "Comma"), Bind("frame.next", "Period"),
        Bind("tool.pencil", "B"), Bind("tool.eraser", "E"), Bind("tool.line", "L"), Bind("tool.curve", "C"), Bind("tool.rectangle", "R"),
        Bind("tool.ellipse", "O"), Bind("tool.polygon", "P"), Bind("tool.fill", "G"), Bind("command.palette", "Ctrl+Shift+P"),
        Bind("view.fullscreenCanvas", "Tab"), Bind("view.resilience", "Ctrl+Alt+R")
    ];

    private static SpriteShortcutBinding Bind(string command, string gesture) => new() { Command = command, Gesture = gesture };
}

public sealed class SpritePreferencesStore
{
    public string Path { get; }
    public SpritePreferencesStore(string path) => Path = System.IO.Path.GetFullPath(path);

    public SpriteEditorPreferences LoadOrDefault()
    {
        try
        {
            SpriteEditorPreferences preferences = ResilienceJson.Read<SpriteEditorPreferences>(Path) ?? SpriteEditorPreferences.Default();
            ValidateAndRepair(preferences);
            return preferences;
        }
        catch
        {
            return SpriteEditorPreferences.Default();
        }
    }

    public void Save(SpriteEditorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ValidateAndRepair(preferences);
        ResilienceJson.WriteAtomic(Path, preferences);
    }

    public static IReadOnlyList<string> ValidateAndRepair(SpriteEditorPreferences preferences)
    {
        List<string> issues = [];
        if (preferences.Schema != "playroma-sprite-preferences/1") { issues.Add("Unsupported preferences schema was reset."); preferences.Schema = "playroma-sprite-preferences/1"; }
        preferences.UiScale = Math.Clamp(preferences.UiScale, .75d, 3d);
        preferences.CanvasScale = Math.Clamp(preferences.CanvasScale, .25d, 8d);
        preferences.FontScale = Math.Clamp(preferences.FontScale, .75d, 2d);
        preferences.AutosaveSeconds = Math.Clamp(preferences.AutosaveSeconds, 15, 3600);
        preferences.RecoverySnapshotsPerDocument = Math.Clamp(preferences.RecoverySnapshotsPerDocument, 2, 100);
        preferences.UndoMemoryLimitBytes = Math.Clamp(preferences.UndoMemoryLimitBytes, 16L * 1024L * 1024L, 8L * 1024L * 1024L * 1024L);
        preferences.BackgroundExportConcurrency = Math.Clamp(preferences.BackgroundExportConcurrency, 1, 8);
        preferences.LargeDocumentPixelThreshold = Math.Clamp(preferences.LargeDocumentPixelThreshold, 1_048_576, 1_073_741_824);
        preferences.ActiveWorkspaceId = string.IsNullOrWhiteSpace(preferences.ActiveWorkspaceId) ? "default" : preferences.ActiveWorkspaceId.Trim();
        preferences.Shortcuts ??= SpriteEditorPreferences.DefaultShortcuts();
        List<string> shortcutIssues = SpriteShortcutRegistry.Validate(preferences.Shortcuts).ToList();
        issues.AddRange(shortcutIssues);
        return issues;
    }
}

public static class SpriteShortcutRegistry
{
    public static IReadOnlyList<string> Validate(IEnumerable<SpriteShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        List<string> issues = [];
        HashSet<string> commands = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> gestures = new(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteShortcutBinding binding in bindings)
        {
            binding.Command = binding.Command.Trim();
            binding.Gesture = Normalize(binding.Gesture);
            binding.Scope = string.IsNullOrWhiteSpace(binding.Scope) ? "global" : binding.Scope.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(binding.Command)) { issues.Add("A shortcut has no command."); continue; }
            if (!commands.Add(binding.Command)) issues.Add($"Command '{binding.Command}' has more than one binding record.");
            if (!binding.Enabled || string.IsNullOrWhiteSpace(binding.Gesture)) continue;
            string key = binding.Scope + "|" + binding.Gesture;
            if (gestures.TryGetValue(key, out string? existing)) issues.Add($"Shortcut '{binding.Gesture}' conflicts between '{existing}' and '{binding.Command}'.");
            else gestures[key] = binding.Command;
        }
        return issues;
    }

    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;
        string[] parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<string> result = [];
        if (parts.Any(item => item.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || item.Equals("Control", StringComparison.OrdinalIgnoreCase) || item.Equals("CtrlKey", StringComparison.OrdinalIgnoreCase) || item.Equals("ControlKey", StringComparison.OrdinalIgnoreCase))) result.Add("Ctrl");
        if (parts.Any(item => item.Equals("Alt", StringComparison.OrdinalIgnoreCase) || item.Equals("AltKey", StringComparison.OrdinalIgnoreCase) || item.Equals("Menu", StringComparison.OrdinalIgnoreCase))) result.Add("Alt");
        if (parts.Any(item => item.Equals("Shift", StringComparison.OrdinalIgnoreCase) || item.Equals("ShiftKey", StringComparison.OrdinalIgnoreCase))) result.Add("Shift");
        string? key = parts.LastOrDefault(item => !new[] { "Ctrl", "Control", "Alt", "Shift", "CtrlKey", "AltKey", "ShiftKey" }.Contains(item, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(key)) result.Add(NormalizeKey(key));
        return string.Join('+', result);
    }

    private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "oemcomma" or "," => "Comma",
        "oemperiod" or "." => "Period",
        "oemplus" or "+" => "Plus",
        "oemminus" or "-" => "Minus",
        "escape" => "Esc",
        "return" => "Enter",
        "spacebar" => "Space",
        _ when key.Length == 1 => key.ToUpperInvariant(),
        _ => char.ToUpperInvariant(key[0]) + key[1..]
    };
}
