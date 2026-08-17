using Playroma.JRPG.Eventing;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed class EventCommandActivatedEventArgs : EventArgs
{
    public EventCommandActivatedEventArgs(ProjectedEventRow row) => Row = row;
    public ProjectedEventRow Row { get; }
}


public sealed class EventCommandInsertRequestedEventArgs : EventArgs
{
    public EventCommandInsertRequestedEventArgs(EventCommandInsertionMode mode) => Mode = mode;
    public EventCommandInsertionMode Mode { get; }
}

public sealed class EventCommandMoveRequestedEventArgs : EventArgs
{
    public EventCommandMoveRequestedEventArgs(int direction) => Direction = Math.Sign(direction);
    public int Direction { get; }
}

/// <summary>Virtualized RPG Maker-style command contents list with a final insertion row.</summary>
public sealed class VirtualEventCommandListControl : UserControl
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        VirtualMode = true,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable,
        StandardTab = true,
        AccessibleName = "Event commands",
        AccessibleDescription = "Event command contents list. Insert adds a command, Enter edits, Delete removes, and the context-menu key opens command actions.",
        AccessibleRole = AccessibleRole.List
    };
    private readonly ContextMenuStrip _menu = new();
    private IReadOnlyList<ProjectedEventRow> _rows = [];
    private EventCommandRegistry? _registry;
    private EventCommandFormatter? _formatter;

    public VirtualEventCommandListControl()
    {
        Dock = DockStyle.Fill;
        AccessibilityMetadata.Name(this, "Event command contents", "A structured list of event commands and branch indentation.", AccessibleRole.Pane);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Index", HeaderText = "#", Width = 54, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "Contents", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Code", Width = 62, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellDoubleClick += OnCellDoubleClick;
        _grid.SelectionChanged += (_, _) =>
        {
            UpdateAccessibleSelection();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _grid.KeyDown += OnKeyDown;
        _grid.MouseDown += OnMouseDown;
        BuildContextMenu();
        _grid.ContextMenuStrip = _menu;
        Controls.Add(_grid);
    }

    public event EventHandler<EventCommandActivatedEventArgs>? CommandActivated;
    public event EventHandler<EventCommandInsertRequestedEventArgs>? InsertRequested;
    public event EventHandler? EditRequested;
    public new event EventHandler? SelectionChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? CutRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler<EventCommandMoveRequestedEventArgs>? MoveRequested;

    public ProjectedEventRow? SelectedRow => _grid.CurrentCell?.RowIndex is int index && index >= 0 && index < _rows.Count
        ? _rows[index]
        : null;

    public bool IsInsertionRowSelected => _grid.CurrentCell?.RowIndex == _rows.Count;

    public int InsertionRawIndex => SelectedRow?.StructuralSpan.StartIndex
        ?? (_rows.Count == 0 ? 0 : Math.Max(0, _rows.Max(row => row.RawIndex)));

    public IReadOnlyList<ProjectedEventRow> SelectedRows => _grid.SelectedRows.Cast<DataGridViewRow>()
        .Select(row => row.Index)
        .Where(index => index >= 0 && index < _rows.Count)
        .Order()
        .Select(index => _rows[index])
        .ToArray();

    public void SetRows(
        IReadOnlyList<ProjectedEventRow> rows,
        EventCommandRegistry registry,
        EventCommandFormatter formatter,
        int? selectRawIndex = null,
        bool selectInsertionRow = false)
    {
        _rows = rows ?? [];
        _registry = registry;
        _formatter = formatter;
        _grid.RowCount = _rows.Count + 1;
        _grid.Invalidate();
        int index = selectInsertionRow
            ? _rows.Count
            : selectRawIndex.HasValue ? FindRow(selectRawIndex.Value) : (_rows.Count > 0 ? 0 : _rows.Count);
        index = Math.Clamp(index, 0, _rows.Count);
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[index].Cells[1];
        _grid.Rows[index].Selected = true;
        try { _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 3); } catch (InvalidOperationException) { }
    }

    public void SelectRawIndex(int rawIndex)
    {
        int index = FindRow(rawIndex);
        if (index < 0) return;
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[index].Cells[1];
        _grid.Rows[index].Selected = true;
    }

    public void SelectInsertionRow()
    {
        if (_grid.RowCount == 0) return;
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[_rows.Count].Cells[1];
        _grid.Rows[_rows.Count].Selected = true;
    }

    private void BuildContextMenu()
    {
        ToolStripMenuItem insertBefore = AddMenu("Insert Before…", (_, _) => RaiseInsert(EventCommandInsertionMode.BeforeSelected), Keys.Insert);
        ToolStripMenuItem insertAfter = AddMenu("Insert After Block…", (_, _) => RaiseInsert(EventCommandInsertionMode.AfterSelectedBlock), Keys.Control | Keys.Insert);
        ToolStripMenuItem insertInside = AddMenu("Insert Inside Branch…", (_, _) => RaiseInsert(EventCommandInsertionMode.InsideSelectedBranch), Keys.Control | Keys.Shift | Keys.Insert);
        ToolStripMenuItem edit = AddMenu("Edit…", (_, _) => EditRequested?.Invoke(this, EventArgs.Empty), Keys.None, "Enter");
        _menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem cut = AddMenu("Cut", (_, _) => CutRequested?.Invoke(this, EventArgs.Empty), Keys.Control | Keys.X);
        ToolStripMenuItem copy = AddMenu("Copy", (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty), Keys.Control | Keys.C);
        AddMenu("Paste", (_, _) => PasteRequested?.Invoke(this, EventArgs.Empty), Keys.Control | Keys.V);
        ToolStripMenuItem duplicate = AddMenu("Duplicate", (_, _) => DuplicateRequested?.Invoke(this, EventArgs.Empty), Keys.Control | Keys.D);
        ToolStripMenuItem delete = AddMenu("Delete", (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty), Keys.Delete);
        _menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem moveUp = AddMenu("Move Up", (_, _) => MoveRequested?.Invoke(this, new EventCommandMoveRequestedEventArgs(-1)), Keys.Alt | Keys.Up);
        ToolStripMenuItem moveDown = AddMenu("Move Down", (_, _) => MoveRequested?.Invoke(this, new EventCommandMoveRequestedEventArgs(1)), Keys.Alt | Keys.Down);
        _menu.Opening += (_, _) =>
        {
            bool command = SelectedRow is not null;
            bool selectedBlock = command && SelectedRow?.IsBlockOpening == true;
            bool selectedBranch = command
                && SelectedRow?.IsContinuation == true
                && !string.IsNullOrWhiteSpace(SelectedRow.BranchLabel);
            insertBefore.Enabled = true;
            insertAfter.Enabled = command;
            insertInside.Enabled = selectedBlock || selectedBranch;
            edit.Enabled = command;
            cut.Enabled = command;
            copy.Enabled = command;
            duplicate.Enabled = command;
            delete.Enabled = command;
            moveUp.Enabled = command;
            moveDown.Enabled = command;
        };
    }

    private void RaiseInsert(EventCommandInsertionMode mode) =>
        InsertRequested?.Invoke(this, new EventCommandInsertRequestedEventArgs(mode));

    private ToolStripMenuItem AddMenu(
        string text,
        EventHandler handler,
        Keys shortcut,
        string? shortcutDisplay = null)
    {
        ToolStripMenuItem item = new(text)
        {
            ShowShortcutKeys = shortcut != Keys.None || !string.IsNullOrWhiteSpace(shortcutDisplay)
        };
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        if (!string.IsNullOrWhiteSpace(shortcutDisplay)) item.ShortcutKeyDisplayString = shortcutDisplay;
        item.Click += handler;
        _menu.Items.Add(item);
        return item;
    }

    private int FindRow(int rawIndex)
    {
        for (int index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].RawIndex == rawIndex) return index;
        }
        return -1;
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex == _rows.Count)
        {
            if (e.ColumnIndex == 1) e.Value = "◆ Insert event command here…";
            return;
        }
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        ProjectedEventRow row = _rows[e.RowIndex];
        if (e.ColumnIndex == 0)
        {
            e.Value = row.RawIndex.ToString("D4");
            return;
        }
        if (e.ColumnIndex == 2)
        {
            e.Value = row.Command.Code;
            return;
        }
        if (_registry is null || _formatter is null)
        {
            e.Value = row.Command.RawJson;
            return;
        }
        EventCommandSummary summary = _formatter.Format(row.Command);
        bool unsupported = string.Equals(_registry.Get(row.Command.Code).Category, "Unknown", StringComparison.OrdinalIgnoreCase);
        string unsupportedPrefix = unsupported ? "[Unsupported command] " : string.Empty;
        string marker = row.IsBlockClosing ? "◇ " : row.IsBlockOpening ? "◆ " : row.IsContinuation ? "  · " : "◆ ";
        string branch = string.IsNullOrWhiteSpace(row.BranchLabel) ? string.Empty : $"[{row.BranchLabel}] ";
        string detail = string.IsNullOrWhiteSpace(summary.SecondaryText) ? string.Empty : $": {summary.SecondaryText}";
        e.Value = new string(' ', Math.Max(0, row.VisualDepth) * 4) + marker + unsupportedPrefix + branch + summary.PrimaryText + detail;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex == _rows.Count)
        {
            e.CellStyle.ForeColor = SystemColors.GrayText;
            e.CellStyle.Font = new Font(Font, FontStyle.Italic);
            return;
        }
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        ProjectedEventRow row = _rows[e.RowIndex];
        if (_registry?.Get(row.Command.Code).Category == "Unknown") e.CellStyle.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.DarkOrange;
        if (row.IsBlockClosing || row.IsContinuation) e.CellStyle.ForeColor = SystemColors.GrayText;
        e.CellStyle.Font = new Font(Font, row.IsBlockOpening ? FontStyle.Bold : FontStyle.Regular);
    }

    private void OnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex == _rows.Count)
        {
            RaiseInsert(EventCommandInsertionMode.AtEnd);
            return;
        }
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        CommandActivated?.Invoke(this, new EventCommandActivatedEventArgs(_rows[e.RowIndex]));
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        DataGridView.HitTestInfo hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0 || hit.RowIndex >= _grid.RowCount) return;
        _grid.ClearSelection();
        _grid.CurrentCell = _grid.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
        _grid.Rows[hit.RowIndex].Selected = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.Insert)
        {
            RaiseInsert(EventCommandInsertionMode.InsideSelectedBranch);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Insert)
        {
            RaiseInsert(EventCommandInsertionMode.AfterSelectedBlock);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Insert)
        {
            RaiseInsert(EventCommandInsertionMode.BeforeSelected);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.X)
        {
            CutRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.V)
        {
            PasteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.D)
        {
            DuplicateRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Up)
        {
            MoveRequested?.Invoke(this, new EventCommandMoveRequestedEventArgs(-1));
            e.Handled = true;
        }
        else if (e.Alt && e.KeyCode == Keys.Down)
        {
            MoveRequested?.Invoke(this, new EventCommandMoveRequestedEventArgs(1));
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (SelectedRow is { } row) CommandActivated?.Invoke(this, new EventCommandActivatedEventArgs(row));
            else RaiseInsert(EventCommandInsertionMode.AtEnd);
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void UpdateAccessibleSelection()
    {
        if (IsInsertionRowSelected)
        {
            _grid.AccessibleDescription = "Insertion row selected. Press Enter to choose a new event command.";
        }
        else if (SelectedRow is { } row)
        {
            string structural = row.IsBlockOpening ? "branch opening" : row.IsBlockClosing ? "branch ending" : row.IsContinuation ? "branch continuation" : "command";
            string name = _registry is null || _formatter is null ? $"code {row.Command.Code}" : _formatter.Format(row.Command).PrimaryText;
            _grid.AccessibleDescription = $"Selected {structural}, {name}, indentation level {row.VisualDepth}.";
        }
        AccessibilityNotifyClients(AccessibleEvents.Focus, -1);
    }
}
