using System.Text;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public class StructuredArrayEditor : UserControl
{
    private readonly ListBox _items = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button _add = new() { Text = "Add", AutoSize = true };
    private readonly Button _edit = new() { Text = "Edit Raw", AutoSize = true };
    private readonly Button _duplicate = new() { Text = "Duplicate", AutoSize = true };
    private readonly Button _delete = new() { Text = "Delete", AutoSize = true };
    private readonly Button _up = new() { Text = "↑", Width = 32 };
    private readonly Button _down = new() { Text = "↓", Width = 32 };
    private MvProjectSession? _session;
    private MutableJsonProjectDocument? _document;
    private JsonPath _arrayPath;
    private string _defaultRawJson = "{}";
    private Func<string, string> _formatter = static raw => raw;
    private Func<string?, string?>? _entryEditor;

    public StructuredArrayEditor()
    {
        Dock = DockStyle.Fill;
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        toolbar.Controls.AddRange([_add, _edit, _duplicate, _delete, _up, _down]);
        Controls.Add(_items);
        Controls.Add(toolbar);
        _add.Click += async (_, _) => await AddAsync().ConfigureAwait(true);
        _edit.Click += async (_, _) => await EditAsync().ConfigureAwait(true);
        _duplicate.Click += async (_, _) => await DuplicateAsync().ConfigureAwait(true);
        _delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        _up.Click += async (_, _) => await MoveAsync(-1).ConfigureAwait(true);
        _down.Click += async (_, _) => await MoveAsync(1).ConfigureAwait(true);
        _items.DoubleClick += async (_, _) => await EditAsync().ConfigureAwait(true);
    }

    public event EventHandler? Changed;

    public void Bind(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        JsonPath arrayPath,
        string defaultRawJson,
        Func<string, string>? formatter = null,
        Func<string?, string?>? entryEditor = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _arrayPath = arrayPath;
        _defaultRawJson = defaultRawJson;
        _formatter = formatter ?? (static raw => raw);
        _entryEditor = entryEditor;
        Reload();
    }

    public void ClearBinding()
    {
        _session = null;
        _document = null;
        _entryEditor = null;
        _items.Items.Clear();
    }

    public void Reload()
    {
        _items.BeginUpdate();
        _items.Items.Clear();
        if (_document?.Syntax.Resolve(_arrayPath) is JsonArraySyntax array)
        {
            for (int index = 0; index < array.Elements.Count; index++)
            {
                string raw = _document.GetRawJson(_arrayPath.Append(index)) ?? "null";
                string summary;
                try { summary = _formatter(raw); }
                catch { summary = "Invalid entry — " + raw; }
                _items.Items.Add(new Entry(index, raw, summary));
            }
        }
        _items.EndUpdate();
    }

    private async ValueTask AddAsync()
    {
        if (!TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document)) return;
        int index = (document.Syntax.Resolve(_arrayPath) as JsonArraySyntax)?.Elements.Count ?? 0;
        string? raw = _entryEditor is null ? _defaultRawJson : _entryEditor(null);
        if (raw is null) return;
        await ExecuteAsync(session, new InsertJsonArrayElementCommand(document.RelativePath, _arrayPath, index, raw, "Add structured entry")).ConfigureAwait(true);
    }

    private async ValueTask EditAsync()
    {
        if (!TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document) || _items.SelectedItem is not Entry entry) return;
        string? raw;
        if (_entryEditor is null)
        {
            using RawJsonEditDialog dialog = new(entry.RawJson);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            raw = dialog.RawJson;
        }
        else
        {
            raw = _entryEditor(entry.RawJson);
            if (raw is null) return;
        }
        await ExecuteAsync(session, new SetJsonValueCommand(document.RelativePath, _arrayPath.Append(entry.Index), raw, "Edit structured entry")).ConfigureAwait(true);
    }

    private async ValueTask DuplicateAsync()
    {
        if (!TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document) || _items.SelectedItem is not Entry entry) return;
        await ExecuteAsync(session, new InsertJsonArrayElementCommand(document.RelativePath, _arrayPath, entry.Index + 1, entry.RawJson, "Duplicate structured entry")).ConfigureAwait(true);
    }

    private async ValueTask DeleteAsync()
    {
        if (!TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document) || _items.SelectedItem is not Entry entry) return;
        await ExecuteAsync(session, new RemoveJsonArrayElementCommand(document.RelativePath, _arrayPath, entry.Index, "Delete structured entry")).ConfigureAwait(true);
    }

    private async ValueTask MoveAsync(int offset)
    {
        if (!TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document) || _items.SelectedItem is not Entry entry) return;
        if (document.Syntax.Resolve(_arrayPath) is not JsonArraySyntax array) return;
        int destination = entry.Index + offset;
        if (destination < 0 || destination >= array.Elements.Count) return;
        List<string> raw = Enumerable.Range(0, array.Elements.Count).Select(index => document.GetRawJson(_arrayPath.Append(index)) ?? "null").ToList();
        (raw[entry.Index], raw[destination]) = (raw[destination], raw[entry.Index]);
        string arrayJson = "[" + string.Join(',', raw) + "]";
        await ExecuteAsync(session, new SetJsonValueCommand(document.RelativePath, _arrayPath, arrayJson, "Reorder structured entries")).ConfigureAwait(true);
        if (destination < _items.Items.Count) _items.SelectedIndex = destination;
    }

    private async ValueTask ExecuteAsync(MvProjectSession session, IEditorCommand command)
    {
        await using ProjectTransaction transaction = session.BeginTransaction(command.Description);
        transaction.Add(command);
        await transaction.CommitAsync().ConfigureAwait(true);
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryGetBinding(out MvProjectSession session, out MutableJsonProjectDocument document)
    {
        session = _session!;
        document = _document!;
        return session is not null && document is not null;
    }

    private sealed record Entry(int Index, string RawJson, string Summary)
    {
        public override string ToString() => $"{Index + 1:D2}  {Summary}";
    }
}

public sealed class TraitListEditor : StructuredArrayEditor
{
    public void Bind(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        JsonPath arrayPath,
        Phase9BProjectCatalog? catalog = null)
    {
        catalog ??= Phase9BProjectCatalogLoader.LoadAsync(session).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        Phase9BProjectCatalog resolved = catalog;
        base.Bind(
            session,
            document,
            arrayPath,
            "{\"code\":21,\"dataId\":0,\"value\":1}",
            raw => TraitEditDialog.Format(raw, resolved),
            raw => TraitEditDialog.Edit(FindForm(), resolved, raw));
    }
}

public sealed class EffectListEditor : StructuredArrayEditor
{
    public void Bind(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        JsonPath arrayPath,
        Phase9BProjectCatalog? catalog = null)
    {
        catalog ??= Phase9BProjectCatalogLoader.LoadAsync(session).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        Phase9BProjectCatalog resolved = catalog;
        base.Bind(
            session,
            document,
            arrayPath,
            "{\"code\":11,\"dataId\":0,\"value1\":0,\"value2\":0}",
            raw => EffectEditDialog.Format(raw, resolved),
            raw => EffectEditDialog.Edit(FindForm(), resolved, raw));
    }
}

public sealed class RawJsonEditDialog : Form
{
    private readonly CodeTextEditorControl _editor = new() { Dock = DockStyle.Fill };
    private readonly Label _error = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.DarkRed };

    public RawJsonEditDialog(string rawJson)
    {
        Text = "Edit Raw JSON Entry";
        Width = 720;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        _editor.Text = rawJson;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button okay = new() { Text = "OK", DialogResult = DialogResult.None, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(okay);
        buttons.Controls.Add(cancel);
        Controls.Add(_editor);
        Controls.Add(_error);
        Controls.Add(buttons);
        CancelButton = cancel;
        okay.Click += (_, _) =>
        {
            try
            {
                JsonValueEncoder.ValidateRawJson(Encoding.UTF8.GetBytes(_editor.Text));
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                _error.Text = exception.Message;
            }
        };
    }

    public string RawJson => _editor.Text;
}

public sealed class ParameterCurveEditor : UserControl
{
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly CurveGraph _graph = new() { Dock = DockStyle.Bottom, Height = 150 };
    private int[] _values = new int[100];
    private bool _loading;

    public ParameterCurveEditor()
    {
        Dock = DockStyle.Fill;
        _grid.Columns.Add("Level", "Level");
        _grid.Columns.Add("Value", "Value");
        _grid.Columns[0].ReadOnly = true;
        for (int level = 1; level <= 99; level++) _grid.Rows.Add(level, 0);
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0 || e.RowIndex >= 99) return;
            if (int.TryParse(Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value, System.Globalization.CultureInfo.InvariantCulture), out int value)) _values[e.RowIndex + 1] = value;
            _graph.Values = _values;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_grid);
        Controls.Add(_graph);
    }

    public event EventHandler? ValueChanged;
    public int[] Values
    {
        get => _values.ToArray();
        set
        {
            _loading = true;
            try
            {
                _values = value?.Length >= 100 ? value.Take(100).ToArray() : new int[100];
                for (int row = 0; row < 99; row++) _grid.Rows[row].Cells[1].Value = _values[row + 1];
                _graph.Values = _values;
            }
            finally
            {
                _loading = false;
            }
        }
    }

    private sealed class CurveGraph : Control
    {
        private int[] _values = [];
        public int[] Values { get => _values; set { _values = value ?? []; Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(SystemColors.Window);
            if (_values.Length < 2) return;
            int max = Math.Max(1, _values.Max());
            PointF[] points = Enumerable.Range(1, Math.Min(99, _values.Length - 1)).Select(level => new PointF((level - 1) * (ClientSize.Width - 1f) / 98f, ClientSize.Height - 1f - _values[level] * (ClientSize.Height - 1f) / max)).ToArray();
            if (points.Length > 1)
            {
                using Pen pen = new(SystemColors.Highlight);
                e.Graphics.DrawLines(pen, points);
            }
        }
    }
}
