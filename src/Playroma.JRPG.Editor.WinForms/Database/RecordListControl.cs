using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Editor.WinForms.Accessibility;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class DatabaseRecordSelectionChangedEventArgs : EventArgs
{
    public DatabaseRecordSelectionChangedEventArgs(IReadOnlyList<int> recordIds) => RecordIds = recordIds;
    public IReadOnlyList<int> RecordIds { get; }
}

public sealed class RecordListControl : UserControl
{
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search records or use id:, modified:, missing:, error:…", AccessibleName = "Search database records" };
    private readonly ComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 128, AccessibleName = "Sort database records" };
    private readonly CheckBox _showEmpty = new() { Text = "Show &empty", AutoSize = true, Checked = false, AccessibleDescription = "Include empty database record slots." };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = true,
        VirtualMode = true,
        AccessibleName = "Database records",
        AccessibleDescription = "Numbered database records. Use arrow keys to select a record and Enter to edit it.",
        AccessibleRole = AccessibleRole.List
    };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(6, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
    private DatabaseRecordIndex? _source;
    private IReadOnlyList<DatabaseRecordIndexEntry> _visible = [];

    public RecordListControl()
    {
        Dock = DockStyle.Fill;
        _list.Columns.Add("ID", 68);
        _list.Columns.Add("Name", 230);
        _list.Columns.Add("Status", 95);
        _list.RetrieveVirtualItem += RetrieveVirtualItem;
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke(this, new DatabaseRecordSelectionChangedEventArgs(SelectedRecordIds));
        _list.DoubleClick += (_, _) => ActivateSelectedRecord();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ActivateSelectedRecord();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _search.TextChanged += (_, _) => RefreshFilter();
        _showEmpty.CheckedChanged += (_, _) => RefreshFilter();
        _sort.Items.AddRange(["ID", "Name", "Recently edited", "Severity"]);
        _sort.SelectedIndex = 0;
        _sort.SelectedIndexChanged += (_, _) => RefreshFilter();
        TableLayoutPanel searchRow = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4, 4, 4, 2)
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(AccessibilityMetadata.CreateLabel("&Search:", _search), 0, 0);
        searchRow.Controls.Add(_search, 1, 0);

        FlowLayoutPanel options = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4, 1, 4, 4)
        };
        options.Controls.Add(AccessibilityMetadata.CreateLabel("S&ort:", _sort));
        options.Controls.Add(_sort);
        options.Controls.Add(_showEmpty);
        Controls.Add(_list);
        Controls.Add(options);
        Controls.Add(searchRow);
        Controls.Add(_summary);
        AccessibilityMetadata.Name(this, "Database record browser", "Search, sort, and select numbered database records.", AccessibleRole.Pane);
        AccessibilityMetadata.SetSequentialTabOrder(this);
    }

    public event EventHandler<DatabaseRecordSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<int>? RecordActivated;
    public bool MultiSelect
    {
        get => _list.MultiSelect;
        set => _list.MultiSelect = value;
    }
    public IReadOnlyList<int> SelectedRecordIds => _list.SelectedIndices.Cast<int>().Where(index => index >= 0 && index < _visible.Count).Select(index => _visible[index].Id).ToArray();
    public int? SelectedRecordId => SelectedRecordIds.Count == 1 ? SelectedRecordIds[0] : null;
    public string SearchText
    {
        get => _search.Text;
        set => _search.Text = value ?? string.Empty;
    }

    public void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }

    public void FocusList() => _list.Focus();

    public void SetSource(DatabaseRecordIndex source)
    {
        if (_source is not null) _source.Changed -= SourceChanged;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _source.Changed += SourceChanged;
        RefreshFilter();
    }

    public void SelectRecord(int id)
    {
        int index = _visible.ToList().FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(index);
        _list.EnsureVisible(index);
    }

    public void SelectRecords(IEnumerable<int> ids)
    {
        HashSet<int> set = ids.ToHashSet();
        _list.SelectedIndices.Clear();
        for (int index = 0; index < _visible.Count; index++) if (set.Contains(_visible[index].Id)) _list.SelectedIndices.Add(index);
        if (_list.SelectedIndices.Count > 0) _list.EnsureVisible(_list.SelectedIndices[0]);
    }

    public void JumpToId(int id)
    {
        _search.Text = $"id:{id}";
        SelectRecord(id);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _source is not null) _source.Changed -= SourceChanged;
        base.Dispose(disposing);
    }

    private void ActivateSelectedRecord()
    {
        if (SelectedRecordIds.Count == 1) RecordActivated?.Invoke(this, SelectedRecordIds[0]);
    }

    private void SourceChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(RefreshFilter)); return; }
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        if (_source is null) { _visible = []; _list.VirtualListSize = 0; return; }
        DatabaseRecordFilter parsed = DatabaseRecordFilter.Parse(_search.Text) with
        {
            Populated = _showEmpty.Checked ? null : true,
            Sort = _sort.SelectedIndex switch
            {
                1 => DatabaseRecordSort.Name,
                2 => DatabaseRecordSort.LastEdited,
                3 => DatabaseRecordSort.Severity,
                _ => DatabaseRecordSort.Id
            },
            Descending = _sort.SelectedIndex == 2
        };
        _visible = _source.Query(parsed);
        _list.VirtualListSize = _visible.Count;
        _list.Invalidate();
        _summary.Text = $"{_visible.Count:N0} visible — {_source.Records.Count:N0} slots";
    }

    private void RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _visible.Count)
        {
            e.Item = new ListViewItem();
            return;
        }
        DatabaseRecordIndexEntry entry = _visible[e.ItemIndex];
        string status = entry.IsPopulated ? "" : "Empty";
        if (entry.HighestSeverity.HasValue) status = entry.HighestSeverity.Value.ToString();
        else if (entry.HasMissingReferences) status = "Missing ref";
        else if (entry.IsModified) status = "Modified";
        e.Item = new ListViewItem([entry.IdText, entry.Name, status]) { Tag = entry.Id };
    }
}
