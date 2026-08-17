using Microsoft.Win32;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Visual;

namespace Playroma.JRPG.Editor.WinForms;

/// <summary>Semantic colors used by themed and owner-drawn editor controls.</summary>
public sealed record ThemePalette
{
    public required Color WindowBackground { get; init; }
    public required Color PanelBackground { get; init; }
    public required Color EditorBackground { get; init; }
    public required Color RaisedSurface { get; init; }
    public required Color InputBackground { get; init; }
    public required Color InputBorder { get; init; }
    public required Color PrimaryText { get; init; }
    public required Color SecondaryText { get; init; }
    public required Color Selection { get; init; }
    public required Color SelectionText { get; init; }
    public required Color Border { get; init; }
    public required Color Divider { get; init; }
    public required Color FocusIndicator { get; init; }
    public required Color ToolbarBackground { get; init; }
    public required Color ToolbarHot { get; init; }
    public required Color ToolbarPressed { get; init; }
    public required Color Error { get; init; }
    public required Color Warning { get; init; }
    public required Color Information { get; init; }
    public required Color Success { get; init; }
    public required Color DisabledText { get; init; }
    public required Color CanvasBackground { get; init; }
    public required Color CheckerboardLight { get; init; }
    public required Color CheckerboardDark { get; init; }
    public bool HighContrast { get; init; }
}

/// <summary>
/// Applies Playroma's semantic light/dark design system while deferring to Windows system
/// colors whenever High Contrast is enabled. It also owns the clean-room icon catalogue and
/// central DPI/text-scale metrics used by all Phase 11 forms.
/// </summary>
public sealed class WinFormsThemeService : IDisposable
{
    private readonly ISettingsService _settings;
    private bool _disposed;

    public WinFormsThemeService(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public EditorTheme Theme => _settings.Current.Theme;
    public event EventHandler? ThemeChanged;
    public EditorIconCatalog Icons { get; } = new();
    public ThemePalette Palette => ResolvePalette(Theme);
    public bool IsHighContrast => SystemInformation.HighContrast;

    public EditorVisualMetrics GetMetrics(Control? control = null) =>
        EditorVisualMetrics.Create(_settings.Current.EffectivePreferences, control?.DeviceDpi ?? 96);

    public void SetTheme(EditorTheme theme)
    {
        _settings.Update(settings => settings with { Theme = theme, Version = EditorSettingsMigrator.CurrentVersion });
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyVisualPreferencesChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ThemePalette palette = Palette;
        EditorVisualMetrics metrics = GetMetrics(root);
        ApplyRootFont(root);
        ApplyRecursive(root, palette, metrics);
        if (root is Form form)
        {
            form.BackColor = palette.WindowBackground;
            AccessibilityMetadata.ApplyFormDefaults(form);
        }
        root.Invalidate(true);
    }

    public Image Icon(EditorIconKind kind, Control owner, int? logicalSize = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ThemePalette palette = Palette;
        EditorVisualMetrics metrics = GetMetrics(owner);
        int size = logicalSize ?? metrics.ToolbarIconSize;
        return Icons.Get(kind, size, palette.PrimaryText, palette.Information);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Icons.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ApplyRootFont(Control root)
    {
        if (root is not Form && root.Parent is not null) return;
        ApplicationPreferenceSettings preferences = _settings.Current.EffectivePreferences;
        float scale = Math.Clamp(preferences.UiScalePercent, 100, 150) / 100f;
        Font source = preferences.FollowWindowsTextSize ? SystemFonts.MessageBoxFont : SystemFonts.DefaultFont;
        float baseline = preferences.FollowWindowsTextSize ? source.Size : 9f;
        float size = Math.Max(7f, baseline * scale);
        if (Math.Abs(root.Font.Size - size) > 0.05f || root.Font.FontFamily.Name != source.FontFamily.Name)
        {
            root.Font = new Font(source.FontFamily, size, source.Style, GraphicsUnit.Point);
        }
    }

    private static void ApplyRecursive(Control control, ThemePalette palette, EditorVisualMetrics metrics)
    {
        control.ForeColor = control.Enabled ? palette.PrimaryText : palette.DisabledText;
        control.BackColor = control switch
        {
            TextBoxBase or ListBox or ComboBox or NumericUpDown or ListView or TreeView or DataGridView or PropertyGrid => palette.InputBackground,
            TabPage or Panel or GroupBox or SplitContainer or UserControl => palette.PanelBackground,
            _ => palette.WindowBackground
        };

        switch (control)
        {
            case Form form:
                form.BackColor = palette.WindowBackground;
                break;
            case Button button:
                button.MinimumSize = new Size(
                    Math.Max(button.MinimumSize.Width, button.AutoSize ? 0 : metrics.MinimumPointerTarget),
                    Math.Max(button.MinimumSize.Height, metrics.MinimumPointerTarget));
                button.UseVisualStyleBackColor = palette.HighContrast;
                button.FlatStyle = palette.HighContrast ? FlatStyle.System : FlatStyle.Flat;
                if (!palette.HighContrast)
                {
                    button.FlatAppearance.BorderColor = palette.InputBorder;
                    button.FlatAppearance.MouseOverBackColor = palette.ToolbarHot;
                    button.FlatAppearance.MouseDownBackColor = palette.ToolbarPressed;
                    button.BackColor = palette.RaisedSurface;
                }
                break;
            case CheckBox checkBox:
                checkBox.UseVisualStyleBackColor = palette.HighContrast;
                break;
            case RadioButton radioButton:
                radioButton.UseVisualStyleBackColor = palette.HighContrast;
                break;
            case DataGridView grid:
                grid.BackgroundColor = palette.EditorBackground;
                grid.GridColor = palette.Divider;
                grid.DefaultCellStyle.BackColor = palette.InputBackground;
                grid.DefaultCellStyle.ForeColor = palette.PrimaryText;
                grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
                grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
                grid.ColumnHeadersDefaultCellStyle.BackColor = palette.RaisedSurface;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.PrimaryText;
                grid.RowHeadersDefaultCellStyle.BackColor = palette.RaisedSurface;
                grid.RowHeadersDefaultCellStyle.ForeColor = palette.PrimaryText;
                grid.EnableHeadersVisualStyles = false;
                break;
            case ListView list:
                list.BackColor = palette.InputBackground;
                list.ForeColor = palette.PrimaryText;
                break;
            case TreeView tree:
                tree.BackColor = palette.InputBackground;
                tree.ForeColor = palette.PrimaryText;
                tree.LineColor = palette.Divider;
                break;
            case ListBox listBox:
                listBox.BackColor = palette.InputBackground;
                listBox.ForeColor = palette.PrimaryText;
                break;
            case TextBoxBase text:
                text.BackColor = text.ReadOnly ? palette.EditorBackground : palette.InputBackground;
                text.ForeColor = palette.PrimaryText;
                break;
            case StatusStrip status:
                status.BackColor = palette.ToolbarBackground;
                status.ForeColor = palette.PrimaryText;
                status.Renderer = new EditorToolStripRenderer(palette, metrics.FocusThickness);
                foreach (ToolStripItem item in status.Items) item.ForeColor = palette.PrimaryText;
                break;
            case ToolStrip strip:
                strip.BackColor = palette.ToolbarBackground;
                strip.ForeColor = palette.PrimaryText;
                strip.Renderer = new EditorToolStripRenderer(palette, metrics.FocusThickness);
                strip.ImageScalingSize = new Size(metrics.ToolbarIconSize, metrics.ToolbarIconSize);
                strip.AutoSize = true;
                foreach (ToolStripItem item in strip.Items) item.ForeColor = palette.PrimaryText;
                break;
            case TabControl tabs:
                tabs.Padding = new Point(Math.Max(8, metrics.StandardSpacing + 4), Math.Max(3, metrics.CompactSpacing));
                break;
        }

        foreach (Control child in control.Controls) ApplyRecursive(child, palette, metrics);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Category is UserPreferenceCategory.Accessibility
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.General
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Window)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ThemePalette ResolvePalette(EditorTheme theme)
    {
        if (SystemInformation.HighContrast)
        {
            return new ThemePalette
            {
                WindowBackground = SystemColors.Window,
                PanelBackground = SystemColors.Control,
                EditorBackground = SystemColors.Window,
                RaisedSurface = SystemColors.Control,
                InputBackground = SystemColors.Window,
                InputBorder = SystemColors.WindowText,
                PrimaryText = SystemColors.WindowText,
                SecondaryText = SystemColors.GrayText,
                Selection = SystemColors.Highlight,
                SelectionText = SystemColors.HighlightText,
                Border = SystemColors.WindowText,
                Divider = SystemColors.WindowText,
                FocusIndicator = SystemColors.Highlight,
                ToolbarBackground = SystemColors.Menu,
                ToolbarHot = SystemColors.Highlight,
                ToolbarPressed = SystemColors.HotTrack,
                Error = SystemColors.WindowText,
                Warning = SystemColors.WindowText,
                Information = SystemColors.WindowText,
                Success = SystemColors.WindowText,
                DisabledText = SystemColors.GrayText,
                CanvasBackground = SystemColors.Window,
                CheckerboardLight = SystemColors.Window,
                CheckerboardDark = SystemColors.Control,
                HighContrast = true
            };
        }

        bool dark = theme == EditorTheme.Dark || (theme == EditorTheme.System && IsSystemDark());
        return dark
            ? new ThemePalette
            {
                WindowBackground = Color.FromArgb(37, 41, 43),
                PanelBackground = Color.FromArgb(48, 53, 55),
                EditorBackground = Color.FromArgb(30, 34, 36),
                RaisedSurface = Color.FromArgb(57, 62, 65),
                InputBackground = Color.FromArgb(30, 34, 36),
                InputBorder = Color.FromArgb(91, 99, 103),
                PrimaryText = Color.FromArgb(235, 238, 239),
                SecondaryText = Color.FromArgb(175, 184, 188),
                Selection = Color.FromArgb(51, 103, 170),
                SelectionText = Color.White,
                Border = Color.FromArgb(76, 83, 86),
                Divider = Color.FromArgb(72, 79, 82),
                FocusIndicator = Color.FromArgb(102, 177, 255),
                ToolbarBackground = Color.FromArgb(43, 48, 50),
                ToolbarHot = Color.FromArgb(63, 72, 77),
                ToolbarPressed = Color.FromArgb(29, 82, 141),
                Error = Color.FromArgb(230, 94, 94),
                Warning = Color.FromArgb(231, 183, 80),
                Information = Color.FromArgb(89, 161, 223),
                Success = Color.FromArgb(92, 184, 120),
                DisabledText = Color.FromArgb(115, 122, 125),
                CanvasBackground = Color.FromArgb(24, 27, 29),
                CheckerboardLight = Color.FromArgb(75, 80, 83),
                CheckerboardDark = Color.FromArgb(57, 62, 65)
            }
            : new ThemePalette
            {
                WindowBackground = SystemColors.Control,
                PanelBackground = Color.FromArgb(243, 245, 246),
                EditorBackground = Color.White,
                RaisedSurface = Color.FromArgb(250, 251, 252),
                InputBackground = Color.White,
                InputBorder = Color.FromArgb(169, 175, 179),
                PrimaryText = Color.FromArgb(30, 34, 36),
                SecondaryText = Color.FromArgb(85, 91, 94),
                Selection = Color.FromArgb(51, 103, 170),
                SelectionText = Color.White,
                Border = Color.FromArgb(195, 199, 201),
                Divider = Color.FromArgb(207, 211, 214),
                FocusIndicator = Color.FromArgb(0, 95, 184),
                ToolbarBackground = Color.FromArgb(238, 241, 243),
                ToolbarHot = Color.FromArgb(219, 231, 243),
                ToolbarPressed = Color.FromArgb(190, 215, 239),
                Error = Color.FromArgb(184, 42, 42),
                Warning = Color.FromArgb(167, 109, 16),
                Information = Color.FromArgb(25, 104, 164),
                Success = Color.FromArgb(35, 128, 67),
                DisabledText = Color.FromArgb(145, 149, 151),
                CanvasBackground = Color.FromArgb(48, 51, 54),
                CheckerboardLight = Color.FromArgb(230, 232, 234),
                CheckerboardDark = Color.FromArgb(202, 205, 208)
            };
    }

    private static bool IsSystemDark()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int number && number == 0;
        }
        catch { return false; }
    }
}
