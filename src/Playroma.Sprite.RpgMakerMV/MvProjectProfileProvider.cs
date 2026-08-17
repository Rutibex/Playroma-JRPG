using System.Text.Json;

namespace Playroma.Sprite.RpgMakerMV;

public static class MvProjectProfileProvider
{
    public const string ProjectProfileRelativePath = "data/PlayromaSprite/RpgMakerMvImageProfiles.json";

    public static RpgMakerMvProfileRegistry LoadForProject(string? projectRoot)
    {
        RpgMakerMvProfileRegistry builtIn = RpgMakerMvProfileRegistry.LoadBuiltIn();
        if (string.IsNullOrWhiteSpace(projectRoot)) return builtIn;
        string path = Path.Combine(Path.GetFullPath(projectRoot), ProjectProfileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? builtIn.MergeFromFile(path, allowOverride: true) : builtIn;
    }

    public static void WriteProjectTemplate(string projectRoot, bool overwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        string path = Path.Combine(Path.GetFullPath(projectRoot), ProjectProfileRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path) && !overwrite) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RpgMakerMvProfileCatalog catalog = new()
        {
            Profiles = []
        };
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(catalog, options) + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
