using System.Drawing.Drawing2D;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

public class DatabaseIdPicker : UserControl
{
    private readonly NumericUpDown _value = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 999999, ThousandsSeparator = true };
    private readonly Button _browse = new() { Text = "…", Dock = DockStyle.Right, Width = 32 };
    private readonly Label _name = new() { Dock = DockStyle.Bottom, Height = 18, AutoEllipsis = true };
    private readonly MvProjectSession _session;
    private bool _suppress;

    public DatabaseIdPicker(MvProjectSession session, MvDocumentKind targetKind)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        TargetKind = targetKind;
        Height = 42;
        Controls.Add(_value);
        Controls.Add(_browse);
        Controls.Add(_name);
        _value.ValueChanged += async (_, _) =>
        {
            if (_suppress) return;
            await UpdateNameAsync().ConfigureAwait(true);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _browse.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);
    }

    public event EventHandler? ValueChanged;
    public MvDocumentKind TargetKind { get; }
    public bool AllowNone { get; set; } = true;

    public int SelectedId
    {
        get => decimal.ToInt32(_value.Value);
        set
        {
            _suppress = true;
            _value.Minimum = AllowNone ? 0 : 1;
            _value.Value = Math.Clamp(value, decimal.ToInt32(_value.Minimum), decimal.ToInt32(_value.Maximum));
            _suppress = false;
            _ = UpdateNameAsync();
        }
    }

    private async ValueTask UpdateNameAsync()
    {
        int id = SelectedId;
        if (id == 0 && AllowNone) { _name.Text = "None"; return; }
        MvDatabaseFileSummary? file = _session.Workspace.Snapshot.Database.Find(TargetKind);
        if (file is null) { _name.Text = $"Missing {TargetKind} database"; return; }
        await using IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(file.RelativePath).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document) { _name.Text = $"{TargetKind} {id}"; return; }
        JsonSyntaxNode? node = document.Syntax.Resolve(JsonPath.Root.Append(id));
        if (node is null || node.Kind == JsonSyntaxKind.Null) { _name.Text = $"Missing record {id}"; return; }
        _name.Text = document.GetString(JsonPath.Root.Append(id).Append("name")) ?? $"{TargetKind} {id}";
    }

    private async ValueTask BrowseAsync()
    {
        MvDatabaseFileSummary? file = _session.Workspace.Snapshot.Database.Find(TargetKind);
        if (file is null) return;
        await using IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(file.RelativePath).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document) return;
        DatabaseRecordIndex index = new(document);
        using Form dialog = new() { Text = $"Select {TargetKind}", Width = 500, Height = 600, StartPosition = FormStartPosition.CenterParent };
        RecordListControl list = new() { Dock = DockStyle.Fill };
        list.SetSource(index);
        Button accept = new() { Text = "Select", Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.OK };
        dialog.Controls.Add(list);
        dialog.Controls.Add(accept);
        dialog.AcceptButton = accept;
        list.RecordActivated += (_, _) => dialog.DialogResult = DialogResult.OK;
        list.SelectRecord(SelectedId);
        if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedRecordIds.Count == 1) SelectedId = list.SelectedRecordIds[0];
    }
}

public sealed class AssetPicker : UserControl
{
    private readonly ComboBox _assets = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _clear = new() { Text = "×", Dock = DockStyle.Right, Width = 30 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 18, AutoEllipsis = true };
    private readonly MvProjectSession _session;
    private bool _suppress;

    public AssetPicker(MvProjectSession session, AssetCategory category)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Category = category;
        Height = 42;
        Controls.Add(_assets);
        Controls.Add(_clear);
        Controls.Add(_status);
        Reload();
        _session.Assets.Changed += AssetsChanged;
        _assets.TextChanged += (_, _) => { if (!_suppress) ValueChanged?.Invoke(this, EventArgs.Empty); UpdateStatus(); };
        _clear.Click += (_, _) => Value = string.Empty;
    }

    public event EventHandler? ValueChanged;
    public AssetCategory Category { get; }

    public string Value
    {
        get => _assets.Text;
        set
        {
            _suppress = true;
            _assets.Text = value ?? string.Empty;
            _suppress = false;
            UpdateStatus();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _session.Assets.Changed -= AssetsChanged;
        base.Dispose(disposing);
    }

    private void AssetsChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(Reload)); return; }
        Reload();
    }

    private void Reload()
    {
        string selected = _assets.Text;
        _suppress = true;
        _assets.Items.Clear();
        _assets.Items.Add(string.Empty);
        foreach (AssetRecord asset in _session.Assets.Assets.Where(asset => asset.Key.Category == Category)) _assets.Items.Add(asset.Key.LogicalName);
        _assets.Text = selected;
        _suppress = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (string.IsNullOrWhiteSpace(Value)) { _status.Text = "None"; return; }
        AssetRecord? record = _session.Assets.Find(new AssetKey(Category, Value));
        _status.Text = record is null ? "Missing asset" : record.Status.ToString();
    }
}

public sealed class IconPicker : UserControl
{
    private readonly IconPreviewSurface _preview = new() { Dock = DockStyle.Left, Width = 48, Margin = new Padding(0, 0, 6, 0), AccessibleName = "Selected icon preview" };
    private readonly NumericUpDown _value = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 99999, AccessibleName = "Icon index" };
    private readonly Button _browse = new() { Text = "Select…", Dock = DockStyle.Right, Width = 78, AccessibleName = "Select icon" };
    private readonly Button _clear = new() { Text = "×", Dock = DockStyle.Right, Width = 30, AccessibleName = "Reset icon to zero" };
    private readonly Label _caption = new() { Text = "Icon index", Dock = DockStyle.Bottom, Height = 18, AutoEllipsis = true };
    private CancellationTokenSource? _loadCancellation;
    private MvProjectSession? _session;
    private Bitmap? _iconSet;
    private GridSheetDescriptor? _descriptor;
    private string _status = "IconSet not loaded";

    public IconPicker(MvProjectSession? session = null)
    {
        Height = 50;
        AccessibleName = "Icon picker";
        Panel editor = new() { Dock = DockStyle.Fill };
        editor.Controls.Add(_value);
        editor.Controls.Add(_browse);
        editor.Controls.Add(_clear);
        Controls.Add(editor);
        Controls.Add(_preview);
        Controls.Add(_caption);

        _value.ValueChanged += (_, _) =>
        {
            UpdateDisplay();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _browse.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);
        _clear.Click += (_, _) => Value = 0;
        if (session is not null) AttachSession(session);
        UpdateDisplay();
    }

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => decimal.ToInt32(_value.Value);
        set
        {
            _value.Value = Math.Clamp(value, 0, decimal.ToInt32(_value.Maximum));
            UpdateDisplay();
        }
    }

    public void AttachSession(MvProjectSession session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null) _session.Assets.Changed -= OnAssetsChanged;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.Assets.Changed += OnAssetsChanged;
        _ = LoadIconSetAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session is not null) _session.Assets.Changed -= OnAssetsChanged;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _iconSet?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnAssetsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => _ = LoadIconSetAsync()));
            return;
        }

        _ = LoadIconSetAsync();
    }

    private async ValueTask LoadIconSetAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;

        if (_session is null)
        {
            _status = "IconSet unavailable";
            _descriptor = null;
            _iconSet?.Dispose();
            _iconSet = null;
            UpdateDisplay();
            return;
        }

        AssetRecord? record = _session.Assets.Find(new AssetKey(AssetCategory.System, "IconSet"));
        AssetVariant? variant = record?.PreferredVariant;
        if (record is null || variant is null)
        {
            _status = "Missing IconSet";
            _descriptor = null;
            _iconSet?.Dispose();
            _iconSet = null;
            UpdateDisplay();
            return;
        }

        if (variant.IsEncrypted)
        {
            _status = "Encrypted IconSet";
            _descriptor = null;
            _iconSet?.Dispose();
            _iconSet = null;
            UpdateDisplay();
            return;
        }

        try
        {
            Bitmap bitmap = await AssetImageLoader.LoadOwnedBitmapAsync(
                _session.AssetContent,
                variant.RelativePath,
                cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || IsDisposed)
            {
                bitmap.Dispose();
                return;
            }

            GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeIconSet(bitmap.Width, bitmap.Height);
            if (!descriptor.IsValid)
            {
                bitmap.Dispose();
                _status = descriptor.ValidationMessage ?? "Invalid IconSet";
                _descriptor = null;
                _iconSet?.Dispose();
                _iconSet = null;
                UpdateDisplay();
                return;
            }

            _iconSet?.Dispose();
            _iconSet = bitmap;
            _descriptor = descriptor;
            _status = string.Empty;
            UpdateDisplay();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            _status = exception.Message;
            _descriptor = null;
            _iconSet?.Dispose();
            _iconSet = null;
            UpdateDisplay();
        }
    }

    private async ValueTask BrowseAsync()
    {
        if (_iconSet is null || _descriptor is null)
        {
            await LoadIconSetAsync().ConfigureAwait(true);
        }

        if (_iconSet is null || _descriptor is null)
        {
            MessageBox.Show(this, "The IconSet image is not available for browsing.", "Select Icon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using IconSetPickerDialog dialog = new((Bitmap)_iconSet.Clone(), _descriptor, Value);
        if (dialog.ShowDialog(this) == DialogResult.OK) Value = dialog.SelectedIconIndex;
    }

    private void UpdateDisplay()
    {
        _preview.SetIcon(_iconSet, _descriptor, Value, _status);
        if (_descriptor is null)
        {
            _caption.Text = string.IsNullOrWhiteSpace(_status) ? $"Icon {Value}" : _status;
            return;
        }

        int count = _descriptor.Cells.Count;
        _caption.Text = Value >= 0 && Value < count
            ? $"Icon {Value} of {count - 1}"
            : $"Icon {Value} (out of range)";
    }
}

internal sealed class IconSetPickerDialog : Form
{
    private readonly IconSheetGridControl _grid = new() { AccessibleName = "Icon sheet grid" };
    private readonly IconPreviewSurface _preview = new() { Dock = DockStyle.Top, Height = 120, AccessibleName = "Selected icon preview" };
    private readonly Label _selection = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 6, 0) };
    private readonly NumericUpDown _index = new() { Dock = DockStyle.Top, Minimum = 0, Maximum = 99999 };
    private readonly Bitmap _iconSet;
    private readonly GridSheetDescriptor _descriptor;
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };

    public IconSetPickerDialog(Bitmap iconSet, GridSheetDescriptor descriptor, int selectedIndex)
    {
        _iconSet = iconSet ?? throw new ArgumentNullException(nameof(iconSet));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Text = "Select Icon";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 520);
        ClientSize = new Size(880, 620);
        Padding = new Padding(8);

        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 580, 340, 420);
        Panel scrollHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
        _grid.SetSheet(_iconSet, _descriptor);
        scrollHost.Controls.Add(_grid);
        split.Panel1.Controls.Add(scrollHost);

        Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _index.Value = Math.Clamp(selectedIndex, 0, Math.Max(0, _descriptor.Cells.Count - 1));
        _index.ValueChanged += (_, _) => _grid.SelectedIndex = decimal.ToInt32(_index.Value);
        right.Controls.Add(new Label { Text = "Index", Dock = DockStyle.Top, Height = 18 });
        right.Controls.Add(_index);
        right.Controls.Add(_selection);
        right.Controls.Add(_preview);
        right.Controls.Add(new Label
        {
            Text = "Click an icon to select it. Double-click an icon to accept immediately.",
            Dock = DockStyle.Top,
            Height = 42,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 8, 0, 0)
        });
        split.Panel2.Controls.Add(right);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([_ok, cancel]);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = cancel;

        _grid.SelectedIndexChanged += (_, _) => UpdateSelection(_grid.SelectedIndex);
        _grid.IconActivated += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        UpdateSelection(decimal.ToInt32(_index.Value));
    }

    public int SelectedIconIndex { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconSet.Dispose();
        base.Dispose(disposing);
    }

    private void UpdateSelection(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, _descriptor.Cells.Count - 1));
        SelectedIconIndex = index;
        if (decimal.ToInt32(_index.Value) != index) _index.Value = index;
        _selection.Text = $"Icon {index}";
        _preview.SetIcon(_iconSet, _descriptor, index, string.Empty);
    }
}

internal sealed class IconSheetGridControl : Control
{
    private Bitmap? _sheet;
    private GridSheetDescriptor? _descriptor;
    private int _selectedIndex;

    public IconSheetGridControl()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? IconActivated;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _descriptor is null ? 0 : Math.Clamp(value, 0, Math.Max(0, _descriptor.Cells.Count - 1));
            if (_selectedIndex == clamped) return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSheet(Bitmap sheet, GridSheetDescriptor descriptor)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Size = sheet.Size;
        MinimumSize = sheet.Size;
        MaximumSize = sheet.Size;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_descriptor is null) return;
        int column = e.X / _descriptor.CellWidth;
        int row = e.Y / _descriptor.CellHeight;
        if (column < 0 || row < 0 || column >= _descriptor.Columns || row >= _descriptor.Rows) return;
        SelectedIndex = row * _descriptor.Columns + column;
        if (e.Clicks >= 2) IconActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_sheet is null || _descriptor is null)
        {
            e.Graphics.Clear(SystemColors.Window);
            return;
        }

        using HatchBrush brush = new(HatchStyle.LargeCheckerBoard, SystemColors.ControlLight, SystemColors.Window);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_sheet, 0, 0, _sheet.Width, _sheet.Height);
        using Pen gridPen = new(Color.FromArgb(72, Color.Black));
        for (int x = 0; x <= _sheet.Width; x += _descriptor.CellWidth) e.Graphics.DrawLine(gridPen, x, 0, x, _sheet.Height);
        for (int y = 0; y <= _sheet.Height; y += _descriptor.CellHeight) e.Graphics.DrawLine(gridPen, 0, y, _sheet.Width, y);

        if (_selectedIndex >= 0 && _selectedIndex < _descriptor.Cells.Count)
        {
            SheetCell cell = _descriptor.Cells[_selectedIndex];
            Rectangle rect = new(cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
            using Pen outer = new(Color.Gold, 3f);
            using Pen inner = new(Color.Black, 1f);
            e.Graphics.DrawRectangle(outer, rect);
            rect.Inflate(-2, -2);
            e.Graphics.DrawRectangle(inner, rect);
        }
    }
}

internal sealed class IconPreviewSurface : Control
{
    private Bitmap? _sheet;
    private GridSheetDescriptor? _descriptor;
    private int _index;
    private string _status = string.Empty;

    public IconPreviewSurface()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.Window;
    }

    public void SetIcon(Bitmap? sheet, GridSheetDescriptor? descriptor, int index, string status)
    {
        _sheet = sheet;
        _descriptor = descriptor;
        _index = index;
        _status = status ?? string.Empty;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using HatchBrush brush = new(HatchStyle.LargeCheckerBoard, SystemColors.ControlLight, SystemColors.Window);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

        if (_sheet is not null && _descriptor is not null && _index >= 0 && _index < _descriptor.Cells.Count)
        {
            SheetCell cell = _descriptor.Cells[_index];
            Rectangle source = new(cell.X, cell.Y, cell.Width, cell.Height);
            Rectangle available = Rectangle.Inflate(ClientRectangle, -5, -5);
            float scale = Math.Min(available.Width / (float)Math.Max(1, source.Width), available.Height / (float)Math.Max(1, source.Height));
            if (scale > 0f)
            {
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                Rectangle destination = new(
                    ClientRectangle.X + (ClientRectangle.Width - width) / 2,
                    ClientRectangle.Y + (ClientRectangle.Height - height) / 2,
                    width,
                    height);
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.DrawImage(_sheet, destination, source, GraphicsUnit.Pixel);
                return;
            }
        }

        string message = string.IsNullOrWhiteSpace(_status) ? "No icon" : _status;
        TextRenderer.DrawText(e.Graphics, message, Font, ClientRectangle, SystemColors.ControlText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}

public sealed class AudioPicker : UserControl
{
    private readonly AssetPicker _asset;
    private readonly NumericUpDown _volume = new() { Minimum = 0, Maximum = 100, Value = 90, Width = 65 };
    private readonly NumericUpDown _pitch = new() { Minimum = 50, Maximum = 150, Value = 100, Width = 65 };
    private readonly NumericUpDown _pan = new() { Minimum = -100, Maximum = 100, Width = 65 };

    public AudioPicker(MvProjectSession session, AssetCategory category)
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        _asset = new AssetPicker(session, category) { Dock = DockStyle.Top };
        FlowLayoutPanel values = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        values.Controls.AddRange([new Label { Text = "Vol", AutoSize = true }, _volume, new Label { Text = "Pitch", AutoSize = true }, _pitch, new Label { Text = "Pan", AutoSize = true }, _pan]);
        Controls.Add(values);
        Controls.Add(_asset);
        _asset.ValueChanged += Changed;
        _volume.ValueChanged += Changed;
        _pitch.ValueChanged += Changed;
        _pan.ValueChanged += Changed;
    }

    public event EventHandler? ValueChanged;
    public string AssetName { get => _asset.Value; set => _asset.Value = value; }
    public int Volume { get => decimal.ToInt32(_volume.Value); set => _volume.Value = Math.Clamp(value, 0, 100); }
    public int Pitch { get => decimal.ToInt32(_pitch.Value); set => _pitch.Value = Math.Clamp(value, 50, 150); }
    public int Pan { get => decimal.ToInt32(_pan.Value); set => _pan.Value = Math.Clamp(value, -100, 100); }
    private void Changed(object? sender, EventArgs e) => ValueChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class NumberRangeEditor : UserControl
{
    private readonly NumericUpDown _minimum = new() { Width = 90, Minimum = decimal.MinValue, Maximum = decimal.MaxValue };
    private readonly NumericUpDown _maximum = new() { Width = 90, Minimum = decimal.MinValue, Maximum = decimal.MaxValue };
    public NumberRangeEditor()
    {
        AutoSize = true;
        FlowLayoutPanel flow = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        flow.Controls.AddRange([new Label { Text = "Min", AutoSize = true }, _minimum, new Label { Text = "Max", AutoSize = true }, _maximum]);
        Controls.Add(flow);
        _minimum.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _maximum.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? ValueChanged;
    public (decimal Minimum, decimal Maximum) Value { get => (_minimum.Value, _maximum.Value); set { _minimum.Value = value.Minimum; _maximum.Value = value.Maximum; } }
}
