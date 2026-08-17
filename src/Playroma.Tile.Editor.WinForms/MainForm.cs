using System.Drawing.Imaging;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Editor.WinForms.Controls;
using Playroma.Tile.Editor.WinForms.Services;
using Playroma.Tile.RpgMakerMV.Export;
using Playroma.Tile.RpgMakerMV.Formats;
using Playroma.Tile.RpgMakerMV.Imaging;
using Playroma.Tile.RpgMakerMV.Import;
using Playroma.Tile.RpgMakerMV.Validation;
using Playroma.Tile.Materials.Validation;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Tilesets.Services;
using Playroma.Tile.Tilesets.Validation;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Editor.WinForms;

public sealed partial class MainForm : Form
{
    private enum CanvasSelectionMode
    {
        LogicalAssets,
        RawCells
    }

    private readonly EditorWorkspace _workspace = new();
    private readonly MvValidator _validator = new();
    private readonly MvExportService _exportService = new();
    private readonly TreeView _projectTree = new();
    private readonly TileSheetCanvas _canvas = new();
    private readonly RegionMetadataPanel _metadataPanel = new();
    private readonly ListView _validationList = new();
    private readonly ToolStripComboBox _modeCombo = new();
    private readonly ToolStripComboBox _zoomCombo = new();
    private readonly ToolStripButton _gridButton = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _coordinateLabel = new();
    private readonly Dictionary<Guid, TreeNode> _sheetNodes = new();
    private SheetSession? _currentSession;
    private TileSheetDefinition? _currentSheet;
    private TileSetDefinition? _currentTileSet;
    private Bitmap? _internalClipboard;
    private CanvasSelectionMode _selectionMode = CanvasSelectionMode.LogicalAssets;

    public MainForm()
        : this(null)
    {
    }

    public MainForm(Playroma.Tile.Editor.WinForms.Integration.TileEditorIntegrationOptions? integration)
    {
        _integration = integration;
        Text = "Playroma Tile — Tileset Creation Studio";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1440;
        Height = 920;
        MinimumSize = new Size(980, 650);
        AllowDrop = true;

        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var status = BuildStatusStrip();
        var content = BuildContent();

        Controls.Add(content);
        Controls.Add(status);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _canvas.SelectedRegionChanged += (_, _) => UpdateRegionPanel();
        _canvas.ImageCoordinateChanged += (_, args) =>
            _coordinateLabel.Text = _canvas.SheetImage is null
                ? string.Empty
                : $"X {args.ImagePoint.X}, Y {args.ImagePoint.Y}";
        _metadataPanel.MetadataChanged += (_, _) =>
        {
            _workspace.MarkProjectDirty();
            UpdateTreeSheetText();
            UpdateWindowTitle();
        };

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += OnFormClosing;
        Shown += (_, _) => InitializeIntegratedWorkspace();
        UpdateUiState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _internalClipboard?.Dispose();
            _workspace.Dispose();
        }

        base.Dispose(disposing);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(CreateMenuItem("&New Project…", Keys.Control | Keys.N, (_, _) => NewProject()));
        file.DropDownItems.Add(CreateMenuItem("&Open Project…", Keys.Control | Keys.O, (_, _) => OpenProject()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("Import &Folder…", Keys.None, (_, _) => ImportFolder()));
        file.DropDownItems.Add(CreateMenuItem("Import &PNG Files…", Keys.None, (_, _) => ImportPngFiles()));
        file.DropDownItems.Add(CreateMenuItem("Import &ZIP Archive…", Keys.None, (_, _) => ImportArchive()));
        if (IsIntegrated)
        {
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(CreateMenuItem("Load Tilesets from jRPG &Database…", Keys.Control | Keys.Shift | Keys.I, (_, _) => LoadFromDatabaseInteractive()));
            file.DropDownItems.Add(CreateMenuItem("&Refresh Linked Database Tilesets", Keys.None, (_, _) => RefreshLinkedDatabaseTilesets()));
        }
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("&Save", Keys.Control | Keys.S, (_, _) => SaveProject()));
        file.DropDownItems.Add(CreateMenuItem("Save Project &As…", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveProjectAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("Export &Selected Tileset…", Keys.Control | Keys.E, (_, _) => ExportSelectedTileSet()));
        file.DropDownItems.Add(CreateMenuItem("Export &All Tilesets…", Keys.None, (_, _) => ExportAllTileSets()));
        if (IsIntegrated)
        {
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(CreateMenuItem("Export Selected to jRPG &Database…", Keys.Control | Keys.Shift | Keys.E, (_, _) => ExportSelectedToDatabase()));
            file.DropDownItems.Add(CreateMenuItem("Export All to jRPG Database", Keys.None, (_, _) => ExportAllToDatabase()));
            if (!string.IsNullOrWhiteSpace(_integration?.SpriteBridgeReturnDirectory))
                file.DropDownItems.Add(CreateMenuItem("Return Selected Sheet to Sprite Studio…", Keys.Control | Keys.Shift | Keys.R, (_, _) => ReturnCurrentSheetToSpriteStudio()));
        }
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(CreateMenuItem("E&xit", Keys.Alt | Keys.F4, (_, _) => RequestExit()));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add(CreateMenuItem("&Undo", Keys.Control | Keys.Z, (_, _) => Undo()));
        edit.DropDownItems.Add(CreateMenuItem("&Redo", Keys.Control | Keys.Y, (_, _) => Redo()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(CreateMenuItem("Cu&t Region", Keys.Control | Keys.X, (_, _) => CutSelection()));
        edit.DropDownItems.Add(CreateMenuItem("&Copy Region", Keys.Control | Keys.C, (_, _) => CopySelection()));
        edit.DropDownItems.Add(CreateMenuItem("&Paste Region", Keys.Control | Keys.V, (_, _) => PasteSelection()));
        edit.DropDownItems.Add(CreateMenuItem("&Clear Region", Keys.Delete, (_, _) => ClearSelection()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(CreateMenuItem("Import Image into Region…", Keys.None, (_, _) => ImportImageIntoSelection()));
        edit.DropDownItems.Add(CreateMenuItem("Export Region Image…", Keys.None, (_, _) => ExportSelectionImage()));

        var view = new ToolStripMenuItem("&View");
        var showGrid = new ToolStripMenuItem("Show &Grid") { Checked = true, CheckOnClick = true };
        showGrid.CheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = showGrid.Checked;
            _gridButton.Checked = showGrid.Checked;
            _canvas.Invalidate();
        };
        view.DropDownItems.Add(showGrid);
        view.DropDownItems.Add(CreateMenuItem("MV &Logical Assets", Keys.None, (_, _) => SetSelectionMode(CanvasSelectionMode.LogicalAssets)));
        view.DropDownItems.Add(CreateMenuItem("Raw &48 × 48 Cells", Keys.None, (_, _) => SetSelectionMode(CanvasSelectionMode.RawCells)));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(CreateMenuItem("&Autotile Compiler Workshop…", Keys.F6, (_, _) => OpenAutotileWorkshop()));
        tools.DropDownItems.Add(CreateMenuItem("&Procedural Material Laboratory…", Keys.F7, (_, _) => OpenMaterialLaboratory()));
        tools.DropDownItems.Add(CreateMenuItem("&Tileset Construction Studio…", Keys.F8, (_, _) => OpenTilesetConstruction()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(CreateMenuItem("&Validate Project", Keys.F9, (_, _) => ValidateProject()));
        tools.DropDownItems.Add(CreateMenuItem("Rename Selected Tileset…", Keys.F2, (_, _) => BeginRenameTileSet()));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(CreateMenuItem("&About Playroma Tile", Keys.None, (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, tools, help });
        return menu;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(CreateToolButton("New", (_, _) => NewProject()));
        toolbar.Items.Add(CreateToolButton("Open", (_, _) => OpenProject()));
        toolbar.Items.Add(CreateToolButton("Save", (_, _) => SaveProject()));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(CreateToolButton("Import", (_, _) => ImportPngFiles()));
        toolbar.Items.Add(CreateToolButton("Export", (_, _) => ExportSelectedTileSet()));
        if (IsIntegrated)
        {
            toolbar.Items.Add(CreateToolButton("Load DB", (_, _) => LoadFromDatabaseInteractive()));
            toolbar.Items.Add(CreateToolButton("Export DB", (_, _) => ExportSelectedToDatabase()));
        }
        toolbar.Items.Add(CreateToolButton("Validate", (_, _) => ValidateProject()));
        toolbar.Items.Add(CreateToolButton("Materials", (_, _) => OpenMaterialLaboratory()));
        toolbar.Items.Add(CreateToolButton("Tileset", (_, _) => OpenTilesetConstruction()));
        toolbar.Items.Add(new ToolStripSeparator());

        toolbar.Items.Add(new ToolStripLabel("Selection:"));
        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Items.AddRange(new object[] { "MV logical assets", "48 × 48 source cells" });
        _modeCombo.SelectedIndex = 0;
        _modeCombo.AutoSize = false;
        _modeCombo.Width = 170;
        _modeCombo.SelectedIndexChanged += (_, _) =>
            SetSelectionMode(_modeCombo.SelectedIndex == 1
                ? CanvasSelectionMode.RawCells
                : CanvasSelectionMode.LogicalAssets);
        toolbar.Items.Add(_modeCombo);

        toolbar.Items.Add(new ToolStripLabel("Zoom:"));
        _zoomCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _zoomCombo.Items.AddRange(new object[] { "25%", "50%", "75%", "100%", "150%", "200%", "300%", "400%" });
        _zoomCombo.SelectedItem = "100%";
        _zoomCombo.AutoSize = false;
        _zoomCombo.Width = 74;
        _zoomCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_zoomCombo.SelectedItem is string text &&
                double.TryParse(text.TrimEnd('%'), out var percent))
            {
                _canvas.Zoom = percent / 100.0;
            }
        };
        toolbar.Items.Add(_zoomCombo);

        _gridButton.Text = "Grid";
        _gridButton.CheckOnClick = true;
        _gridButton.Checked = true;
        _gridButton.CheckedChanged += (_, _) =>
        {
            _canvas.ShowGrid = _gridButton.Checked;
            _canvas.Invalidate();
        };
        toolbar.Items.Add(_gridButton);
        return toolbar;
    }

    private StatusStrip BuildStatusStrip()
    {
        var status = new StatusStrip();
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        status.Items.Add(_statusLabel);
        status.Items.Add(_coordinateLabel);
        return status;
    }

    private Control BuildContent()
    {
        _projectTree.Dock = DockStyle.Fill;
        _projectTree.HideSelection = false;
        _projectTree.LabelEdit = true;
        _projectTree.AfterSelect += (_, _) => HandleTreeSelection();
        _projectTree.BeforeLabelEdit += (_, args) =>
        {
            if (args.Node?.Tag is not TileSetDefinition)
            {
                args.CancelEdit = true;
            }
        };
        _projectTree.AfterLabelEdit += (_, args) =>
        {
            if (args.Node?.Tag is TileSetDefinition tileSet && !string.IsNullOrWhiteSpace(args.Label))
            {
                tileSet.Name = args.Label.Trim();
                _workspace.MarkProjectDirty();
                UpdateWindowTitle();
            }
        };
        _projectTree.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.F2)
            {
                BeginRenameTileSet();
                args.Handled = true;
            }
        };

        var canvasHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(37, 37, 38)
        };
        _canvas.Location = Point.Empty;
        canvasHost.Controls.Add(_canvas);

        var editorSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(900, 650),
            SplitterDistance = 560,
            Panel1MinSize = 220,
            Panel2MinSize = 220
        };
        editorSplit.Resize += (_, _) => AdjustVerticalSplit(editorSplit, 300);
        editorSplit.Panel1.Controls.Add(canvasHost);
        editorSplit.Panel2.Controls.Add(_metadataPanel);

        ConfigureValidationList();
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Size = new Size(900, 700),
            SplitterDistance = 510,
            Panel1MinSize = 260,
            Panel2MinSize = 100
        };
        rightSplit.Resize += (_, _) => AdjustHorizontalSplit(rightSplit, 170);
        rightSplit.Panel1.Controls.Add(editorSplit);
        rightSplit.Panel2.Controls.Add(_validationList);

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1400, 790),
            SplitterDistance = 250,
            Panel1MinSize = 180
        };
        mainSplit.Panel1.Controls.Add(_projectTree);
        mainSplit.Panel2.Controls.Add(rightSplit);
        return mainSplit;
    }

    private void ConfigureValidationList()
    {
        _validationList.Dock = DockStyle.Fill;
        _validationList.View = View.Details;
        _validationList.FullRowSelect = true;
        _validationList.GridLines = true;
        _validationList.Columns.Add("Severity", 80);
        _validationList.Columns.Add("Tileset", 150);
        _validationList.Columns.Add("Sheet", 80);
        _validationList.Columns.Add("Message", 720);
        _validationList.DoubleClick += (_, _) => NavigateToValidationIssue();
    }

    private void NewProject()
    {
        if (!ConfirmDiscardOrSave())
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Playroma Tile project (*.playromatile.json)|*.playromatile.json|JSON files (*.json)|*.json",
            DefaultExt = "playromatile.json",
            AddExtension = true,
            FileName = "MyTileset.playromatile.json",
            Title = "Create Playroma Tile Project"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(dialog.FileName));
            _workspace.CreateNew(dialog.FileName, name);
            ResetCurrentSheet();
            RefreshProjectTree();
            _statusLabel.Text = "Created a new project. Import an MV tileset folder, PNG files, or ZIP archive.";
            UpdateWindowTitle();
            UpdateUiState();
        }
        catch (Exception ex)
        {
            ShowError("The project could not be created.", ex);
        }
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardOrSave())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Playroma Tile project (*.playromatile.json;*.json)|*.playromatile.json;*.json|All files (*.*)|*.*",
            Title = "Open Playroma Tile Project"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _workspace.Open(dialog.FileName);
            ResetCurrentSheet();
            RefreshProjectTree();
            ValidateProject(saveFirst: false);
            _statusLabel.Text = $"Opened {Path.GetFileName(dialog.FileName)}";
            UpdateWindowTitle();
            UpdateUiState();
        }
        catch (Exception ex)
        {
            ShowError("The project could not be opened.", ex);
        }
    }

    private void SaveProject()
    {
        if (_workspace.Project is null)
        {
            return;
        }

        try
        {
            _workspace.Save();
            _statusLabel.Text = "Project saved.";
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            ShowError("The project could not be saved.", ex);
        }
    }

    private void SaveProjectAs()
    {
        if (_workspace.Project is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Playroma Tile project (*.playromatile.json)|*.playromatile.json|JSON files (*.json)|*.json",
            DefaultExt = "playromatile.json",
            AddExtension = true,
            FileName = Path.GetFileName(_workspace.Project.ProjectFilePath),
            Title = "Save Playroma Tile Project As"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _workspace.SaveAs(dialog.FileName);
            _statusLabel.Text = "Project saved to the new location.";
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            ShowError("The project could not be saved to the new location.", ex);
        }
    }

    private void ImportFolder()
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder containing RPG Maker MV tileset PNG files",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunImport(() => _workspace.ImportFolder(dialog.SelectedPath));
    }

    private void ImportPngFiles()
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PNG tilesheets (*.png)|*.png|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Import RPG Maker MV Tilesheets"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunImport(() => _workspace.ImportFiles(dialog.FileNames));
    }

    private void ImportArchive()
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "Import RPG Maker MV Tileset Archive"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        RunImport(() => _workspace.ImportArchive(dialog.FileName));
    }

    private void RunImport(Func<MvImportResult> importAction)
    {
        try
        {
            UseWaitCursor = true;
            var result = importAction();
            _workspace.Save();
            RefreshProjectTree(result.ImportedSheets.FirstOrDefault()?.Id);
            ValidateProject(saveFirst: false);
            ShowImportResult(result);
            UpdateWindowTitle();
            UpdateUiState();
        }
        catch (Exception ex)
        {
            ShowError("The tileset import failed.", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ExportSelectedTileSet()
    {
        if (_workspace.Project is null || _currentTileSet is null)
        {
            MessageBox.Show(this, "Select a tileset or one of its sheets first.", "Export Tileset",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"Select the export folder for {_currentTileSet.Name}",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _workspace.Save();
            var result = _exportService.ExportTileSet(
                _workspace.Project,
                _currentTileSet,
                dialog.SelectedPath,
                new MvExportOptions { OverwriteExistingFiles = true });
            _statusLabel.Text = $"Exported {result.ExportedFiles.Count} files to {dialog.SelectedPath}";
            MessageBox.Show(this,
                $"Export complete.\n\n{result.ExportedFiles.Count} files written.\n" +
                (result.Warnings.Count == 0 ? "No validation warnings." : $"{result.Warnings.Count} warning(s) were recorded."),
                "Playroma Tile Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("The tileset could not be exported.", ex);
        }
    }

    private void ExportAllTileSets()
    {
        if (_workspace.Project is null || _workspace.Project.TileSets.Count == 0)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a root folder. Each tileset will be exported to its own subfolder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _workspace.Save();
            var result = _exportService.ExportAllTileSets(
                _workspace.Project,
                dialog.SelectedPath,
                new MvExportOptions { OverwriteExistingFiles = true });
            _statusLabel.Text = $"Exported {result.ExportedFiles.Count} files.";
            MessageBox.Show(this, $"Exported {result.ExportedFiles.Count} files.", "Playroma Tile Export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("The tilesets could not be exported.", ex);
        }
    }

    private void HandleTreeSelection()
    {
        switch (_projectTree.SelectedNode?.Tag)
        {
            case TileSetDefinition tileSet:
                _currentTileSet = tileSet;
                break;
            case TileSheetDefinition sheet:
                _currentTileSet = _projectTree.SelectedNode.Parent?.Tag as TileSetDefinition;
                OpenSheet(sheet);
                break;
        }

        UpdateUiState();
    }

    private void OpenSheet(TileSheetDefinition sheet)
    {
        try
        {
            _currentSheet = sheet;
            _currentSession = _workspace.OpenSheet(sheet);
            _canvas.SheetImage = _currentSession.Bitmap;
            UpdateCanvasRegions();
            _statusLabel.Text = $"{sheet.DisplayName}: {_currentSession.Bitmap.Width} × {_currentSession.Bitmap.Height}";
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            ResetCurrentSheet();
            ShowError("The tilesheet could not be opened.", ex);
        }
    }

    private void UpdateCanvasRegions()
    {
        if (_currentSheet is null)
        {
            _canvas.Regions = Array.Empty<MvRegionDefinition>();
            return;
        }

        _canvas.Regions = _selectionMode == CanvasSelectionMode.LogicalAssets
            ? MvLayoutCatalog.GetLogicalRegions(_currentSheet.Slot)
            : MvLayoutCatalog.GetRawTileCells(_currentSheet.Slot);
        _canvas.ShowRegionNumbers = _selectionMode == CanvasSelectionMode.LogicalAssets;
        UpdateRegionPanel();
    }

    private void UpdateRegionPanel()
    {
        var region = _canvas.SelectedRegion;
        if (region is null || _currentSheet is null)
        {
            _metadataPanel.ClearSelection();
            return;
        }

        if (_selectionMode == CanvasSelectionMode.RawCells)
        {
            _metadataPanel.ShowRawCell(region);
            return;
        }

        var metadata = _currentSheet.Regions.FirstOrDefault(item => item.Index == region.Index);
        if (metadata is null)
        {
            metadata = new TileRegionMetadata { Index = region.Index, Name = region.DefaultName };
            _currentSheet.Regions.Add(metadata);
            _workspace.MarkProjectDirty();
        }

        _metadataPanel.ShowLogicalRegion(region, metadata);
    }

    private void SetSelectionMode(CanvasSelectionMode mode)
    {
        if (_selectionMode == mode && _canvas.Regions.Count > 0)
        {
            return;
        }

        _selectionMode = mode;
        _modeCombo.SelectedIndex = mode == CanvasSelectionMode.RawCells ? 1 : 0;
        UpdateCanvasRegions();
    }

    private void CopySelection()
    {
        if (!TryGetEditableSelection(out var session, out var region, requireUnlocked: false))
        {
            return;
        }

        _internalClipboard?.Dispose();
        _internalClipboard = RegionImageEditor.Extract(session.Bitmap, region.Bounds);
        try
        {
            Clipboard.SetImage((Bitmap)_internalClipboard.Clone());
        }
        catch
        {
            // The internal clipboard remains available if the Windows clipboard is busy.
        }

        _statusLabel.Text = $"Copied {region.Bounds.Width} × {region.Bounds.Height} px region.";
    }

    private void CutSelection()
    {
        if (!TryGetEditableSelection(out _, out _, requireUnlocked: true))
        {
            return;
        }

        CopySelection();
        ClearSelection();
    }

    private void PasteSelection()
    {
        if (!TryGetEditableSelection(out var session, out var region, requireUnlocked: true))
        {
            return;
        }

        Image? source = null;
        var disposeSource = false;
        try
        {
            if (_internalClipboard is not null)
            {
                source = _internalClipboard;
            }
            else if (Clipboard.ContainsImage())
            {
                source = Clipboard.GetImage();
                disposeSource = source is not null;
            }

            if (source is null)
            {
                MessageBox.Show(this, "The image clipboard is empty.", "Paste Region",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var scale = false;
            if (source.Width != region.Bounds.Width || source.Height != region.Bounds.Height)
            {
                var answer = MessageBox.Show(this,
                    $"The copied image is {source.Width} × {source.Height} px, while the selection is " +
                    $"{region.Bounds.Width} × {region.Bounds.Height} px.\n\nScale it using nearest-neighbour sampling?",
                    "Paste Region", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                scale = true;
            }

            session.CaptureBeforeEdit();
            RegionImageEditor.Paste(session.Bitmap, region.Bounds, source, scale);
            session.MarkEdited();
            RefreshCanvasAfterImageEdit();
            _statusLabel.Text = "Region pasted.";
        }
        catch (Exception ex)
        {
            ShowError("The image could not be pasted.", ex);
        }
        finally
        {
            if (disposeSource)
            {
                source?.Dispose();
            }
        }
    }

    private void ClearSelection()
    {
        if (!TryGetEditableSelection(out var session, out var region, requireUnlocked: true))
        {
            return;
        }

        session.CaptureBeforeEdit();
        RegionImageEditor.Clear(session.Bitmap, region.Bounds);
        session.MarkEdited();
        RefreshCanvasAfterImageEdit();
        _statusLabel.Text = "Region cleared to transparency.";
    }

    private void ImportImageIntoSelection()
    {
        if (!TryGetEditableSelection(out var session, out var region, requireUnlocked: true))
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.bmp;*.jpg;*.jpeg)|*.png;*.bmp;*.jpg;*.jpeg|All files (*.*)|*.*",
            Title = "Import Image into Selected Region"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var source = Image.FromFile(dialog.FileName);
            var scale = false;
            if (source.Width != region.Bounds.Width || source.Height != region.Bounds.Height)
            {
                var answer = MessageBox.Show(this,
                    $"The selected image is {source.Width} × {source.Height} px. The target region is " +
                    $"{region.Bounds.Width} × {region.Bounds.Height} px.\n\nScale it with nearest-neighbour sampling?",
                    "Import Region Image", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                scale = true;
            }

            session.CaptureBeforeEdit();
            RegionImageEditor.Paste(session.Bitmap, region.Bounds, source, scale);
            session.MarkEdited();
            RefreshCanvasAfterImageEdit();
            _statusLabel.Text = "Image imported into the selected region.";
        }
        catch (Exception ex)
        {
            ShowError("The image could not be imported.", ex);
        }
    }

    private void ExportSelectionImage()
    {
        if (!TryGetEditableSelection(out var session, out var region, requireUnlocked: false))
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = "png",
            AddExtension = true,
            FileName = $"{_currentSheet!.DisplayName}_{region.Index + 1:000}.png",
            Title = "Export Selected Region"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var extracted = RegionImageEditor.Extract(session.Bitmap, region.Bounds);
            extracted.Save(dialog.FileName, ImageFormat.Png);
            _statusLabel.Text = $"Exported {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ShowError("The selected region could not be exported.", ex);
        }
    }

    private void Undo()
    {
        if (_currentSession?.Undo() == true)
        {
            _canvas.SheetImage = _currentSession.Bitmap;
            UpdateCanvasRegions();
            _statusLabel.Text = "Undid image edit.";
            UpdateWindowTitle();
        }
    }

    private void Redo()
    {
        if (_currentSession?.Redo() == true)
        {
            _canvas.SheetImage = _currentSession.Bitmap;
            UpdateCanvasRegions();
            _statusLabel.Text = "Redid image edit.";
            UpdateWindowTitle();
        }
    }

    private bool TryGetEditableSelection(
        out SheetSession session,
        out MvRegionDefinition region,
        bool requireUnlocked)
    {
        session = null!;
        region = null!;
        if (_currentSession is null || _currentSheet is null || _canvas.SelectedRegion is null)
        {
            return false;
        }

        session = _currentSession;
        region = _canvas.SelectedRegion;
        if (requireUnlocked && IsSelectionLocked(region.Bounds))
        {
            MessageBox.Show(this,
                "This selection intersects a locked logical asset. Unlock it in the metadata panel before editing its pixels.",
                "Region Locked", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private bool IsSelectionLocked(Rectangle bounds)
    {
        if (_currentSheet is null)
        {
            return false;
        }

        var logicalRegions = MvLayoutCatalog.GetLogicalRegions(_currentSheet.Slot);
        return logicalRegions.Any(region =>
            region.Bounds.IntersectsWith(bounds) &&
            _currentSheet.Regions.FirstOrDefault(metadata => metadata.Index == region.Index)?.IsLocked == true);
    }

    private void RefreshCanvasAfterImageEdit()
    {
        _canvas.Invalidate();
        _workspace.MarkProjectDirty();
        UpdateWindowTitle();
    }

    private void ValidateProject(bool saveFirst = true)
    {
        if (_workspace.Project is null)
        {
            return;
        }

        try
        {
            if (saveFirst)
            {
                _workspace.Save();
            }

            var issues = _validator.ValidateProject(_workspace.Project).ToList();
            foreach (var materialIssue in new MaterialProjectValidator().Validate(_workspace.Project))
            {
                var severity = materialIssue.Severity switch
                {
                    MaterialIssueSeverity.Error => ValidationSeverity.Error,
                    MaterialIssueSeverity.Warning => ValidationSeverity.Warning,
                    _ => ValidationSeverity.Info
                };
                issues.Add(new MvValidationIssue(severity, materialIssue.Message, $"Material: {materialIssue.MaterialName}"));
            }
            foreach (var tileSet in _workspace.Project.TileSets)
            {
                var constructionReference = tileSet.ConstructionId is Guid constructionId
                    ? _workspace.Project.TilesetConstructions.FirstOrDefault(x => x.Id == constructionId)
                    : null;
                if (constructionReference is null || !File.Exists(ProjectPath.Resolve(_workspace.Project, constructionReference.RelativeDefinitionPath))) continue;
                try
                {
                    var construction = TilesetSerializer.Load(_workspace.Project, constructionReference);
                    var phase4Issues = new Playroma.Tile.Tilesets.Validation.TilesetValidator().Validate(_workspace.Project, tileSet, construction);
                    foreach (var phase4Issue in phase4Issues)
                    {
                        var severity = phase4Issue.Severity switch
                        {
                            TilesetIssueSeverity.Error => ValidationSeverity.Error,
                            TilesetIssueSeverity.Warning => ValidationSeverity.Warning,
                            _ => ValidationSeverity.Info
                        };
                        var sheetId = phase4Issue.Sheet.HasValue
                            ? tileSet.Sheets.FirstOrDefault(x => x.Slot == phase4Issue.Sheet.Value)?.Id
                            : null;
                        issues.Add(new MvValidationIssue(severity, phase4Issue.Message, tileSet.Name, sheetId, phase4Issue.CellIndex));
                    }
                }
                catch (Exception ex)
                {
                    issues.Add(new MvValidationIssue(ValidationSeverity.Error,
                        $"Tileset construction could not be validated: {ex.Message}", tileSet.Name));
                }
            }
            PopulateValidationList(issues);
            var errors = issues.Count(issue => issue.Severity == ValidationSeverity.Error);
            var warnings = issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
            _statusLabel.Text = errors == 0
                ? $"Validation complete: {warnings} warning(s), no errors."
                : $"Validation found {errors} error(s) and {warnings} warning(s).";
            UpdateWindowTitle();
        }
        catch (Exception ex)
        {
            ShowError("Validation could not be completed.", ex);
        }
    }

    private void PopulateValidationList(IReadOnlyList<MvValidationIssue> issues)
    {
        _validationList.BeginUpdate();
        try
        {
            _validationList.Items.Clear();
            foreach (var issue in issues.OrderByDescending(issue => issue.Severity))
            {
                var sheetText = string.Empty;
                if (issue.SheetId is Guid id && _workspace.Project is not null)
                {
                    sheetText = _workspace.Project.TileSets
                        .SelectMany(set => set.Sheets)
                        .FirstOrDefault(sheet => sheet.Id == id)?.Slot.ToString() ?? string.Empty;
                }

                var item = new ListViewItem(issue.Severity.ToString()) { Tag = issue };
                item.SubItems.Add(issue.TileSetName);
                item.SubItems.Add(sheetText);
                item.SubItems.Add(issue.Message);
                _validationList.Items.Add(item);
            }
        }
        finally
        {
            _validationList.EndUpdate();
        }
    }

    private void NavigateToValidationIssue()
    {
        if (_validationList.SelectedItems.Count == 0 ||
            _validationList.SelectedItems[0].Tag is not MvValidationIssue issue ||
            issue.SheetId is not Guid sheetId ||
            !_sheetNodes.TryGetValue(sheetId, out var node))
        {
            return;
        }

        _projectTree.SelectedNode = node;
        node.EnsureVisible();
        if (issue.RegionIndex is int regionIndex)
        {
            SetSelectionMode(CanvasSelectionMode.LogicalAssets);
            _canvas.SelectRegionByLogicalIndex(regionIndex);
        }
    }

    private void RefreshProjectTree(Guid? selectSheetId = null)
    {
        _projectTree.BeginUpdate();
        try
        {
            _projectTree.Nodes.Clear();
            _sheetNodes.Clear();
            if (_workspace.Project is null)
            {
                return;
            }

            var root = new TreeNode(_workspace.Project.Name) { Tag = _workspace.Project };
            _projectTree.Nodes.Add(root);
            foreach (var tileSet in _workspace.Project.TileSets.OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase))
            {
                var setNode = new TreeNode(tileSet.Name) { Tag = tileSet };
                root.Nodes.Add(setNode);
                foreach (var sheet in tileSet.Sheets.OrderBy(sheet => sheet.Slot))
                {
                    var sheetNode = new TreeNode(CreateSheetNodeText(sheet)) { Tag = sheet };
                    setNode.Nodes.Add(sheetNode);
                    _sheetNodes[sheet.Id] = sheetNode;
                }

                setNode.Expand();
            }

            root.Expand();
            if (selectSheetId is Guid id && _sheetNodes.TryGetValue(id, out var selected))
            {
                _projectTree.SelectedNode = selected;
                selected.EnsureVisible();
            }
            else if (root.Nodes.Count > 0)
            {
                _projectTree.SelectedNode = root.Nodes[0];
            }
        }
        finally
        {
            _projectTree.EndUpdate();
        }
    }

    private void UpdateTreeSheetText()
    {
        if (_currentSheet is not null && _sheetNodes.TryGetValue(_currentSheet.Id, out var node))
        {
            node.Text = CreateSheetNodeText(_currentSheet);
        }
    }

    private static string CreateSheetNodeText(TileSheetDefinition sheet) =>
        $"{sheet.Slot} — {sheet.DisplayName}";

    private void BeginRenameTileSet()
    {
        var node = _projectTree.SelectedNode;
        if (node?.Tag is TileSheetDefinition)
        {
            node = node.Parent;
        }

        if (node?.Tag is TileSetDefinition)
        {
            node.BeginEdit();
        }
    }

    private void ResetCurrentSheet()
    {
        _currentSession = null;
        _currentSheet = null;
        _currentTileSet = null;
        _canvas.SheetImage = null;
        _canvas.Regions = Array.Empty<MvRegionDefinition>();
        _metadataPanel.ClearSelection();
        _validationList.Items.Clear();
    }

    private void UpdateUiState()
    {
        var projectOpen = _workspace.Project is not null;
        _projectTree.Enabled = projectOpen;
        _modeCombo.Enabled = _currentSheet is not null;
        _zoomCombo.Enabled = _currentSheet is not null;
        _gridButton.Enabled = _currentSheet is not null;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var projectName = _workspace.Project?.Name ?? "No project";
        var dirty = _workspace.HasUnsavedChanges ? " *" : string.Empty;
        var sheet = _currentSheet is null ? string.Empty : $" — {_currentSheet.DisplayName}";
        Text = $"Playroma Tile — {projectName}{sheet}{dirty}";
    }

    private bool EnsureProjectOpen()
    {
        if (_workspace.Project is not null)
        {
            return true;
        }

        var answer = MessageBox.Show(this,
            "Create a Playroma Tile project before importing assets. Create one now?",
            "No Project Open", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            NewProject();
        }

        return _workspace.Project is not null;
    }

    private bool ConfirmDiscardOrSave()
    {
        if (!_workspace.HasUnsavedChanges)
        {
            return true;
        }

        var answer = MessageBox.Show(this,
            "The current project has unsaved changes. Save them before continuing?",
            "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel)
        {
            return false;
        }

        if (answer == DialogResult.Yes)
        {
            SaveProject();
            return !_workspace.HasUnsavedChanges;
        }

        return true;
    }

    private void ShowImportResult(MvImportResult result)
    {
        var message = $"Imported {result.ImportedSheets.Count} sheet(s).";
        if (result.Warnings.Count > 0)
        {
            message += $"\n\nWarnings ({result.Warnings.Count}):\n" +
                       string.Join("\n", result.Warnings.Take(12).Select(warning => "• " + warning));
        }

        if (result.SkippedFiles.Count > 0)
        {
            message += $"\n\nSkipped ({result.SkippedFiles.Count}):\n" +
                       string.Join("\n", result.SkippedFiles.Take(12).Select(file => "• " + file));
        }

        if (result.Warnings.Count > 12 || result.SkippedFiles.Count > 12)
        {
            message += "\n\nAdditional messages were omitted from this summary.";
        }

        MessageBox.Show(this, message, "MV Import Complete", MessageBoxButtons.OK,
            result.SkippedFiles.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void OpenAutotileWorkshop()
    {
        if (_workspace.Project is null)
        {
            MessageBox.Show(this, "Create or open a Playroma Tile project first.", "Autotile Compiler",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int? logicalRegionIndex = null;
        if (_selectionMode == CanvasSelectionMode.LogicalAssets &&
            _currentSheet?.Slot is MvSheetSlot.A1 or MvSheetSlot.A2 or MvSheetSlot.A3 or MvSheetSlot.A4)
        {
            logicalRegionIndex = _canvas.SelectedRegion?.Index;
        }

        using var workshop = new AutotileWorkshopForm(
            _workspace, _currentSession, _currentTileSet, _currentSheet, logicalRegionIndex);
        workshop.ShowDialog(this);
        _canvas.Invalidate();
        UpdateRegionPanel();
        UpdateTreeSheetText();
        UpdateWindowTitle();
    }


    private void OpenMaterialLaboratory()
    {
        if (_workspace.Project is null)
        {
            MessageBox.Show(this, "Create or open a Playroma Tile project first.", "Material Laboratory",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var laboratory = new MaterialLaboratoryForm(_workspace);
        laboratory.ShowDialog(this);
        UpdateWindowTitle();
    }

    private void OpenTilesetConstruction()
    {
        if (_workspace.Project is null)
        {
            MessageBox.Show(this, "Create or open a Playroma Tile project first.", "Tileset Construction",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var tileSet = _currentTileSet ?? _workspace.Project.TileSets.FirstOrDefault();
        if (tileSet is null)
        {
            MessageBox.Show(this, "Import or create an RPG Maker MV tileset first.", "Tileset Construction",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var studio = new TilesetConstructionStudioForm(_workspace, tileSet);
        studio.ShowDialog(this);
        RefreshProjectTree(_currentSheet?.Id);
        if (_currentSheet is not null)
        {
            _currentSession = _workspace.OpenSheet(_currentSheet);
            _canvas.SheetImage = _currentSession.Bitmap;
            UpdateCanvasRegions();
        }
        UpdateRegionPanel();
        UpdateTreeSheetText();
        UpdateWindowTitle();
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "Playroma Tile — Tileset Construction\n\n" +
            "A complete RPG Maker MV tileset production environment with procedural materials, exact autotiles, " +
            "ordinary objects, metadata painting, full test maps, assisted construction, validation, project patching, and publication export.\n\n" +
            "Built with .NET 8 and Windows Forms; no external runtime packages are required.",
            "About Playroma Tile", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (!EnsureProjectOpen() || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        try
        {
            var aggregate = new MvImportResult();
            foreach (var directory in paths.Where(Directory.Exists))
            {
                MergeImportResults(aggregate, _workspace.ImportFolder(directory));
            }

            foreach (var archive in paths.Where(path => File.Exists(path) &&
                         string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)))
            {
                MergeImportResults(aggregate, _workspace.ImportArchive(archive));
            }

            var pngs = paths.Where(path => File.Exists(path) &&
                                           string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase));
            MergeImportResults(aggregate, _workspace.ImportFiles(pngs));
            _workspace.Save();
            RefreshProjectTree(aggregate.ImportedSheets.FirstOrDefault()?.Id);
            ValidateProject(saveFirst: false);
            ShowImportResult(aggregate);
        }
        catch (Exception ex)
        {
            ShowError("The dropped files could not be imported.", ex);
        }
    }

    private static void MergeImportResults(MvImportResult destination, MvImportResult source)
    {
        destination.ImportedSheets.AddRange(source.ImportedSheets);
        destination.Warnings.AddRange(source.Warnings);
        destination.SkippedFiles.AddRange(source.SkippedFiles);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_hostClosePrepared) return;
        if (!ConfirmDiscardOrSave())
        {
            e.Cancel = true;
        }
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }

        item.Click += handler;
        return item;
    }

    private static ToolStripButton CreateToolButton(string text, EventHandler handler)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += handler;
        return button;
    }

    private static void AdjustVerticalSplit(SplitContainer split, int desiredPanel2Width)
    {
        if (split.ClientSize.Width <= 0)
        {
            return;
        }

        var minimum = split.Panel1MinSize;
        var maximum = split.ClientSize.Width - split.SplitterWidth - split.Panel2MinSize;
        if (maximum < minimum)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(
            split.ClientSize.Width - split.SplitterWidth - desiredPanel2Width,
            minimum,
            maximum);
    }

    private static void AdjustHorizontalSplit(SplitContainer split, int desiredPanel2Height)
    {
        if (split.ClientSize.Height <= 0)
        {
            return;
        }

        var minimum = split.Panel1MinSize;
        var maximum = split.ClientSize.Height - split.SplitterWidth - split.Panel2MinSize;
        if (maximum < minimum)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(
            split.ClientSize.Height - split.SplitterWidth - desiredPanel2Height,
            minimum,
            maximum);
    }

    private void ShowError(string heading, Exception exception)
    {
        MessageBox.Show(this, $"{heading}\n\n{exception.Message}", "Playroma Tile",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        _statusLabel.Text = heading;
    }
}
