using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Assets;
using Playroma.JRPG.Rendering;
using Playroma.JRPG.Rendering.Windows;

namespace Playroma.JRPG.Editor.WinForms.ToolWindows;

/// <summary>
/// RPG Maker-style A-E and Region palette. The A tab exposes MV's A1-A5 sub-sheets,
/// ordinary sheets support rectangular multi-tile brush selection, and the R tab publishes
/// region IDs through the same editor selection service.
/// </summary>
public sealed class TilesetPaletteControl : UserControl
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _selection;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Top, Height = 30 };
    private readonly ComboBox _aSheets = new()
    {
        Dock = DockStyle.Top,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Height = 28
    };
    private readonly Label _title = new()
    {
        Dock = DockStyle.Top,
        Height = 42,
        AutoEllipsis = true,
        Padding = new Padding(5)
    };
    private readonly AssetImageCanvas _canvas = new()
    {
        Dock = DockStyle.Fill,
        ShowGrid = true,
        Zoom = 0.5f
    };
    private readonly TrackBar _zoom = new()
    {
        Dock = DockStyle.Bottom,
        Minimum = 10,
        Maximum = 400,
        Value = 50,
        TickFrequency = 50,
        Height = 32
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 42,
        AutoEllipsis = true,
        Padding = new Padding(4)
    };
    private readonly Dictionary<int, PaletteEntry> _entries = [];
    private readonly Dictionary<string, TileSheetKind> _ordinaryTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B"] = TileSheetKind.B,
        ["C"] = TileSheetKind.C,
        ["D"] = TileSheetKind.D,
        ["E"] = TileSheetKind.E
    };
    private LoadedMapRenderContext? _context;
    private CancellationTokenSource? _loadCancellation;
    private string _requestedTab = "A";

    public TilesetPaletteControl(IWorkspaceService workspace, ISelectionService selection)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));

        foreach (string name in new[] { "A", "B", "C", "D", "E", "R" })
        {
            _tabs.TabPages.Add(new TabPage(name) { Name = name, ToolTipText = PaletteToolTip(name) });
        }

        Controls.Add(_canvas);
        Controls.Add(_aSheets);
        Controls.Add(_tabs);
        Controls.Add(_title);
        Controls.Add(_zoom);
        Controls.Add(_status);

        _selection.SelectionChanged += OnSelectionChanged;
        _workspace.StateChanged += OnWorkspaceChanged;
        _tabs.SelectedIndexChanged += OnPaletteTabChanged;
        _aSheets.SelectedIndexChanged += (_, _) => DisplaySelectedPalette();
        _zoom.ValueChanged += (_, _) => _canvas.Zoom = _zoom.Value / 100f;
        _canvas.SelectedCellsChanged += OnSelectedCellsChanged;
        _status.Text = "Select a map, then choose a logical tile or region.";
    }

    public int PaletteZoomPercent
    {
        get => _zoom.Value;
        set => _zoom.Value = Math.Clamp(value, _zoom.Minimum, _zoom.Maximum);
    }

    public string SelectedPaletteTab
    {
        get => _tabs.SelectedTab?.Text ?? _requestedTab;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string requested = NormalizeTab(value);
            _requestedTab = requested;
            TabPage? page = _tabs.TabPages.Cast<TabPage>()
                .FirstOrDefault(candidate => string.Equals(candidate.Text, requested, StringComparison.OrdinalIgnoreCase));
            if (page is not null) _tabs.SelectedTab = page;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selection.SelectionChanged -= OnSelectionChanged;
            _workspace.StateChanged -= OnWorkspaceChanged;
            _tabs.SelectedIndexChanged -= OnPaletteTabChanged;
            _canvas.SelectedCellsChanged -= OnSelectedCellsChanged;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnSelectionChanged(sender, e))) return;
        if (_selection.Current is MapSelection map) _ = LoadMapAsync(map.MapId);
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceChanged(sender, e))) return;
        if (e.Session is not null) return;
        _context = null;
        _entries.Clear();
        _aSheets.Items.Clear();
        _canvas.SetImage(null);
        _title.Text = string.Empty;
        _status.Text = "No project is open.";
        UpdateTabAvailability();
    }

    private async Task LoadMapAsync(int mapId)
    {
        Playroma.JRPG.Application.MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken token = _loadCancellation.Token;
        _status.Text = "Loading tileset…";
        try
        {
            LoadedMapRenderContext context = await new MvProjectMapRenderLoader()
                .LoadAsync(session, mapId, token)
                .ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _context = context;
            _title.Text = $"{context.Tileset.Id:D4} {context.Tileset.Name}";

            string? previousA = (_aSheets.SelectedItem as SheetChoice)?.Sheet.ToString();
            _aSheets.BeginUpdate();
            _aSheets.Items.Clear();
            foreach (TileSheetKind sheet in new[]
                     {
                         TileSheetKind.A1,
                         TileSheetKind.A2,
                         TileSheetKind.A3,
                         TileSheetKind.A4,
                         TileSheetKind.A5
                     })
            {
                string name = context.Tileset.GetSheetName(sheet);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _aSheets.Items.Add(new SheetChoice(sheet, $"{sheet}: {name}"));
                }
            }
            _aSheets.EndUpdate();
            int previousIndex = -1;
            if (!string.IsNullOrWhiteSpace(previousA))
            {
                for (int index = 0; index < _aSheets.Items.Count; index++)
                {
                    if (_aSheets.Items[index] is SheetChoice choice
                        && string.Equals(choice.Sheet.ToString(), previousA, StringComparison.OrdinalIgnoreCase))
                    {
                        previousIndex = index;
                        break;
                    }
                }
            }
            if (_aSheets.Items.Count > 0) _aSheets.SelectedIndex = previousIndex >= 0 ? previousIndex : 0;

            UpdateTabAvailability();
            SelectedPaletteTab = _requestedTab;
            DisplaySelectedPalette();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Tileset loading cancelled.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _canvas.SetImage(null);
        }
    }

    private void OnPaletteTabChanged(object? sender, EventArgs e)
    {
        _requestedTab = _tabs.SelectedTab?.Text ?? "A";
        DisplaySelectedPalette();
    }

    private void DisplaySelectedPalette()
    {
        _entries.Clear();
        string tab = _tabs.SelectedTab?.Text ?? "A";
        _aSheets.Visible = string.Equals(tab, "A", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(tab, "R", StringComparison.OrdinalIgnoreCase))
        {
            DisplayRegionPalette();
            return;
        }

        if (_context is null)
        {
            _canvas.SetImage(null);
            return;
        }

        TileSheetKind? sheet = ResolveSelectedSheet(tab);
        if (!sheet.HasValue
            || !_context.Assets.Tilesets.TryGetValue(sheet.Value, out RgbaImage? image))
        {
            _canvas.SetImage(null);
            _status.Text = $"The {tab} palette is not populated for this tileset.";
            return;
        }

        IReadOnlyList<SheetCell> cells = BuildCells(sheet.Value, image.Width, image.Height);
        Bitmap bitmap = BitmapPixelConverter.ToBitmap(image);
        _canvas.SetImage(bitmap, cells);
        _status.Text = sheet is TileSheetKind.B or TileSheetKind.C or TileSheetKind.D or TileSheetKind.E or TileSheetKind.A5
            ? $"{sheet}: drag across tiles to create a multi-tile brush."
            : $"{sheet}: select an autotile kind; its shape is calculated while painting.";
        PublishSelection();
    }

    private TileSheetKind? ResolveSelectedSheet(string tab)
    {
        if (string.Equals(tab, "A", StringComparison.OrdinalIgnoreCase))
        {
            return (_aSheets.SelectedItem as SheetChoice)?.Sheet;
        }
        return _ordinaryTabs.TryGetValue(tab, out TileSheetKind sheet) ? sheet : null;
    }

    private void DisplayRegionPalette()
    {
        const int columns = 16;
        const int rows = 16;
        const int cellSize = 48;
        Bitmap bitmap = new(columns * cellSize, rows * cellSize);
        List<SheetCell> cells = [];
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(45, 47, 51));
            using StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using Font font = new(this.Font.FontFamily, 9f, FontStyle.Bold, GraphicsUnit.Point);
            for (int region = 0; region < 256; region++)
            {
                int column = region % columns;
                int row = region / columns;
                Rectangle rectangle = new(column * cellSize, row * cellSize, cellSize, cellSize);
                int shade = 60 + ((region * 37) % 150);
                using SolidBrush fill = new(Color.FromArgb(shade, 255 - shade / 2, 90 + shade / 3));
                graphics.FillRectangle(fill, rectangle);
                graphics.DrawString(region.ToString(System.Globalization.CultureInfo.InvariantCulture), font, Brushes.Black, rectangle, format);
                cells.Add(new SheetCell(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, region));
                _entries[region] = new PaletteEntry(region, 5, "R", rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
        }
        _canvas.SetImage(bitmap, cells);
        _status.Text = "R: select a region ID from 0 to 255.";
        PublishSelection();
    }

    private IReadOnlyList<SheetCell> BuildCells(TileSheetKind sheet, int width, int height)
    {
        List<SheetCell> cells = [];
        switch (sheet)
        {
            case TileSheetKind.B:
            case TileSheetKind.C:
            case TileSheetKind.D:
            case TileSheetKind.E:
            case TileSheetKind.A5:
                BuildNormalCells(sheet, width, height, cells);
                break;
            case TileSheetKind.A1:
                BuildA1Cells(width, height, cells);
                break;
            case TileSheetKind.A2:
                BuildAutotileGrid(sheet, width, height, 96, 144, MvTileRules.TileIdA2, cells);
                break;
            case TileSheetKind.A3:
                BuildAutotileGrid(sheet, width, height, 96, 96, MvTileRules.TileIdA3, cells);
                break;
            case TileSheetKind.A4:
                BuildA4Cells(width, height, cells);
                break;
        }
        return cells;
    }

    private void BuildNormalCells(TileSheetKind sheet, int width, int height, List<SheetCell> cells)
    {
        int baseId = sheet switch
        {
            TileSheetKind.B => MvTileRules.TileIdB,
            TileSheetKind.C => MvTileRules.TileIdC,
            TileSheetKind.D => MvTileRules.TileIdD,
            TileSheetKind.E => MvTileRules.TileIdE,
            TileSheetKind.A5 => MvTileRules.TileIdA5,
            _ => throw new ArgumentOutOfRangeException(nameof(sheet))
        };
        int columns = Math.Min(16, width / MvTileRules.TileWidth);
        int rows = height / MvTileRules.TileHeight;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int localId = checked((column / 8) * 128 + row * 8 + column % 8);
                int tileId = checked(baseId + localId);
                int suggestedLayer = sheet is TileSheetKind.B or TileSheetKind.C or TileSheetKind.D or TileSheetKind.E ? 2 : 0;
                Add(cells, column * 48, row * 48, 48, 48, tileId, suggestedLayer, sheet.ToString());
            }
        }
    }

    private void BuildAutotileGrid(
        TileSheetKind sheet,
        int width,
        int height,
        int cellWidth,
        int cellHeight,
        int firstTileId,
        List<SheetCell> cells)
    {
        int columns = width / cellWidth;
        int rows = height / cellHeight;
        int firstKind = MvTileRules.GetAutotileKind(firstTileId);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int kind = checked(firstKind + row * 8 + column);
                Add(
                    cells,
                    column * cellWidth,
                    row * cellHeight,
                    cellWidth,
                    cellHeight,
                    MvTileRules.MakeAutotileId(kind, 0),
                    0,
                    sheet.ToString());
            }
        }
    }

    private void BuildA1Cells(int width, int height, List<SheetCell> cells)
    {
        for (int kind = 0; kind < 16; kind++)
        {
            int tx = kind % 8;
            int ty = kind / 8;
            int bx;
            int by;
            bool waterfall = false;
            if (kind == 0) { bx = 0; by = 0; }
            else if (kind == 1) { bx = 0; by = 3; }
            else if (kind == 2) { bx = 6; by = 0; }
            else if (kind == 3) { bx = 6; by = 3; }
            else
            {
                bx = tx / 4 * 8;
                by = ty * 6 + tx / 2 % 2 * 3;
                if (kind % 2 == 1) { bx += 6; waterfall = true; }
            }
            int x = bx * 48;
            int y = by * 48;
            int cellWidth = 96;
            int cellHeight = waterfall ? 96 : 144;
            if (x >= width || y >= height) continue;
            cellWidth = Math.Min(cellWidth, width - x);
            cellHeight = Math.Min(cellHeight, height - y);
            Add(cells, x, y, cellWidth, cellHeight, MvTileRules.MakeAutotileId(kind, 0), 0, "A1");
        }
    }

    private void BuildA4Cells(int width, int height, List<SheetCell> cells)
    {
        int firstKind = MvTileRules.GetAutotileKind(MvTileRules.TileIdA4);
        for (int row = 0; row < 6; row++)
        {
            int ty = 10 + row;
            int by = (int)Math.Floor((ty - 10) * 2.5 + (ty % 2 == 1 ? 0.5 : 0));
            int cellHeight = ty % 2 == 1 ? 96 : 144;
            for (int column = 0; column < 8; column++)
            {
                int x = column * 96;
                int y = by * 48;
                if (x >= width || y >= height) continue;
                int kind = checked(firstKind + row * 8 + column);
                Add(
                    cells,
                    x,
                    y,
                    Math.Min(96, width - x),
                    Math.Min(cellHeight, height - y),
                    MvTileRules.MakeAutotileId(kind, 0),
                    0,
                    "A4");
            }
        }
    }

    private void Add(
        List<SheetCell> cells,
        int x,
        int y,
        int width,
        int height,
        int tileId,
        int layer,
        string sheet)
    {
        int index = cells.Count;
        cells.Add(new SheetCell(x, y, width, height, index));
        _entries[index] = new PaletteEntry(tileId, layer, sheet, x, y, width, height);
    }

    private void OnSelectedCellsChanged(object? sender, EventArgs e) => PublishSelection();

    private void PublishSelection()
    {
        IReadOnlyList<int> selectedIndices = _canvas.SelectedCells;
        if (selectedIndices.Count == 0
            || !_entries.TryGetValue(_canvas.SelectedCell, out PaletteEntry? primary))
        {
            return;
        }

        List<PaletteEntry> selected = selectedIndices
            .Select(index => _entries.TryGetValue(index, out PaletteEntry? entry) ? entry : null)
            .Where(entry => entry is not null)
            .Cast<PaletteEntry>()
            .ToList();
        bool supportsBrush = selected.Count > 1
            && selected.All(entry => entry.Sheet == primary.Sheet)
            && selected.All(entry => entry.Width == MvTileRules.TileWidth && entry.Height == MvTileRules.TileHeight);
        if (!supportsBrush)
        {
            _selection.Select(new MapTilePaletteSelection(primary.TileId, primary.SuggestedLayer, primary.Sheet));
            _status.Text = $"{primary.Sheet} tile {primary.TileId} selected — suggested layer {primary.SuggestedLayer + 1}.";
            return;
        }

        int left = selected.Min(entry => entry.X);
        int top = selected.Min(entry => entry.Y);
        int right = selected.Max(entry => entry.X + entry.Width);
        int bottom = selected.Max(entry => entry.Y + entry.Height);
        int brushWidth = Math.Max(1, (right - left) / MvTileRules.TileWidth);
        int brushHeight = Math.Max(1, (bottom - top) / MvTileRules.TileHeight);
        int[] values = new int[brushWidth * brushHeight];
        foreach (PaletteEntry entry in selected)
        {
            int x = (entry.X - left) / MvTileRules.TileWidth;
            int y = (entry.Y - top) / MvTileRules.TileHeight;
            if (x >= 0 && x < brushWidth && y >= 0 && y < brushHeight)
            {
                values[y * brushWidth + x] = entry.TileId;
            }
        }
        int anchorX = Math.Clamp((primary.X - left) / MvTileRules.TileWidth, 0, brushWidth - 1);
        int anchorY = Math.Clamp((primary.Y - top) / MvTileRules.TileHeight, 0, brushHeight - 1);
        _selection.Select(new MapTileBrushSelection(
            primary.Sheet,
            brushWidth,
            brushHeight,
            values,
            primary.SuggestedLayer,
            anchorX,
            anchorY));
        _status.Text = $"{brushWidth}×{brushHeight} {primary.Sheet} brush selected — drag to stamp it on the map.";
    }

    private void UpdateTabAvailability()
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            page.Enabled = page.Text switch
            {
                "A" => _aSheets.Items.Count > 0,
                "R" => true,
                _ => _context is not null
                    && _ordinaryTabs.TryGetValue(page.Text, out TileSheetKind sheet)
                    && !string.IsNullOrWhiteSpace(_context.Tileset.GetSheetName(sheet))
            };
        }
        if (_tabs.SelectedTab is { Enabled: false })
        {
            _tabs.SelectedTab = _tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Enabled)
                ?? _tabs.TabPages["R"];
        }
    }

    private static string NormalizeTab(string value)
    {
        string trimmed = value.Trim().ToUpperInvariant();
        return trimmed.StartsWith('A') ? "A" : trimmed is "B" or "C" or "D" or "E" or "R" ? trimmed : "A";
    }

    private static string PaletteToolTip(string name) => name switch
    {
        "A" => "Ground, wall, building, and autotile sheets",
        "R" => "Region IDs",
        _ => $"Upper-layer {name} tiles"
    };

    private sealed record PaletteEntry(
        int TileId,
        int SuggestedLayer,
        string Sheet,
        int X,
        int Y,
        int Width,
        int Height);

    private sealed record SheetChoice(TileSheetKind Sheet, string Text)
    {
        public override string ToString() => Text;
    }
}
