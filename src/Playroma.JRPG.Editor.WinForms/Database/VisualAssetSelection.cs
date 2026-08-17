using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.Editor.WinForms.Assets;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed record AssetIndexSelection(string AssetName, int Index);

/// <summary>Compact visual selector used by the Actor editor for faces and map characters.</summary>
public sealed class AssetIndexSelectionControl : UserControl
{
    private readonly MvProjectSession _session;
    private AssetCategory _category;
    private readonly bool _hasIndex;
    private readonly bool _hasHue;
    private int _hue;
    private readonly Label _value = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 18, AutoEllipsis = true };
    private readonly Button _select = new() { Text = "Select…", Dock = DockStyle.Right, Width = 78 };
    private readonly Button _edit = new() { Text = "Edit…", Dock = DockStyle.Right, Width = 62 };
    private readonly Button _clear = new() { Text = "×", Dock = DockStyle.Right, Width = 30 };
    private readonly Func<AssetCategory, AssetIndexSelection, Task>? _assetEditor;

    public AssetIndexSelectionControl(
        MvProjectSession session,
        AssetCategory category,
        bool hasIndex,
        bool hasHue = false,
        Func<AssetCategory, AssetIndexSelection, Task>? assetEditor = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _category = category;
        _hasIndex = hasIndex;
        _hasHue = hasHue;
        _assetEditor = assetEditor;
        Height = 46;
        Controls.Add(_value);
        Controls.Add(_clear);
        Controls.Add(_edit);
        Controls.Add(_select);
        Controls.Add(_status);
        _edit.Visible = _assetEditor is not null;
        _select.Click += async (_, _) => await SelectAsync().ConfigureAwait(true);
        _edit.Click += async (_, _) => await EditSelectedAsync().ConfigureAwait(true);
        _clear.Click += (_, _) =>
        {
            Selection = new AssetIndexSelection(string.Empty, 0);
            Hue = 0;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? SelectionChanged;

    public int Hue
    {
        get => _hue;
        set
        {
            _hue = Math.Clamp(value, 0, 360);
            UpdateDisplay();
        }
    }

    public AssetCategory Category
    {
        get => _category;
        set
        {
            if (_category == value) return;
            _category = value;
            UpdateDisplay();
        }
    }

    private AssetIndexSelection _selection = new(string.Empty, 0);

    public AssetIndexSelection Selection
    {
        get => _selection;
        set
        {
            _selection = value ?? new AssetIndexSelection(string.Empty, 0);
            UpdateDisplay();
        }
    }

    private async Task EditSelectedAsync()
    {
        if (_assetEditor is null || string.IsNullOrWhiteSpace(Selection.AssetName)) return;
        await _assetEditor(_category, Selection).ConfigureAwait(true);
    }

    private async ValueTask SelectAsync()
    {
        using AssetIndexPickerDialog dialog = new(_session, _category, _hasIndex, Selection, _hasHue, Hue);
        await dialog.InitializeAsync().ConfigureAwait(true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Selection = dialog.Selection;
        Hue = dialog.Hue;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplay()
    {
        _value.Text = string.IsNullOrWhiteSpace(Selection.AssetName)
            ? "None"
            : _hasIndex ? $"{Selection.AssetName}  [{Selection.Index}]" : Selection.AssetName;
        _edit.Enabled = _assetEditor is not null && !string.IsNullOrWhiteSpace(Selection.AssetName);
        if (string.IsNullOrWhiteSpace(Selection.AssetName))
        {
            _status.Text = "No asset assigned";
            return;
        }
        AssetRecord? record = _session.Assets.Find(new AssetKey(_category, Selection.AssetName));
        string hue = _hasHue ? $" • hue {Hue}°" : string.Empty;
        _status.Text = record is null
            ? "Missing asset" + hue
            : $"{record.Status} • used by {record.Usage.ExactReferenceCount} exact / {record.Usage.HeuristicReferenceCount} probable reference(s){hue}";
    }
}

public sealed class AssetIndexPickerDialog : Form
{
    private readonly MvProjectSession _session;
    private AssetCategory _category;
    private readonly bool _hasIndex;
    private readonly bool _hasHue;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search assets…" };
    private readonly ListBox _assets = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly AssetImageCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly Label _details = new() { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true, Padding = new Padding(6) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Firebrick, AutoEllipsis = true };
    private readonly Button _accept = new() { Text = "Select", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly TrackBar _hueBar = new()
    {
        Minimum = 0,
        Maximum = 360,
        TickFrequency = 30,
        SmallChange = 1,
        LargeChange = 15,
        Dock = DockStyle.Fill,
        AccessibleName = "Monster hue selection bar"
    };
    private readonly NumericUpDown _hueValue = new()
    {
        Minimum = 0,
        Maximum = 360,
        Width = 72,
        AccessibleName = "Monster hue value"
    };
    private readonly System.Windows.Forms.Timer _huePreviewTimer = new() { Interval = 80 };
    private IReadOnlyList<AssetRecord> _all = [];
    private CancellationTokenSource? _previewCancellation;
    private Bitmap? _previewSource;
    private IReadOnlyList<SheetCell> _previewCells = [];
    private int _previewSelectedCell = -1;
    private bool _syncingHue;
    private bool _updatingPreview;
    private bool _initialized;

    public AssetIndexPickerDialog(
        MvProjectSession session,
        AssetCategory category,
        bool hasIndex,
        AssetIndexSelection current,
        bool hasHue = false,
        int currentHue = 0)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _category = category;
        _hasIndex = hasIndex;
        _hasHue = hasHue;
        Selection = current ?? new AssetIndexSelection(string.Empty, 0);
        Hue = Math.Clamp(currentHue, 0, 360);
        _hueBar.Value = Hue;
        _hueValue.Value = Hue;
        Text = _hasHue ? "Select Monster Graphic" : $"Select {category}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 560);
        Size = new Size(1000, 680);

        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 300, 230, 400);
        split.Panel1.Controls.Add(_assets);
        split.Panel1.Controls.Add(_search);
        split.Panel2.Controls.Add(_canvas);
        if (_hasHue) split.Panel2.Controls.Add(BuildHuePanel());
        split.Panel2.Controls.Add(_details);
        split.Panel2.Controls.Add(_status);
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.AddRange([_accept, _cancel]);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = _accept;
        CancelButton = _cancel;

        _search.TextChanged += (_, _) => ApplyFilter();
        _assets.SelectedIndexChanged += async (_, _) => await PreviewSelectedAsync().ConfigureAwait(true);
        _assets.DoubleClick += (_, _) => AcceptSelection();
        _canvas.SelectedCellChanged += (_, _) =>
        {
            if (_updatingPreview) return;
            _previewSelectedCell = _canvas.SelectedCell;
            UpdateDetails();
        };
        _hueBar.ValueChanged += (_, _) => SetHue(_hueBar.Value);
        _hueBar.MouseUp += (_, _) => ApplyPendingHuePreview();
        _hueValue.ValueChanged += (_, _) => SetHue(decimal.ToInt32(_hueValue.Value));
        _huePreviewTimer.Tick += (_, _) => ApplyPendingHuePreview();
        _accept.Click += (_, _) => AcceptSelection();
    }

    public AssetIndexSelection Selection { get; private set; }
    public int Hue { get; private set; }

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_initialized) return ValueTask.CompletedTask;
        _all = _session.Assets.Assets
            .Where(asset => asset.Key.Category == _category)
            .OrderBy(asset => asset.Key.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _initialized = true;
        ApplyFilter();
        int index = _assets.Items.Cast<AssetListItem>().ToList().FindIndex(item =>
            string.Equals(item.Record.Key.LogicalName, Selection.AssetName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _assets.SelectedIndex = index;
        else if (_assets.Items.Count > 0) _assets.SelectedIndex = 0;
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _previewSource?.Dispose();
            _huePreviewTimer.Stop();
            _huePreviewTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ApplyFilter()
    {
        string filter = _search.Text.Trim();
        string? selected = (_assets.SelectedItem as AssetListItem)?.Record.Key.LogicalName;
        _assets.BeginUpdate();
        _assets.Items.Clear();
        foreach (AssetRecord record in _all)
        {
            if (filter.Length > 0 && !record.Key.LogicalName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            _assets.Items.Add(new AssetListItem(record));
        }
        _assets.EndUpdate();
        if (selected is not null)
        {
            int index = _assets.Items.Cast<AssetListItem>().ToList().FindIndex(item =>
                string.Equals(item.Record.Key.LogicalName, selected, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _assets.SelectedIndex = index;
        }
    }

    private async ValueTask PreviewSelectedAsync()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _previewCancellation.Token;
        _status.Text = string.Empty;
        _canvas.SetImage(null);
        _previewSource?.Dispose();
        _previewSource = null;
        _previewCells = [];
        _previewSelectedCell = -1;
        if (_assets.SelectedItem is not AssetListItem item) return;
        AssetRecord record = item.Record;
        AssetVariant? variant = record.PreferredVariant;
        if (variant is null || variant.IsEncrypted)
        {
            _status.Text = variant is null ? "No readable image variant is available." : "Encrypted assets cannot be previewed.";
            UpdateDetails();
            return;
        }
        try
        {
            Bitmap bitmap = await AssetImageLoader.LoadOwnedBitmapAsync(
                _session.AssetContent,
                variant.RelativePath,
                cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            _previewSource = bitmap;
            _previewCells = BuildSelectionCells(record, bitmap.Size);
            _previewSelectedCell = string.Equals(record.Key.LogicalName, Selection.AssetName, StringComparison.OrdinalIgnoreCase)
                ? Selection.Index
                : 0;
            ApplyHuePreview();
            UpdateDetails();
        }
        catch (OperationCanceledException)
        {
            // A newer selection owns the preview.
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            _status.Text = exception.Message;
        }
    }

    private Control BuildHuePanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            ColumnCount = 3,
            Padding = new Padding(6, 2, 6, 2),
            AccessibleName = "Monster hue selector"
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = "Hue",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, 0);
        panel.Controls.Add(_hueBar, 1, 0);
        panel.Controls.Add(_hueValue, 2, 0);
        return panel;
    }

    private void SetHue(int value)
    {
        if (_syncingHue) return;
        value = Math.Clamp(value, 0, 360);
        _syncingHue = true;
        try
        {
            Hue = value;
            if (_hueBar.Value != value) _hueBar.Value = value;
            if (decimal.ToInt32(_hueValue.Value) != value) _hueValue.Value = value;
        }
        finally
        {
            _syncingHue = false;
        }
        _huePreviewTimer.Stop();
        _huePreviewTimer.Start();
        UpdateDetails();
    }

    private void ApplyPendingHuePreview()
    {
        _huePreviewTimer.Stop();
        ApplyHuePreview();
    }

    private void ApplyHuePreview()
    {
        if (_previewSource is null)
        {
            _canvas.SetImage(null);
            return;
        }

        Bitmap display = _hasHue && Hue != 0
            ? EnemyEditorControl.ApplyHue(_previewSource, Hue)
            : (Bitmap)_previewSource.Clone();
        _updatingPreview = true;
        try
        {
            _canvas.SetImage(display, _previewCells);
            _canvas.ShowGrid = _hasIndex && _previewCells.Count > 0;
            _canvas.SelectedCell = _previewCells.Count == 0
                ? -1
                : Math.Clamp(_previewSelectedCell, 0, _previewCells.Count - 1);
        }
        finally
        {
            _updatingPreview = false;
        }
    }

    private IReadOnlyList<SheetCell> BuildSelectionCells(AssetRecord record, Size size)
    {
        if (!_hasIndex) return [];
        if (_category == AssetCategory.Faces)
        {
            return MvSpriteSheetDescriptors.DescribeFaces(size.Width, size.Height).Cells;
        }
        if (_category == AssetCategory.Characters)
        {
            CharacterSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeCharacter(
                record.Key.LogicalName,
                size.Width,
                size.Height);
            if (!descriptor.IsValid) return [];
            int width = size.Width / descriptor.CharacterColumns;
            int height = size.Height / descriptor.CharacterRows;
            List<SheetCell> result = [];
            for (int row = 0; row < descriptor.CharacterRows; row++)
            {
                for (int column = 0; column < descriptor.CharacterColumns; column++)
                {
                    int index = row * descriptor.CharacterColumns + column;
                    result.Add(new SheetCell(column * width, row * height, width, height, index));
                }
            }
            return result;
        }
        return [];
    }

    private void UpdateDetails()
    {
        if (_assets.SelectedItem is not AssetListItem item)
        {
            _details.Text = string.Empty;
            return;
        }
        AssetRecord record = item.Record;
        string index = _hasIndex && _canvas.SelectedCell >= 0 ? $" • index {_canvas.SelectedCell}" : string.Empty;
        string dimensions = record.Metadata.Image is { } image ? $" • {image.Width}×{image.Height}" : string.Empty;
        string hue = _hasHue ? $" • hue {Hue}°" : string.Empty;
        _details.Text = $"{record.Key.LogicalName}{index}{dimensions}{hue} • {record.Status} • {record.Usage.ExactReferenceCount} exact use(s)";
    }

    private void AcceptSelection()
    {
        if (_assets.SelectedItem is not AssetListItem item) return;
        int index = _hasIndex ? Math.Max(0, _canvas.SelectedCell) : 0;
        Selection = new AssetIndexSelection(item.Record.Key.LogicalName, index);
        DialogResult = DialogResult.OK;
    }

    private sealed record AssetListItem(AssetRecord Record)
    {
        public override string ToString() => Record.Status == AssetStatus.Available
            ? Record.Key.LogicalName
            : $"{Record.Key.LogicalName}  [{Record.Status}]";
    }
}
