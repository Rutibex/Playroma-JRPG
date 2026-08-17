using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.WinForms.Rendering;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>A reusable map-and-coordinate field with an RPG-Maker-style visual location browser.</summary>
public sealed class MapLocationPickerControl : UserControl
{
    private readonly MvProjectSession _session;
    private readonly bool _allowMapChange;
    private readonly int? _fixedMapId;
    private readonly NumericUpDown _map = Number();
    private readonly NumericUpDown _x = Number();
    private readonly NumericUpDown _y = Number();
    private readonly TextBox _summary = new() { Dock = DockStyle.Fill, ReadOnly = true, TabStop = false };
    private readonly Button _browse = new() { Text = "Select Location…", AutoSize = true };
    private bool _loading;

    public MapLocationPickerControl(MvProjectSession session, bool allowMapChange = true, int? fixedMapId = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _allowMapChange = allowMapChange;
        _fixedMapId = fixedMapId;
        Height = 64;
        MinimumSize = new Size(360, 60);
        Dock = DockStyle.Top;
        AccessibleName = "Map location picker";

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        layout.Controls.Add(new Label { Text = "Map", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_map, 1, 0);
        layout.Controls.Add(new Label { Text = "X", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 3, 3, 3) }, 2, 0);
        layout.Controls.Add(_x, 3, 0);
        layout.Controls.Add(new Label { Text = "Y", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 3, 3, 3) }, 4, 0);
        layout.Controls.Add(_y, 5, 0);
        layout.Controls.Add(_browse, 7, 0);
        layout.Controls.Add(_summary, 0, 1);
        layout.SetColumnSpan(_summary, 8);
        Controls.Add(layout);

        _map.Enabled = _allowMapChange;
        if (_fixedMapId.HasValue)
        {
            _map.Value = Math.Clamp(_fixedMapId.Value, 0, decimal.ToInt32(_map.Maximum));
            _map.Enabled = false;
        }

        _map.ValueChanged += (_, _) => Changed();
        _x.ValueChanged += (_, _) => Changed();
        _y.ValueChanged += (_, _) => Changed();
        _browse.Click += async (_, _) => await BrowseAsync().ConfigureAwait(true);
        UpdateSummary();
    }

    public event EventHandler? ValueChanged;

    public SystemPosition Value
    {
        get => new(decimal.ToInt32(_map.Value), decimal.ToInt32(_x.Value), decimal.ToInt32(_y.Value));
        set
        {
            _loading = true;
            try
            {
                int mapId = _fixedMapId ?? value.MapId;
                _map.Value = Math.Clamp(mapId, 0, decimal.ToInt32(_map.Maximum));
                _x.Value = Math.Clamp(value.X, 0, decimal.ToInt32(_x.Maximum));
                _y.Value = Math.Clamp(value.Y, 0, decimal.ToInt32(_y.Maximum));
            }
            finally
            {
                _loading = false;
            }
            UpdateSummary();
        }
    }

    public async ValueTask BrowseAsync()
    {
        int? fixedMap = _allowMapChange ? null : (_fixedMapId ?? Value.MapId);
        using MapLocationSelectorDialog dialog = new(_session, Value, fixedMap);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Value = dialog.SelectedPosition;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private void Changed()
    {
        if (_fixedMapId.HasValue && decimal.ToInt32(_map.Value) != _fixedMapId.Value)
        {
            _loading = true;
            _map.Value = Math.Clamp(_fixedMapId.Value, 0, decimal.ToInt32(_map.Maximum));
            _loading = false;
        }
        UpdateSummary();
        if (!_loading) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        SystemPosition value = Value;
        MvMapDescriptor? map = _session.Workspace.Snapshot.Maps.FirstOrDefault(candidate => candidate.Id == value.MapId);
        string mapName = map is null ? $"Map {value.MapId:D3}" : $"{map.Id:D3} {map.Name}";
        _summary.Text = $"{mapName}  ({value.X}, {value.Y})";
    }

    private static NumericUpDown Number() => new()
    {
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 999999,
        ThousandsSeparator = true
    };
}

public sealed class MapLocationSelectorDialog : Form
{
    private readonly MvProjectSession _session;
    private readonly int? _fixedMapId;
    private readonly MvProjectMapRenderLoader _loader = new();
    private readonly TreeView _maps = new() { Dock = DockStyle.Fill, HideSelection = false, AccessibleName = "Project maps" };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search maps…" };
    private readonly MapCanvasControl _canvas = new() { Dock = DockStyle.Fill, Overlays = MapOverlayOptions.Grid | MapOverlayOptions.EventIds, AccessibleName = "Location map preview" };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, AutoEllipsis = true, Padding = new Padding(8, 0, 8, 0), TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown _x = new() { Minimum = 0, Maximum = 999999, Width = 90 };
    private readonly NumericUpDown _y = new() { Minimum = 0, Maximum = 999999, Width = 90 };
    private readonly Label _selection = new() { AutoSize = true, Margin = new Padding(8, 8, 12, 3) };
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(86, 30) };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(86, 30) };
    private CancellationTokenSource? _loadCancellation;
    private int _loadGeneration;
    private int _selectedMapId;
    private bool _updatingCoordinates;

    public MapLocationSelectorDialog(MvProjectSession session, SystemPosition initial, int? fixedMapId = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _fixedMapId = fixedMapId;
        _selectedMapId = fixedMapId ?? initial.MapId;
        SelectedPosition = initial with { MapId = _selectedMapId };

        Text = "Select Map Location";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(880, 620);
        Size = new Size(1120, 760);
        ShowInTaskbar = false;
        KeyPreview = true;

        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 300, 230, 480);
        Panel treeHost = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        treeHost.Controls.Add(_maps);
        treeHost.Controls.Add(_search);
        split.Panel1.Controls.Add(treeHost);

        Panel previewHost = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        FlowLayoutPanel coordinateBar = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4) };
        coordinateBar.Controls.Add(_selection);
        coordinateBar.Controls.Add(new Label { Text = "X", AutoSize = true, Margin = new Padding(8, 8, 3, 3) });
        coordinateBar.Controls.Add(_x);
        coordinateBar.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(8, 8, 3, 3) });
        coordinateBar.Controls.Add(_y);
        previewHost.Controls.Add(_canvas);
        previewHost.Controls.Add(_status);
        previewHost.Controls.Add(coordinateBar);
        split.Panel2.Controls.Add(previewHost);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8) };
        buttons.Controls.AddRange([_ok, _cancel]);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = _cancel;

        _search.TextChanged += (_, _) => RebuildMapTree(_selectedMapId);
        _maps.AfterSelect += async (_, e) =>
        {
            if (e.Node?.Tag is not MvMapDescriptor map) return;
            await SelectMapAsync(map.Id, keepCoordinates: map.Id == initial.MapId).ConfigureAwait(true);
        };
        _canvas.TilePointerDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            SetCoordinates(e.X, e.Y, center: false);
        };
        _canvas.TileDoubleClicked += (_, e) =>
        {
            SetCoordinates(e.X, e.Y, center: false);
            DialogResult = DialogResult.OK;
            Close();
        };
        _x.ValueChanged += (_, _) => CoordinateEditorsChanged();
        _y.ValueChanged += (_, _) => CoordinateEditorsChanged();
        _ok.Click += (_, _) => SelectedPosition = new SystemPosition(_selectedMapId, decimal.ToInt32(_x.Value), decimal.ToInt32(_y.Value));
        FormClosed += (_, _) => CancelLoad();

        RebuildMapTree(_selectedMapId);
        SetCoordinates(initial.X, initial.Y, center: false);
    }

    public SystemPosition SelectedPosition { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CancelLoad();
        base.Dispose(disposing);
    }

    private void RebuildMapTree(int selectedMapId)
    {
        string filter = _search.Text.Trim();
        IReadOnlyList<MvMapDescriptor> all = _fixedMapId.HasValue
            ? _session.Workspace.Snapshot.Maps.Where(map => map.Id == _fixedMapId.Value).ToArray()
            : _session.Workspace.Snapshot.Maps;

        HashSet<int>? include = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            include = all.Where(map => map.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || map.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(map => map.Id)
                .ToHashSet();
            foreach (MvMapDescriptor match in all.Where(map => include.Contains(map.Id)).ToArray())
            {
                int parent = match.ParentId;
                while (parent > 0 && include.Add(parent)) parent = all.FirstOrDefault(map => map.Id == parent)?.ParentId ?? 0;
            }
        }

        _maps.BeginUpdate();
        _maps.Nodes.Clear();
        try
        {
            MvMapDescriptor[] visible = all.Where(map => include is null || include.Contains(map.Id)).ToArray();
            Dictionary<int, TreeNode> nodes = visible.ToDictionary(
                map => map.Id,
                map => new TreeNode($"{map.Id:D3} {map.Name}")
                {
                    Tag = map,
                    ForeColor = map.FileExists ? SystemColors.WindowText : SystemColors.GrayText
                });
            foreach (MvMapDescriptor map in visible.OrderBy(map => map.Order).ThenBy(map => map.Id))
            {
                TreeNode node = nodes[map.Id];
                if (map.ParentId > 0 && nodes.TryGetValue(map.ParentId, out TreeNode? parent)) parent.Nodes.Add(node);
                else _maps.Nodes.Add(node);
            }
            if (!string.IsNullOrWhiteSpace(filter)) _maps.ExpandAll();
            TreeNode? selected = FindMapNode(_maps.Nodes, selectedMapId) ?? FirstNode(_maps.Nodes);
            if (selected is not null)
            {
                _maps.SelectedNode = selected;
                selected.EnsureVisible();
            }
        }
        finally
        {
            _maps.EndUpdate();
        }
    }

    private async ValueTask SelectMapAsync(int mapId, bool keepCoordinates)
    {
        MvMapDescriptor? descriptor = _session.Workspace.Snapshot.Maps.FirstOrDefault(map => map.Id == mapId);
        if (descriptor is null || !descriptor.FileExists)
        {
            _status.Text = descriptor is null ? $"Map {mapId:D3} was not found." : $"Map file for {descriptor.Id:D3} {descriptor.Name} is missing.";
            _ok.Enabled = false;
            return;
        }

        _selectedMapId = mapId;
        _ok.Enabled = false;
        _status.Text = $"Loading {descriptor.Id:D3} {descriptor.Name}…";
        CancelLoad();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken token = _loadCancellation.Token;
        int generation = ++_loadGeneration;
        try
        {
            LoadedMapRenderContext context = await _loader.LoadAsync(_session, mapId, descriptor.RelativePath, descriptor.Name, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || generation != _loadGeneration || IsDisposed) return;
            _canvas.SetContext(context);
            _x.Maximum = Math.Max(0, context.Map.Width - 1);
            _y.Maximum = Math.Max(0, context.Map.Height - 1);
            int x = keepCoordinates ? SelectedPosition.X : Math.Min(decimal.ToInt32(_x.Value), context.Map.Width - 1);
            int y = keepCoordinates ? SelectedPosition.Y : Math.Min(decimal.ToInt32(_y.Value), context.Map.Height - 1);
            SetCoordinates(x, y, center: true);
            _status.Text = $"{descriptor.Id:D3} {descriptor.Name} — {context.Map.Width}×{context.Map.Height}. Click a tile to select the location; double-click to accept.";
            _ok.Enabled = true;
            BeginInvoke(new Action(() =>
            {
                _canvas.ZoomToFit();
                _canvas.CentreOnTile(decimal.ToInt32(_x.Value), decimal.ToInt32(_y.Value));
            }));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _loadGeneration || IsDisposed) return;
            _status.Text = "Map preview failed: " + exception.Message;
            _ok.Enabled = false;
        }
    }

    private void CoordinateEditorsChanged()
    {
        if (_updatingCoordinates) return;
        SetCoordinates(decimal.ToInt32(_x.Value), decimal.ToInt32(_y.Value), center: false);
    }

    private void SetCoordinates(int x, int y, bool center)
    {
        x = Math.Clamp(x, 0, decimal.ToInt32(_x.Maximum));
        y = Math.Clamp(y, 0, decimal.ToInt32(_y.Maximum));
        _updatingCoordinates = true;
        try
        {
            _x.Value = x;
            _y.Value = y;
        }
        finally
        {
            _updatingCoordinates = false;
        }
        SelectedPosition = new SystemPosition(_selectedMapId, x, y);
        _selection.Text = $"Selected: Map {_selectedMapId:D3}  ({x}, {y})";
        _canvas.SelectionRegion = new PixelRect(x * MvTileRules.TileWidth, y * MvTileRules.TileHeight, MvTileRules.TileWidth, MvTileRules.TileHeight);
        if (center) _canvas.CentreOnTile(x, y);
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private static TreeNode? FindMapNode(TreeNodeCollection nodes, int mapId)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is MvMapDescriptor map && map.Id == mapId) return node;
            TreeNode? child = FindMapNode(node.Nodes, mapId);
            if (child is not null) return child;
        }
        return null;
    }

    private static TreeNode? FirstNode(TreeNodeCollection nodes) => nodes.Count == 0 ? null : nodes[0];
}
