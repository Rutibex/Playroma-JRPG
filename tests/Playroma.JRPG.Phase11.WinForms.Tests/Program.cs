using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.Rendering;
using Playroma.JRPG.Editor.WinForms.Visual;
using Playroma.JRPG.Eventing;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Run();
            Console.WriteLine("PASS Phase 11 WinForms visual-parity and accessibility tests");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 11 WinForms visual-parity and accessibility tests: " + exception);
            return 1;
        }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string settingsPath = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase11-" + Guid.NewGuid().ToString("N"), "settings.json");
        JsonSettingsService settings = new(settingsPath);
        settings.Update(current => current with
        {
            Theme = EditorTheme.Dark,
            Preferences = current.EffectivePreferences with
            {
                UiScalePercent = 125,
                UiDensity = EditorUiDensity.Comfortable,
                Version = 2
            },
            Version = 8
        });

        using WinFormsThemeService theme = new(settings);
        VerifyMetrics(theme);
        VerifyIcons(theme);
        VerifyThemeAndAccessibility(theme);
        VerifyEventCommandPalette(root, theme);
        VerifyMapCanvas(theme);
        VerifyAuditDetectsMissingName();
        VerifyLiveRegion();
        try { Directory.Delete(Path.GetDirectoryName(settingsPath)!, recursive: true); } catch { }
    }

    private static void VerifyMetrics(WinFormsThemeService theme)
    {
        using Form form = new() { Size = new Size(900, 650) };
        EditorVisualMetrics metrics = theme.GetMetrics(form);
        Assert(metrics.MinimumPointerTarget >= 24, "Pointer target fell below 24 logical pixels.");
        Assert(metrics.ToolbarIconSize >= 20, "Toolbar icon scale was not applied.");
        Assert(metrics.ControlHeight >= 30, "Comfortable density control height was too small.");
    }

    private static void VerifyIcons(WinFormsThemeService theme)
    {
        using Form owner = new();
        foreach (EditorIconKind kind in Enum.GetValues<EditorIconKind>())
        {
            using Bitmap copy = new(theme.Icon(kind, owner, 24));
            bool hasVisiblePixel = false;
            for (int y = 0; y < copy.Height && !hasVisiblePixel; y++)
            {
                for (int x = 0; x < copy.Width; x++)
                {
                    if (copy.GetPixel(x, y).A > 0) { hasVisiblePixel = true; break; }
                }
            }
            Assert(hasVisiblePixel, $"Icon {kind} rendered completely transparent.");
        }
    }

    private static void VerifyThemeAndAccessibility(WinFormsThemeService theme)
    {
        using Form form = new() { Name = "Phase11ThemeFixture", Text = "&Fixture", Size = new Size(640, 420) };
        TextBox editor = new() { Name = "Editor", AccessibleName = "Fixture editor", Dock = DockStyle.Top };
        Button action = new() { Name = "Action", Text = "&Apply", AutoSize = true, AccessibleDescription = "Apply the fixture." };
        form.Controls.Add(action);
        form.Controls.Add(editor);
        form.CreateControl();
        theme.Apply(form);
        form.PerformLayout();
        IReadOnlyList<AccessibilityAuditFinding> findings = AccessibilityAudit.Inspect(form, theme.GetMetrics(form).MinimumPointerTarget);
        Assert(findings.All(item => item.Severity != AccessibilityAuditSeverity.Error), string.Join(Environment.NewLine, findings));
        Assert(form.Font.Size > 9f, "UI text scaling did not increase the form font.");
        Assert(editor.BackColor == theme.Palette.InputBackground, "Text input did not receive the semantic input background.");
    }

    private static void VerifyEventCommandPalette(string root, WinFormsThemeService theme)
    {
        EventCommandRegistry registry = EventCommandRegistry.CreateBuiltIn(root);
        string usagePath = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase11-Usage-" + Guid.NewGuid().ToString("N") + ".json");
        EventCommandUsageService usage = new(usagePath);
        using EventCommandPaletteForm palette = new(registry, usage) { Size = new Size(1024, 760) };
        palette.CreateControl();
        theme.Apply(palette);
        palette.PerformLayout();
        using Bitmap image = new(palette.Width, palette.Height);
        palette.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        Assert(image.Width == 1024 && image.Height == 760, "Event command palette did not render at the requested size.");
        TabControl tabs = Find<TabControl>(palette) ?? throw new InvalidOperationException("Event command tabs were not created.");
        Assert(tabs.TabPages.Count >= 5, "Pages 1-3, Favorites and Recent were not present.");
        IReadOnlyList<AccessibilityAuditFinding> findings = AccessibilityAudit.Inspect(palette, 20);
        Assert(findings.All(item => item.Severity != AccessibilityAuditSeverity.Error), string.Join(Environment.NewLine, findings));
        try { File.Delete(usagePath); } catch { }
    }

    private static void VerifyMapCanvas(WinFormsThemeService theme)
    {
        using MapCanvasControl canvas = new() { Size = new Size(640, 420) };
        canvas.CreateControl();
        canvas.ApplyPalette(theme.Palette);
        Assert(canvas.TabStop, "Map canvas is not keyboard focusable.");
        Assert(!string.IsNullOrWhiteSpace(canvas.AccessibleName), "Map canvas has no accessible name.");
        Assert(canvas.BackColor == theme.Palette.CanvasBackground, "Map canvas did not use the semantic canvas color.");
    }

    private static void VerifyAuditDetectsMissingName()
    {
        using Form form = new() { Size = new Size(300, 200) };
        form.Controls.Add(new Button { Size = new Size(32, 32), Text = string.Empty, AccessibleName = null });
        form.CreateControl();
        IReadOnlyList<AccessibilityAuditFinding> findings = AccessibilityAudit.Inspect(form);
        Assert(findings.Any(item => item.Rule == "ACC001"), "Accessibility audit failed to detect an unnamed icon-only button.");
    }

    private static void VerifyLiveRegion()
    {
        using Form form = new();
        using LiveRegionAnnouncer announcer = new();
        announcer.Attach(form);
        announcer.Announce("Validation completed with no errors.");
        Assert(announcer.Region.AccessibleName == "Validation completed with no errors.", "Live region did not expose its announcement.");
    }

    private static T? Find<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T found) return found;
            if (Find<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Playroma.JRPG.sln.");
    }
}
