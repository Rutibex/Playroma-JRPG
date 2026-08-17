namespace Playroma.Sprite.Resilience;

public enum SpritePanelDock { Left, Right, Bottom, Floating, Hidden }

public sealed class SpriteWorkspacePanelState
{
    public string Key { get; set; } = string.Empty;
    public SpritePanelDock Dock { get; set; } = SpritePanelDock.Right;
    public bool Visible { get; set; } = true;
    public double SizeRatio { get; set; } = .25d;
    public int Order { get; set; }
}

public sealed class SpriteWorkspaceProfile
{
    public string Schema { get; set; } = "playroma-sprite-workspace/1";
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Default";
    public int WindowX { get; set; } = 80;
    public int WindowY { get; set; } = 80;
    public int WindowWidth { get; set; } = 1540;
    public int WindowHeight { get; set; } = 940;
    public bool Maximized { get; set; }
    public bool MirrorInspector { get; set; }
    public bool TimelineVisible { get; set; } = true;
    public int InspectorWidth { get; set; } = 410;
    public int TimelineHeight { get; set; } = 250;
    public string ActiveInspectorTab { get; set; } = "Layers";
    public int CanvasZoom { get; set; } = 4;
    public List<SpriteWorkspacePanelState> Panels { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SpriteWorkspaceProfileStore
{
    public string DirectoryPath { get; }
    public SpriteWorkspaceProfileStore(string directoryPath) => DirectoryPath = Path.GetFullPath(directoryPath);

    public IReadOnlyList<SpriteWorkspaceProfile> LoadAll()
    {
        Directory.CreateDirectory(DirectoryPath);
        List<SpriteWorkspaceProfile> profiles = [];
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.playroma-workspace.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                SpriteWorkspaceProfile? profile = ResilienceJson.Read<SpriteWorkspaceProfile>(path);
                if (profile is not null) { Validate(profile); profiles.Add(profile); }
            }
            catch { }
        }
        if (profiles.Count == 0) profiles.Add(Default());
        return profiles;
    }

    public SpriteWorkspaceProfile Load(string id)
    {
        string safe = SafeId(id);
        string path = Path.Combine(DirectoryPath, safe + ".playroma-workspace.json");
        SpriteWorkspaceProfile profile = ResilienceJson.Read<SpriteWorkspaceProfile>(path) ?? Default();
        Validate(profile);
        return profile;
    }

    public void Save(SpriteWorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        profile.SavedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(DirectoryPath);
        ResilienceJson.WriteAtomic(Path.Combine(DirectoryPath, SafeId(profile.Id) + ".playroma-workspace.json"), profile);
    }

    public void Delete(string id)
    {
        string path = Path.Combine(DirectoryPath, SafeId(id) + ".playroma-workspace.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static SpriteWorkspaceProfile Default() => new()
    {
        Panels =
        [
            new() { Key = "inspector", Dock = SpritePanelDock.Right, Visible = true, SizeRatio = .27d },
            new() { Key = "timeline", Dock = SpritePanelDock.Bottom, Visible = true, SizeRatio = .25d }
        ]
    };

    public static void Validate(SpriteWorkspaceProfile profile)
    {
        if (profile.Schema != "playroma-sprite-workspace/1") throw new InvalidDataException("Unsupported workspace profile schema.");
        profile.Id = SafeId(profile.Id);
        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Workspace" : profile.Name.Trim();
        profile.WindowWidth = Math.Clamp(profile.WindowWidth, 800, 10000);
        profile.WindowHeight = Math.Clamp(profile.WindowHeight, 600, 10000);
        profile.InspectorWidth = Math.Clamp(profile.InspectorWidth, 240, 2400);
        profile.TimelineHeight = Math.Clamp(profile.TimelineHeight, 100, 1600);
        profile.CanvasZoom = Math.Clamp(profile.CanvasZoom, 1, 64);
        foreach (SpriteWorkspacePanelState panel in profile.Panels)
            panel.SizeRatio = Math.Clamp(panel.SizeRatio, .05d, .9d);
    }

    private static string SafeId(string? id)
    {
        string value = string.IsNullOrWhiteSpace(id) ? "default" : id.Trim().ToLowerInvariant();
        value = new string(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(value) ? "default" : value;
    }
}
