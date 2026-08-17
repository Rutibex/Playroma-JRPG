using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Visual;

/// <summary>Central logical-pixel measurements used by every Phase 11 editor surface.</summary>
public sealed record EditorVisualMetrics(
    int ScalePercent,
    EditorUiDensity Density,
    int CompactSpacing,
    int StandardSpacing,
    int SectionSpacing,
    int ControlHeight,
    int PrimaryButtonWidth,
    int ToolbarIconSize,
    int ToolbarHeight,
    int CategoryRailWidth,
    int RecordListWidth,
    int SplitterWidth,
    int FocusThickness,
    int MinimumPointerTarget)
{
    public static EditorVisualMetrics Create(ApplicationPreferenceSettings preferences, int deviceDpi = 96)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        double dpiScale = Math.Max(0.75, deviceDpi / 96d);
        double textScale = Math.Clamp(preferences.UiScalePercent, 100, 150) / 100d;
        double densityScale = preferences.UiDensity == EditorUiDensity.Compact ? 0.88d : 1d;
        int Px(double logical, bool densitySensitive = true) => Math.Max(1, (int)Math.Round(logical * dpiScale * textScale * (densitySensitive ? densityScale : 1d)));

        return new EditorVisualMetrics(
            preferences.UiScalePercent,
            preferences.UiDensity,
            Px(4),
            Px(8),
            Px(14),
            Px(preferences.UiDensity == EditorUiDensity.Compact ? 26 : 30),
            Px(92),
            Px(20, densitySensitive: false),
            Px(preferences.UiDensity == EditorUiDensity.Compact ? 28 : 32),
            Px(132),
            Px(218),
            Px(6),
            Math.Max(2, Px(2, densitySensitive: false)),
            Math.Max(24, Px(24, densitySensitive: false)));
    }

    public Padding WindowPadding => new(StandardSpacing);
    public Padding SectionPadding => new(StandardSpacing);
    public Padding CompactMargin => new(CompactSpacing);
}
