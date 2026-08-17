namespace Playroma.JRPG.Editor.WinForms.Accessibility;

/// <summary>Ensures keyboard focus is scrolled into view inside nested WinForms containers.</summary>
public static class FocusVisibilityService
{
    public static void Install(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (Control control in AccessibilityMetadata.Enumerate(root).Prepend(root))
        {
            control.Enter -= OnControlEnter;
            control.Enter += OnControlEnter;
        }
    }

    private static void OnControlEnter(object? sender, EventArgs e)
    {
        if (sender is not Control focused) return;
        for (Control? parent = focused.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollableControl scrollable && scrollable.AutoScroll)
            {
                scrollable.ScrollControlIntoView(focused);
            }
        }
    }
}
