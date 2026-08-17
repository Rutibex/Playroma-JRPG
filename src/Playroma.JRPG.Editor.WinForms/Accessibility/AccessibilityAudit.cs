namespace Playroma.JRPG.Editor.WinForms.Accessibility;

public enum AccessibilityAuditSeverity { Information, Warning, Error }

public sealed record AccessibilityAuditFinding(
    AccessibilityAuditSeverity Severity,
    string ControlPath,
    string Rule,
    string Message);

/// <summary>Fast control-tree audit used by Phase 11 smoke tests and verification tooling.</summary>
public static class AccessibilityAudit
{
    public static IReadOnlyList<AccessibilityAuditFinding> Inspect(Control root, int minimumTarget = 24)
    {
        ArgumentNullException.ThrowIfNull(root);
        List<AccessibilityAuditFinding> findings = [];
        HashSet<Control> visited = [];
        Visit(root, root.Name.Length > 0 ? root.Name : root.GetType().Name, findings, visited, minimumTarget);
        return findings;
    }

    private static void Visit(
        Control control,
        string path,
        List<AccessibilityAuditFinding> findings,
        HashSet<Control> visited,
        int minimumTarget)
    {
        if (!visited.Add(control)) return;
        bool interactive = control is ButtonBase or TextBoxBase or NumericUpDown or ListControl or TreeView or ListView or DataGridView;
        if (interactive && string.IsNullOrWhiteSpace(control.AccessibleName) && string.IsNullOrWhiteSpace(AccessibilityMetadata.StripMnemonic(control.Text)))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Error, path, "ACC001", "Interactive control has no visible text or AccessibleName."));
        }
        if (interactive && control.Visible && control.Enabled && (control.Width < minimumTarget || control.Height < minimumTarget))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Warning, path, "ACC002", $"Interactive target is {control.Width}×{control.Height}; expected at least {minimumTarget}×{minimumTarget} logical pixels."));
        }
        if (control.TabStop && (!control.Visible || !control.Enabled))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Warning, path, "ACC003", "Hidden or disabled control remains in the tab sequence."));
        }
        if (control.Parent is not null && control.Visible && control.Dock == DockStyle.None && !control.Parent.ClientRectangle.IntersectsWith(control.Bounds))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Error, path, "LAY001", "Control is outside its parent's client area."));
        }

        Dictionary<int, int> tabCounts = control.Controls.Cast<Control>().Where(item => item.TabStop && item.Visible).GroupBy(item => item.TabIndex).ToDictionary(group => group.Key, group => group.Count());
        foreach ((int tabIndex, int count) in tabCounts.Where(pair => pair.Value > 1))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Warning, path, "ACC004", $"{count} sibling controls share TabIndex {tabIndex}."));
        }

        Dictionary<char, List<Control>> mnemonicGroups = control.Controls.Cast<Control>()
            .Where(item => item.Visible)
            .Select(item => (Control: item, Mnemonic: FindMnemonic(item.Text)))
            .Where(item => item.Mnemonic.HasValue)
            .GroupBy(item => item.Mnemonic!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Control).ToList());
        foreach ((char mnemonic, List<Control> controls) in mnemonicGroups.Where(pair => pair.Value.Count > 1))
        {
            findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Warning, path, "ACC005", $"Mnemonic Alt+{char.ToUpperInvariant(mnemonic)} is shared by {controls.Count} sibling controls."));
        }

        if (control.Visible && !control.AutoSize && !string.IsNullOrWhiteSpace(control.Text) && control is Label or ButtonBase)
        {
            Size preferred = control.GetPreferredSize(Size.Empty);
            if (preferred.Width > control.ClientSize.Width + 2 || preferred.Height > control.ClientSize.Height + 2)
            {
                findings.Add(new AccessibilityAuditFinding(AccessibilityAuditSeverity.Warning, path, "LAY002", $"Text may be clipped: preferred {preferred.Width}×{preferred.Height}, actual {control.ClientSize.Width}×{control.ClientSize.Height}."));
            }
        }

        foreach (Control child in control.Controls)
        {
            string childName = child.Name.Length > 0 ? child.Name : child.GetType().Name;
            Visit(child, path + "/" + childName, findings, visited, minimumTarget);
        }
    }

    private static char? FindMnemonic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (int index = 0; index < text.Length - 1; index++)
        {
            if (text[index] != '&') continue;
            if (text[index + 1] == '&') { index++; continue; }
            return char.ToLowerInvariant(text[index + 1]);
        }
        return null;
    }
}
