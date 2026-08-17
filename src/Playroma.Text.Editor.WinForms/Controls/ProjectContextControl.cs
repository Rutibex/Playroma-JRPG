namespace Playroma.Text.Editor.WinForms.Controls;

/// <summary>Read-only view of the host RPG project's values exposed to generation recipes.</summary>
public sealed class ProjectContextControl : UserControl
{
    private readonly Label _summary = new()
    {
        Dock = DockStyle.Top,
        Height = 52,
        Padding = new Padding(8),
        AutoEllipsis = true
    };
    private readonly ListView _items = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = false,
        HideSelection = false
    };

    public ProjectContextControl()
    {
        _items.Columns.Add("Name", 150);
        _items.Columns.Add("Current project context", 320);
        Controls.Add(_items);
        Controls.Add(_summary);
    }

    public void Bind(TextStudioProjectContextSnapshot? context)
    {
        _items.BeginUpdate();
        try
        {
            _items.Items.Clear();
            _items.Groups.Clear();
            if (context is null)
            {
                _summary.Text = "No host project context is available.";
                return;
            }

            _summary.Text = $"{context.ProjectTitle}\r\nRecipes can read these values through context/project variables.";
            foreach (var item in context.EffectiveSummary)
            {
                var row = new ListViewItem(item.Name);
                row.SubItems.Add(item.Value);
                if (!string.IsNullOrWhiteSpace(item.Group)) row.Group = EnsureGroup(item.Group!);
                _items.Items.Add(row);
            }
        }
        finally
        {
            foreach (ColumnHeader column in _items.Columns) column.Width = -2;
            _items.EndUpdate();
        }
    }

    private ListViewGroup EnsureGroup(string name)
    {
        foreach (ListViewGroup group in _items.Groups)
            if (group.Header.Equals(name, StringComparison.OrdinalIgnoreCase)) return group;
        var created = new ListViewGroup(name, HorizontalAlignment.Left);
        _items.Groups.Add(created);
        return created;
    }
}
