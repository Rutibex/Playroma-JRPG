namespace Playroma.Sound.WinForms.Application;

public sealed record EditorApplicationPaths(
    string ApplicationDataDirectory,
    string SettingsFilePath,
    string RecentProjectsFilePath,
    string AutosaveDirectory)
{
    public static EditorApplicationPaths CreateDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Playroma",
            "Sound");
        return new EditorApplicationPaths(
            root,
            Path.Combine(root, "editor-settings.json"),
            Path.Combine(root, "recent-projects.json"),
            Path.Combine(root, "Autosave"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(AutosaveDirectory);
    }
}
