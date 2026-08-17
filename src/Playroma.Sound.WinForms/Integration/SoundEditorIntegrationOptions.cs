namespace Playroma.Sound.WinForms.Integration;

/// <summary>
/// Optional host integration supplied when Playroma Sound is opened inside another editor.
/// The standalone Playroma Sound application continues to use the regular constructor and
/// therefore retains its original behaviour.
/// </summary>
public sealed class SoundEditorIntegrationOptions
{
    public SoundEditorIntegrationOptions(
        string rpgMakerProjectRoot,
        string hostProjectTitle,
        Action? requestHostClose = null)
    {
        if (string.IsNullOrWhiteSpace(rpgMakerProjectRoot))
            throw new ArgumentException("An RPG Maker MV project root is required.", nameof(rpgMakerProjectRoot));

        NormalizedProjectRoot = Path.GetFullPath(rpgMakerProjectRoot);
        HostProjectTitle = string.IsNullOrWhiteSpace(hostProjectTitle) ? "RPG Maker MV Project" : hostProjectTitle.Trim();
        RequestHostClose = requestHostClose;
    }

    public string NormalizedProjectRoot { get; }
    public string HostProjectTitle { get; }
    public Action? RequestHostClose { get; }
}
