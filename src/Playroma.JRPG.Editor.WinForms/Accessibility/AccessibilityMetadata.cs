namespace Playroma.JRPG.Editor.WinForms.Accessibility;

/// <summary>Reusable accessibility metadata and keyboard-layout helpers.</summary>
public static class AccessibilityMetadata
{
    public static T Name<T>(T control, string name, string? description = null, AccessibleRole? role = null)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        control.AccessibleName = name;
        if (!string.IsNullOrWhiteSpace(description)) control.AccessibleDescription = description;
        if (role.HasValue) control.AccessibleRole = role.Value;
        return control;
    }

    public static Label CreateLabel(string text, Control target, int marginTop = 7)
    {
        ArgumentNullException.ThrowIfNull(target);
        Label label = new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, marginTop, 8, 3)
        };
        label.UseMnemonic = true;
        label.TabStop = false;
        target.AccessibleName ??= StripMnemonic(text);
        label.Click += (_, _) => target.Focus();
        return label;
    }

    public static void ApplyFormDefaults(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.AccessibleName ??= StripMnemonic(form.Text);
        AssignAutomaticNames(form);
        NormalizeTabOrder(form);
    }

    public static void AssignAutomaticNames(Control root)
    {
        foreach (Control control in Enumerate(root))
        {
            if (string.IsNullOrWhiteSpace(control.AccessibleName))
            {
                string text = StripMnemonic(control.Text);
                if (!string.IsNullOrWhiteSpace(text)) control.AccessibleName = text;
                else if (!string.IsNullOrWhiteSpace(control.Name)) control.AccessibleName = SplitIdentifier(control.Name);
            }
        }
    }

    public static void SetSequentialTabOrder(Control root) => NormalizeTabOrder(root);

    public static void NormalizeTabOrder(Control root)
    {
        foreach (Control parent in Enumerate(root).Prepend(root))
        {
            Control[] focusable = parent.Controls.Cast<Control>()
                .Where(control => control.TabStop && control.Visible)
                .OrderBy(control => control.Top)
                .ThenBy(control => control.Left)
                .ToArray();
            for (int index = 0; index < focusable.Length; index++) focusable[index].TabIndex = index;
        }
    }

    public static IEnumerable<Control> Enumerate(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Enumerate(child)) yield return descendant;
        }
    }

    public static string StripMnemonic(string? text) => (text ?? string.Empty).Replace("&&", "\0", StringComparison.Ordinal).Replace("&", string.Empty, StringComparison.Ordinal).Replace("\0", "&", StringComparison.Ordinal).Trim();

    private static string SplitIdentifier(string identifier)
    {
        string trimmed = identifier.TrimStart('_');
        if (trimmed.Length == 0) return "Control";
        System.Text.StringBuilder builder = new();
        for (int index = 0; index < trimmed.Length; index++)
        {
            char value = trimmed[index];
            if (index > 0 && char.IsUpper(value) && !char.IsUpper(trimmed[index - 1])) builder.Append(' ');
            builder.Append(value);
        }
        return builder.ToString();
    }
}
