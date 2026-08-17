using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

/// <summary>
/// Persists normal bounds and maximized state while protecting users from windows restored
/// entirely off-screen after monitor or DPI changes.
/// </summary>
public sealed class WinFormsWindowPlacementService
{
    private const int MinimumVisibleWidth = 120;
    private const int MinimumVisibleTitleHeight = 36;
    private readonly ISettingsService _settings;

    public WinFormsWindowPlacementService(ISettingsService settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public void Restore(Form form, WindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(request);

        WindowPlacement? placement = _settings.Current.EffectiveWindowPlacements.FirstOrDefault(
            item => string.Equals(
                item.PlacementId,
                request.EffectivePlacementId,
                StringComparison.OrdinalIgnoreCase));
        if (placement is null) return;

        Rectangle desired = new(
            placement.NormalBounds.X,
            placement.NormalBounds.Y,
            Math.Max(form.MinimumSize.Width, placement.NormalBounds.Width),
            Math.Max(form.MinimumSize.Height, placement.NormalBounds.Height));

        Screen target = ResolveTargetScreen(placement, desired);
        Rectangle safe = ClampToWorkingArea(desired, target.WorkingArea, form.MinimumSize);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = safe;
        if (placement.Maximized) form.WindowState = FormWindowState.Maximized;
    }

    public void Capture(Form form, WindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(request);
        if (form.WindowState == FormWindowState.Minimized) return;

        Rectangle normal = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        if (normal.Width <= 0 || normal.Height <= 0) return;

        Screen monitor = Screen.FromRectangle(normal);
        WindowPlacement replacement = new(
            request.EffectivePlacementId,
            new ShellBounds(normal.X, normal.Y, normal.Width, normal.Height),
            form.WindowState == FormWindowState.Maximized,
            monitor.DeviceName);

        _settings.Update(current =>
        {
            List<WindowPlacement> placements = current.EffectiveWindowPlacements
                .Where(item => !string.Equals(
                    item.PlacementId,
                    replacement.PlacementId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            placements.Add(replacement);
            placements.Sort((left, right) => string.Compare(
                left.PlacementId,
                right.PlacementId,
                StringComparison.OrdinalIgnoreCase));
            return current with { WindowPlacements = placements, Version = EditorSettingsMigrator.CurrentVersion };
        });
    }

    private static Screen ResolveTargetScreen(WindowPlacement placement, Rectangle desired)
    {
        if (!string.IsNullOrWhiteSpace(placement.MonitorDeviceName))
        {
            Screen? remembered = Screen.AllScreens.FirstOrDefault(screen => string.Equals(
                screen.DeviceName,
                placement.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));
            if (remembered is not null) return remembered;
        }

        Screen intersecting = Screen.FromRectangle(desired);
        return intersecting ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private static Rectangle ClampToWorkingArea(
        Rectangle desired,
        Rectangle workingArea,
        Size minimumSize)
    {
        int minimumWidth = Math.Min(Math.Max(1, minimumSize.Width), workingArea.Width);
        int minimumHeight = Math.Min(Math.Max(1, minimumSize.Height), workingArea.Height);
        int width = Math.Clamp(desired.Width, minimumWidth, workingArea.Width);
        int height = Math.Clamp(desired.Height, minimumHeight, workingArea.Height);

        int minimumX = workingArea.Left - width + Math.Min(MinimumVisibleWidth, width);
        int maximumX = workingArea.Right - Math.Min(MinimumVisibleWidth, width);
        int minimumY = workingArea.Top;
        int maximumY = workingArea.Bottom - Math.Min(MinimumVisibleTitleHeight, height);

        int x = Math.Clamp(desired.X, minimumX, Math.Max(minimumX, maximumX));
        int y = Math.Clamp(desired.Y, minimumY, Math.Max(minimumY, maximumY));
        return new Rectangle(x, y, width, height);
    }
}
