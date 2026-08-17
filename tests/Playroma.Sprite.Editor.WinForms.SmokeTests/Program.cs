using Playroma.Sprite.Editor.WinForms;

try
{
    ApplicationConfiguration.Initialize();
    using MainForm form = new();
    if (!form.Text.Contains("Playroma Sprite Studio", StringComparison.Ordinal)) throw new InvalidOperationException("Window title missing.");
    if (form.Controls.Count == 0) throw new InvalidOperationException("Sprite editor window is empty.");
    string[] required = ["Layers", "Animation", "Preview", "Selection", "History", "Brush", "Dynamics", "Symmetry & Tile", "Guides", "Color & Palette", "Effects", "MV System Assets", "Automation", "Resilience & Polish", "MV Validation"];
    TabControl? tabs = Find<TabControl>(form);
    if (tabs is null) throw new InvalidOperationException("Phase 10 property tabs are missing.");
    foreach (string page in required)
        if (!tabs.TabPages.Cast<TabPage>().Any(item => item.Text == page)) throw new InvalidOperationException("Missing editor tab: " + page);
    Console.WriteLine("PASS Playroma Sprite WinForms Phase 10 resilience and polish workspace smoke test");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}

static T? Find<T>(Control root) where T : Control
{
    foreach (Control child in root.Controls)
    {
        if (child is T found) return found;
        T? nested = Find<T>(child);
        if (nested is not null) return nested;
    }
    return null;
}
