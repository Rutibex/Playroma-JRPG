using System.Diagnostics;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Assets;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Editor.WinForms.Forms;
using Playroma.JRPG.Editor.WinForms.ResourcesUI;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>
/// Resource Manager browser surface. It reuses the project-wide catalogue, overlay and transaction
/// system, while keeping search, selection, preview and thumbnail work local to its owning window.
/// </summary>
public sealed class AssetInventoryDocumentControl : UserControl, IAsyncDisposable
{
    private readonly ISelectionService _selection;
    private readonly MvProjectSession _session;
    private readonly WinFormsThemeService? _theme;
    private readonly AssetPreviewControl _preview;
    private readonly AssetThumbnailService _thumbnails;
    private readonly DataGridView _details = ControlHelpers.CreateReadOnlyGrid();
    private readonly ListView _thumbnailView = new()
    {
        Dock = DockStyle.Fill,
        View = View.LargeIcon,
        MultiSelect = false,
        HideSelection = false,
        ShowItemToolTips = true,
        AccessibleName = "Resource thumbnails",
        AccessibleDescription = "Use arrow keys to select a resource. Enter opens it externally, F2 renames, and Delete deletes it.",
        AccessibleRole = AccessibleRole.List
    };
    private readonly ImageList _thumbnailImages = new() { ImageSize = new Size(112, 112), ColorDepth = ColorDepth.Depth32Bit };
    private readonly TreeView _categories = new() { Dock = DockStyle.Fill, HideSelection = false, AccessibleName = "Resource categories", AccessibleDescription = "Choose All Assets, a resource category, or a smart collection." };
    private readonly TextBox _search = new() { PlaceholderText = "Search assets or use category:characters used:true missing:true…", Width = 360, AccessibleName = "Search resources" };
    private readonly ComboBox _viewMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, AccessibleName = "Resource view mode" };
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(10, 8, 0, 0) };
    private readonly Button _import = new() { Text = "Import &Files…", AutoSize = true };
    private readonly Button _importFolder = new() { Text = "Import F&older…", AutoSize = true };
    private readonly Button _replace = new() { Text = "&Replace…", AutoSize = true };
    private readonly Button _rename = new() { Text = "Rena&me…", AutoSize = true };
    private readonly Button _delete = new() { Text = "&Delete…", AutoSize = true };
    private readonly Button _export = new() { Text = "&Export…", AutoSize = true };
    private readonly Button _open = new() { Text = "&Open", AutoSize = true };
    private readonly Button _reveal = new() { Text = "Re&veal", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Re&fresh", AutoSize = true };
    private readonly Panel _assetHost = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _categorySplit;
    private readonly SplitContainer _contentSplit;
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 220 };
    private readonly object _rebuildSync = new();
    private readonly HashSet<string> _loadedThumbnailKeys = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AssetRecord> _visible = [];
    private ResourceBrowseFilter _browseFilter = ResourceBrowseFilter.All;
    private CancellationTokenSource? _rebuildCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private Task _activeRebuildTask = Task.CompletedTask;
    private Task _activeThumbnailTask = Task.CompletedTask;
    private Task? _resourceDisposeTask;
    private AssetKey? _pendingSelectionKey;
    private string? _pendingRelativePath;
    private string? _pendingPreviewTab;
    private string _sortColumn = "Name";
    private bool _sortDescending;
    private bool _disposed;
    private bool _suppressTreeSelection;

    public AssetInventoryDocumentControl(
        IWorkspaceService workspace,
        ISelectionService selection,
        WinFormsThemeService? theme = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _theme = theme;
        _session = workspace.CurrentSession ?? throw new InvalidOperationException("No project is open.");
        _preview = new AssetPreviewControl(_session);
        _preview.SelectedVariantChanged += (_, _) => UpdateCommandStates();
        AssetCache cache = new(_session.Workspace.Snapshot.Identity.ProjectRoot);
        _thumbnails = new AssetThumbnailService(cache, [new WindowsImageThumbnailProvider(_session.AssetContent)]);
        Dock = DockStyle.Fill;
        AllowDrop = true;
        AccessibilityMetadata.Name(this, "Resource browser", "Browse resource categories, select project assets, preview them, and perform transactional imports or replacements.", AccessibleRole.Pane);
        _details.AccessibleName = "Resource details";
        _details.AccessibleDescription = "Project resources shown in a sortable details table.";
        _preview.AccessibleName = "Resource preview";

        BuildCategoryTree();
        ConfigureDetailsGrid();
        ConfigureThumbnails();

        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(4)
        };
        TableLayoutPanel filters = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.Controls.Add(AccessibilityMetadata.CreateLabel("&Search:", _search), 0, 0);
        filters.Controls.Add(_search, 1, 0);
        filters.Controls.Add(AccessibilityMetadata.CreateLabel("&View:", _viewMode), 2, 0);
        filters.Controls.Add(_viewMode, 3, 0);
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        _viewMode.Items.AddRange(["Details", "Small thumbnails", "Large thumbnails"]);
        _viewMode.SelectedIndex = 0;
        actions.Controls.AddRange([_import, _importFolder, _replace, _rename, _delete, _export, _open, _reveal, _refresh, _summary]);
        toolbar.Controls.Add(filters, 0, 0);
        toolbar.Controls.Add(actions, 0, 1);

        _categorySplit = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 215, 150, 500);
        _contentSplit = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 590, 320, 300);
        _categorySplit.Panel1.Controls.Add(_categories);
        _contentSplit.Panel1.Controls.Add(_assetHost);
        _contentSplit.Panel2.Controls.Add(_preview);
        _categorySplit.Panel2.Controls.Add(_contentSplit);
        Controls.Add(_categorySplit);
        Controls.Add(toolbar);

        WireEvents();
        SwitchView();
        QueueRebuild();
    }

    public event EventHandler? CommandStateChanged;
    public event EventHandler? ViewStateChanged;

    public AssetRecord? CurrentRecord => SelectedRecord();
    public AssetVariant? CurrentVariant => SelectedVariant();
    public AssetCategory SuggestedImportCategory => _browseFilter.Category ?? AssetCategory.Unknown;
    public bool CanImport => !_session.State.IsReadOnly;
    public bool CanReplace => _replace.Enabled;
    public bool CanRename => _rename.Enabled;
    public bool CanDelete => _delete.Enabled;
    public bool CanExport => _export.Enabled;
    public bool CanOpenExternally => _open.Enabled;
    public bool CanReveal => _reveal.Enabled;

    public ResourceNavigationTarget CaptureNavigationTarget()
    {
        AssetRecord? selected = SelectedRecord();
        return selected is not null
            ? new ResourceNavigationTarget(selected.Key.Category, selected.Key.LogicalName, PreviewTab: _preview.SelectedTabName)
            : new ResourceNavigationTarget(
                Category: _browseFilter.Category,
                SmartCollection: _browseFilter.SmartCollection,
                PreviewTab: _preview.SelectedTabName);
    }

    public ResourceManagerViewState CaptureViewState()
    {
        AssetRecord? selected = SelectedRecord();
        return new ResourceManagerViewState(
            SelectedCategory: _categories.SelectedNode?.Name ?? "All",
            SearchText: _search.Text,
            ViewMode: Convert.ToString(_viewMode.SelectedItem) ?? "Details",
            SortColumn: _sortColumn,
            SortDescending: _sortDescending,
            SelectedAsset: selected is null ? null : selected.Key.ToString(),
            SelectedPreviewTab: _preview.SelectedTabName,
            CategoryPanelWidth: _categorySplit.SplitterDistance,
            PreviewPanelWidth: Math.Max(300, _contentSplit.ClientSize.Width - _contentSplit.SplitterDistance - _contentSplit.SplitterWidth),
            ThumbnailSize: _thumbnailImages.ImageSize.Width,
            ExpandedGroups: _categories.Nodes.Cast<TreeNode>().Where(node => node.IsExpanded).Select(node => node.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray());
    }

    public void RestoreViewState(ResourceManagerWindowLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _search.Text = settings.SearchText ?? string.Empty;
        _sortColumn = string.IsNullOrWhiteSpace(settings.SortColumn) ? "Name" : settings.SortColumn;
        _sortDescending = settings.SortDescending;
        int viewIndex = _viewMode.Items.Cast<object>().Select(item => Convert.ToString(item)).ToList()
            .FindIndex(item => string.Equals(item, settings.ViewMode, StringComparison.OrdinalIgnoreCase));
        _viewMode.SelectedIndex = viewIndex >= 0 ? viewIndex : 0;
        TreeNode? node = FindNodeByName(settings.SelectedCategory);
        if (node is not null)
        {
            _suppressTreeSelection = true;
            try { _categories.SelectedNode = node; _browseFilter = node.Tag as ResourceBrowseFilter ?? ResourceBrowseFilter.All; }
            finally { _suppressTreeSelection = false; }
        }
        foreach (TreeNode root in _categories.Nodes)
        {
            if (settings.EffectiveExpandedGroups.Contains(root.Name, StringComparer.OrdinalIgnoreCase)) root.Expand();
            else if (root.Nodes.Count > 0) root.Collapse();
        }
        _pendingPreviewTab = settings.SelectedPreviewTab;
        if (TryParseAssetKey(settings.SelectedAsset, out AssetKey selected)) _pendingSelectionKey = selected;

        void ApplySavedBounds()
        {
            if (_disposed) return;
            ControlHelpers.SetSplitterDistanceSafe(_categorySplit, settings.CategoryPanelWidth);
            int contentDistance = _contentSplit.ClientSize.Width - settings.PreviewPanelWidth - _contentSplit.SplitterWidth;
            ControlHelpers.SetSplitterDistanceSafe(_contentSplit, contentDistance);
        }
        if (IsHandleCreated) TryPostToUi(ApplySavedBounds);
        else
        {
            EventHandler? applyBounds = null;
            applyBounds = (_, _) =>
            {
                HandleCreated -= applyBounds;
                TryPostToUi(ApplySavedBounds);
            };
            HandleCreated += applyBounds;
        }
        QueueRebuild();
    }

    public async ValueTask NavigateAsync(ResourceNavigationTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        _pendingSelectionKey = target.Category.HasValue && !string.IsNullOrWhiteSpace(target.LogicalName)
            ? new AssetKey(target.Category.Value, target.LogicalName)
            : null;
        _pendingRelativePath = target.RelativePath;
        _pendingPreviewTab = target.PreviewTab;

        TreeNode? node = target.Category.HasValue
            ? FindNodeByName("Category." + target.Category.Value)
            : target.SmartCollection.HasValue
                ? FindNodeByName("Smart." + target.SmartCollection.Value)
                : FindNodeByName("All");
        if (node is not null)
        {
            _suppressTreeSelection = true;
            try
            {
                _categories.SelectedNode = node;
                node.EnsureVisible();
                _browseFilter = node.Tag as ResourceBrowseFilter ?? ResourceBrowseFilter.All;
            }
            finally { _suppressTreeSelection = false; }
        }
        QueueRebuild();
        await _activeRebuildTask.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task ImportFilesAsync(IEnumerable<string>? initialFiles = null) => ShowImportWizardAsync(initialFiles);

    public async Task ImportFolderAsync()
    {
        using FolderBrowserDialog folder = new()
        {
            Description = "Select a folder containing assets. All supported files in subfolders will be included.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        await ShowImportWizardAsync(AssetImportPlanner.EnumerateSupportedFiles(folder.SelectedPath, recursive: true)).ConfigureAwait(true);
    }

    public Task ReplaceSelectedAsync() => ReplaceAsync();
    public Task RenameSelectedAsync() => RenameAsync();
    public Task DeleteSelectedAsync() => DeleteAsync();
    public Task ExportSelectedAsync() => ExportAsync();
    public Task RefreshCatalogueAsync() => RefreshAsync();
    public void OpenSelectedExternally() => OpenSelectedExternallyCore();
    public void RevealSelected() => RevealSelectedCore();
    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) BeginDispose();
        base.Dispose(disposing);
    }

    public async ValueTask DisposeAsync()
    {
        BeginDispose();
        if (_resourceDisposeTask is not null) await _resourceDisposeTask.ConfigureAwait(true);
        GC.SuppressFinalize(this);
    }

    private void BeginDispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Assets.Changed -= OnCatalogueChanged;
        _session.StateChanged -= OnSessionStateChanged;
        _searchDebounce.Stop();
        _searchDebounce.Dispose();
        _rebuildCancellation?.Cancel();
        _thumbnailCancellation?.Cancel();
        _resourceDisposeTask = DisposeResourcesAsync();
    }

    private async Task DisposeResourcesAsync()
    {
        try { await _activeRebuildTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        try { await _activeThumbnailTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        _rebuildCancellation?.Dispose();
        _thumbnailCancellation?.Dispose();
        await _thumbnails.DisposeAsync().ConfigureAwait(false);
        _thumbnailImages.Dispose();
    }

    private void WireEvents()
    {
        _categories.AfterSelect += (_, e) =>
        {
            if (_suppressTreeSelection) return;
            _browseFilter = e.Node?.Tag as ResourceBrowseFilter ?? ResourceBrowseFilter.All;
            QueueRebuild();
            ViewStateChanged?.Invoke(this, EventArgs.Empty);
        };
        _search.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); QueueRebuild(); ViewStateChanged?.Invoke(this, EventArgs.Empty); };
        _viewMode.SelectedIndexChanged += (_, _) => { SwitchView(); ViewStateChanged?.Invoke(this, EventArgs.Empty); };
        _details.SelectionChanged += async (_, _) => await SelectFromDetailsAsync().ConfigureAwait(true);
        _details.CellDoubleClick += (_, _) => OpenSelectedExternallyCore();
        _details.ColumnHeaderMouseClick += (_, e) => ChangeSort(_details.Columns[e.ColumnIndex].Name);
        _thumbnailView.SelectedIndexChanged += async (_, _) => await SelectFromThumbnailsAsync().ConfigureAwait(true);
        _thumbnailView.DoubleClick += (_, _) => OpenSelectedExternallyCore();
        _thumbnailView.MouseWheel += (_, _) => TryPostToUi(QueueVisibleThumbnails);
        _thumbnailView.Resize += (_, _) => QueueVisibleThumbnails();
        _thumbnailView.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenSelectedExternallyCore(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { _ = DeleteAsync(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { _ = RenameAsync(); e.Handled = true; }
        };
        _import.Click += async (_, _) => await ShowImportWizardAsync().ConfigureAwait(true);
        _importFolder.Click += async (_, _) => await ImportFolderAsync().ConfigureAwait(true);
        _replace.Click += async (_, _) => await ReplaceAsync().ConfigureAwait(true);
        _rename.Click += async (_, _) => await RenameAsync().ConfigureAwait(true);
        _delete.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        _export.Click += async (_, _) => await ExportAsync().ConfigureAwait(true);
        _open.Click += (_, _) => OpenSelectedExternallyCore();
        _reveal.Click += (_, _) => RevealSelectedCore();
        _refresh.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _session.Assets.Changed += OnCatalogueChanged;
        _session.StateChanged += OnSessionStateChanged;
    }

    private void BuildCategoryTree()
    {
        _categories.BeginUpdate();
        try
        {
            _categories.Nodes.Clear();
            TreeNode all = new("All Assets") { Name = "All", Tag = ResourceBrowseFilter.All };
            _categories.Nodes.Add(all);
            TreeNode images = new("Images") { Name = "Images" };
            TreeNode audio = new("Audio") { Name = "Audio" };
            TreeNode other = new("Other Media") { Name = "Other Media" };
            foreach (AssetCategory category in Enum.GetValues<AssetCategory>().Where(category => category != AssetCategory.Unknown))
            {
                TreeNode node = new(FormatCategory(category))
                {
                    Name = "Category." + category,
                    Tag = new ResourceBrowseFilter(Category: category)
                };
                switch (category)
                {
                    case AssetCategory.BackgroundMusic:
                    case AssetCategory.BackgroundSounds:
                    case AssetCategory.MusicEffects:
                    case AssetCategory.SoundEffects:
                        audio.Nodes.Add(node);
                        break;
                    case AssetCategory.Fonts:
                    case AssetCategory.Movies:
                        other.Nodes.Add(node);
                        break;
                    default:
                        images.Nodes.Add(node);
                        break;
                }
            }
            _categories.Nodes.Add(images);
            _categories.Nodes.Add(audio);
            _categories.Nodes.Add(other);
            AddSmartNode("Missing", ResourceSmartCollection.Missing);
            AddSmartNode("No Known References", ResourceSmartCollection.NoKnownReferences);
            AddSmartNode("Encrypted", ResourceSmartCollection.Encrypted);
            AddSmartNode("Duplicates", ResourceSmartCollection.Duplicates);
            AddSmartNode("Pending Changes", ResourceSmartCollection.PendingChanges);
            AddSmartNode("Invalid / Unsupported", ResourceSmartCollection.InvalidOrUnsupported);
            AddSmartNode("Warnings", ResourceSmartCollection.Warnings);
            AddSmartNode("Recently Modified", ResourceSmartCollection.RecentlyModified);
            images.Expand();
            audio.Expand();
            other.Expand();
            _categories.SelectedNode = all;
        }
        finally { _categories.EndUpdate(); }
    }

    private void AddSmartNode(string text, ResourceSmartCollection smart) =>
        _categories.Nodes.Add(new TreeNode(text) { Name = "Smart." + smart, Tag = new ResourceBrowseFilter(SmartCollection: smart) });

    private void ConfigureDetailsGrid()
    {
        _details.VirtualMode = true;
        _details.Columns.Add("Name", "Name");
        _details.Columns.Add("Category", "Category");
        _details.Columns.Add("Media", "Media");
        _details.Columns.Add("Variants", "Variants");
        _details.Columns.Add("Metadata", "Dimensions / Duration");
        _details.Columns.Add("Size", "Size");
        _details.Columns.Add("Exact", "Exact");
        _details.Columns.Add("Heuristic", "Heuristic");
        _details.Columns.Add("Status", "Status");
        _details.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _details.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
            AssetRecord asset = _visible[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => asset.Key.LogicalName,
                1 => FormatCategory(asset.Key.Category),
                2 => asset.MediaKind,
                3 => string.Join(", ", asset.Variants.Select(variant => variant.Extension.TrimStart('.')).Distinct(StringComparer.OrdinalIgnoreCase)),
                4 => FormatMetadata(asset.Metadata),
                5 => ControlHelpers.FormatBytes(asset.TotalBytes),
                6 => asset.Usage.ExactReferenceCount,
                7 => asset.Usage.HeuristicReferenceCount,
                8 => asset.Status,
                _ => string.Empty
            };
        };
        _details.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visible.Count) return;
            AssetRecord asset = _visible[e.RowIndex];
            _details.Rows[e.RowIndex].DefaultCellStyle.ForeColor = asset.IsMissing
                ? Color.Firebrick
                : asset.Status == AssetStatus.PendingDeletion
                    ? Color.Gray
                    : _details.ForeColor;
        };
    }

    private void ConfigureThumbnails()
    {
        _thumbnailView.LargeImageList = _thumbnailImages;
        _thumbnailImages.Images.Add("image", CreateMediaPlaceholder("IMG"));
        _thumbnailImages.Images.Add("audio", CreateMediaPlaceholder("AUDIO"));
        _thumbnailImages.Images.Add("font", CreateMediaPlaceholder("FONT"));
        _thumbnailImages.Images.Add("movie", CreateMediaPlaceholder("MOVIE"));
        _thumbnailImages.Images.Add("missing", CreateMediaPlaceholder("MISSING"));
    }

    private void SwitchView()
    {
        _assetHost.Controls.Clear();
        bool thumbnails = _viewMode.SelectedIndex > 0;
        if (thumbnails)
        {
            Size requested = _viewMode.SelectedIndex == 1 ? new Size(64, 64) : new Size(112, 112);
            if (_thumbnailImages.ImageSize != requested)
            {
                ClearDynamicThumbnails();
                _loadedThumbnailKeys.Clear();
                _thumbnailImages.ImageSize = requested;
            }
        }
        Control view = thumbnails ? _thumbnailView : _details;
        view.Dock = DockStyle.Fill;
        _assetHost.Controls.Add(view);
        QueueRebuild();
    }

    private void ChangeSort(string columnName)
    {
        if (string.Equals(_sortColumn, columnName, StringComparison.OrdinalIgnoreCase)) _sortDescending = !_sortDescending;
        else { _sortColumn = columnName; _sortDescending = false; }
        QueueRebuild();
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueRebuild()
    {
        if (_disposed) return;
        CancellationTokenSource cancellation;
        lock (_rebuildSync)
        {
            _rebuildCancellation?.Cancel();
            _rebuildCancellation?.Dispose();
            _rebuildCancellation = new CancellationTokenSource();
            cancellation = _rebuildCancellation;
        }
        _activeRebuildTask = RebuildAsync(cancellation.Token);
    }

    private async Task RebuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            AssetQuery parsed = AssetQueryParser.Parse(_search.Text);
            AssetQuery query = parsed with
            {
                Category = _browseFilter.Category ?? parsed.Category,
                Missing = _browseFilter.SmartCollection == ResourceSmartCollection.Missing ? true : parsed.Missing,
                Used = _browseFilter.SmartCollection == ResourceSmartCollection.NoKnownReferences ? false : parsed.Used,
                Encrypted = _browseFilter.SmartCollection == ResourceSmartCollection.Encrypted ? true : parsed.Encrypted,
                Duplicate = null
            };
            IReadOnlyList<AssetRecord> records = _session.Assets.Search(query);
            bool duplicatesRequested = _browseFilter.SmartCollection == ResourceSmartCollection.Duplicates || parsed.Duplicate == true;
            if (duplicatesRequested)
            {
                IReadOnlyList<IReadOnlyList<AssetVariant>> groups = await _session.Assets.FindExactDuplicatesAsync(cancellationToken).ConfigureAwait(false);
                HashSet<string> duplicatePaths = groups.SelectMany(group => group).Select(variant => variant.NormalizedRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                records = records.Where(record => record.Variants.Any(variant => duplicatePaths.Contains(variant.NormalizedRelativePath))).ToArray();
            }
            records = ApplySmartFilter(records, _browseFilter.SmartCollection);
            records = Sort(records);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;
            if (InvokeRequired) TryPostToUi(() => InstallRecords(records, cancellationToken));
            else InstallRecords(records, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_disposed) TryPostToUi(() => _summary.Text = "Catalogue error: " + exception.Message);
        }
    }

    private IReadOnlyList<AssetRecord> ApplySmartFilter(IReadOnlyList<AssetRecord> records, ResourceSmartCollection? smart) => smart switch
    {
        ResourceSmartCollection.PendingChanges => records.Where(record => record.Status is AssetStatus.PendingImport or AssetStatus.PendingReplacement or AssetStatus.PendingRename or AssetStatus.PendingDeletion).ToArray(),
        ResourceSmartCollection.InvalidOrUnsupported => records.Where(record => record.Status is AssetStatus.UnsupportedFormat or AssetStatus.Invalid or AssetStatus.CaseConflict or AssetStatus.VariantConflict).ToArray(),
        ResourceSmartCollection.Warnings => records.Where(record => record.Diagnostics.Count > 0).ToArray(),
        ResourceSmartCollection.RecentlyModified => records.Where(record => record.Variants.Any(variant => variant.LastWriteTimeUtc >= DateTimeOffset.UtcNow.AddDays(-7))).ToArray(),
        _ => records
    };

    private IReadOnlyList<AssetRecord> Sort(IReadOnlyList<AssetRecord> records)
    {
        Func<AssetRecord, object?> selector = _sortColumn switch
        {
            "Category" => record => record.Key.Category,
            "Media" => record => record.MediaKind,
            "Variants" => record => record.Variants.Count,
            "Size" => record => record.TotalBytes,
            "Exact" => record => record.Usage.ExactReferenceCount,
            "Heuristic" => record => record.Usage.HeuristicReferenceCount,
            "Status" => record => record.Status,
            _ => record => record.Key.LogicalName
        };
        IOrderedEnumerable<AssetRecord> ordered = _sortDescending
            ? records.OrderByDescending(selector, ObjectComparer.Instance)
            : records.OrderBy(selector, ObjectComparer.Instance);
        return ordered.ThenBy(record => record.Key.LogicalName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void InstallRecords(IReadOnlyList<AssetRecord> records, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested) return;
        AssetKey? selected = _pendingSelectionKey ?? SelectedRecord()?.Key;
        _details.ClearSelection();
        _details.CurrentCell = null;
        _visible = records;
        _details.RowCount = records.Count;
        _details.Invalidate();
        ClearDynamicThumbnails();
        _loadedThumbnailKeys.Clear();
        PopulateThumbnailItems();
        bool restoredSelection = RestoreSelection(selected, _pendingRelativePath);
        if (!restoredSelection) _ = ShowSelectionAsync(null);
        _pendingSelectionKey = null;
        _pendingRelativePath = null;
        if (!string.IsNullOrWhiteSpace(_pendingPreviewTab))
        {
            _preview.SelectTab(_pendingPreviewTab);
            _pendingPreviewTab = null;
        }
        AssetCatalogueStatistics stats = _session.Assets.Statistics;
        _summary.Text = $"Showing {records.Count:N0} of {stats.LogicalAssetCount:N0} logical assets · {stats.PhysicalVariantCount:N0} files · {stats.MissingAssetCount:N0} missing";
        UpdateCommandStates();
        if (_viewMode.SelectedIndex > 0) QueueVisibleThumbnails();
    }

    private void PopulateThumbnailItems()
    {
        _thumbnailView.BeginUpdate();
        try
        {
            _thumbnailView.Items.Clear();
            for (int index = 0; index < _visible.Count; index++)
            {
                AssetRecord asset = _visible[index];
                string imageKey = asset.IsMissing ? "missing" : asset.MediaKind.ToString().ToLowerInvariant();
                if (!_thumbnailImages.Images.ContainsKey(imageKey)) imageKey = "image";
                ListViewItem item = new(asset.Key.LogicalName, imageKey)
                {
                    Tag = index,
                    ToolTipText = $"{asset.Key}\r\n{asset.Status}\r\n{asset.Variants.Count} variant(s)"
                };
                _thumbnailView.Items.Add(item);
            }
        }
        finally { _thumbnailView.EndUpdate(); }
    }

    private void QueueVisibleThumbnails()
    {
        if (_disposed || _viewMode.SelectedIndex <= 0 || _visible.Count == 0 || !_thumbnailView.IsHandleCreated) return;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = CancellationTokenSource.CreateLinkedTokenSource(_rebuildCancellation?.Token ?? CancellationToken.None);
        _activeThumbnailTask = PopulateVisibleThumbnailsAsync(_thumbnailCancellation.Token);
    }

    private async Task PopulateVisibleThumbnailsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        int top = 0;
        try { top = _thumbnailView.TopItem?.Index ?? 0; }
        catch (InvalidOperationException) { }
        int rowHeight = Math.Max(48, _thumbnailImages.ImageSize.Height + 36);
        int visibleRows = Math.Max(3, (int)Math.Ceiling(_thumbnailView.ClientSize.Height / (double)rowHeight));
        int start = Math.Max(0, top - visibleRows);
        int end = Math.Min(_visible.Count, top + visibleRows * 3);
        int thumbnailSize = _thumbnailImages.ImageSize.Width;
        using SemaphoreSlim workers = new(4, 4);
        List<Task> tasks = [];
        for (int index = start; index < end; index++)
        {
            AssetRecord asset = _visible[index];
            string thumbnailKey = "asset:" + asset.Key;
            if (_loadedThumbnailKeys.Contains(thumbnailKey)) continue;
            int capturedIndex = index;
            tasks.Add(LoadThumbnailAsync(capturedIndex, asset, thumbnailKey, thumbnailSize, workers, cancellationToken));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task LoadThumbnailAsync(
        int index,
        AssetRecord asset,
        string key,
        int thumbnailSize,
        SemaphoreSlim workers,
        CancellationToken cancellationToken)
    {
        AssetVariant? variant = asset.PreferredVariant;
        if (asset.MediaKind != AssetMediaKind.Image || variant is null || variant.IsEncrypted || variant.Status == AssetStatus.PendingDeletion) return;
        await workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AssetThumbnail thumbnail = await _thumbnails.GetThumbnailAsync(asset, variant, new AssetThumbnailRequest(thumbnailSize, thumbnailSize), cancellationToken).ConfigureAwait(false);
            if (thumbnail.PngBytes.Length == 0) return;
            using MemoryStream stream = new(thumbnail.PngBytes, writable: false);
            using Image decoded = Image.FromStream(stream);
            Bitmap owned = new(decoded);
            if (_disposed || !TryPostToUi(() => InstallThumbnail(index, asset.Key, key, owned, cancellationToken)))
            {
                owned.Dispose();
            }
        }
        finally { workers.Release(); }
    }

    private void InstallThumbnail(int index, AssetKey expectedAsset, string key, Bitmap image, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested || index >= _thumbnailView.Items.Count
            || index >= _visible.Count || !AssetKeysEqual(_visible[index].Key, expectedAsset))
        {
            image.Dispose();
            return;
        }
        if (!_thumbnailImages.Images.ContainsKey(key))
        {
            _thumbnailImages.Images.Add(key, image);
            _loadedThumbnailKeys.Add(key);
        }
        image.Dispose();
        _thumbnailView.Items[index].ImageKey = key;
    }

    private async Task SelectFromDetailsAsync()
    {
        if (_details.SelectedRows.Count != 1) { await ShowSelectionAsync(null).ConfigureAwait(true); return; }
        int index = _details.SelectedRows[0].Index;
        await ShowSelectionAsync(index >= 0 && index < _visible.Count ? _visible[index] : null).ConfigureAwait(true);
    }

    private async Task SelectFromThumbnailsAsync()
    {
        if (_thumbnailView.SelectedItems.Count != 1 || _thumbnailView.SelectedItems[0].Tag is not int index || index < 0 || index >= _visible.Count)
        {
            await ShowSelectionAsync(null).ConfigureAwait(true);
            return;
        }
        await ShowSelectionAsync(_visible[index]).ConfigureAwait(true);
    }

    private async Task ShowSelectionAsync(AssetRecord? record)
    {
        if (_disposed) return;
        _selection.Select(record is null
            ? new EmptySelection()
            : new AssetSelection(record.Key, record.PreferredVariant?.RelativePath));
        ValueTask previewLoad = _preview.ShowAssetAsync(record);
        UpdateCommandStates();
        await previewLoad.ConfigureAwait(true);
        if (_disposed) return;
        UpdateCommandStates();
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private AssetRecord? SelectedRecord()
    {
        if (_viewMode.SelectedIndex > 0)
        {
            if (_thumbnailView.SelectedItems.Count == 1 && _thumbnailView.SelectedItems[0].Tag is int index && index >= 0 && index < _visible.Count) return _visible[index];
            return null;
        }
        if (_details.SelectedRows.Count == 1)
        {
            int index = _details.SelectedRows[0].Index;
            if (index >= 0 && index < _visible.Count) return _visible[index];
        }
        return null;
    }

    private AssetVariant? SelectedVariant() => _preview.SelectedVariant ?? SelectedRecord()?.PreferredVariant;

    private void UpdateCommandStates()
    {
        if (_disposed) return;
        AssetRecord? record = SelectedRecord();
        AssetVariant? variant = SelectedVariant();
        bool writable = !_session.State.IsReadOnly;
        bool editableRecord = record is { IsMissing: false }
            && record.Status is not AssetStatus.PendingDeletion and not AssetStatus.Encrypted
            && record.Variants.All(candidate => !candidate.IsEncrypted);
        _import.Enabled = writable;
        _importFolder.Enabled = writable;
        _replace.Enabled = writable && variant is not null && !variant.IsEncrypted && variant.Status != AssetStatus.PendingDeletion;
        _rename.Enabled = writable && editableRecord;
        _delete.Enabled = writable && editableRecord;
        _export.Enabled = variant is not null && variant.Status != AssetStatus.PendingDeletion;
        _open.Enabled = variant is not null && !variant.IsEncrypted && variant.Status != AssetStatus.PendingDeletion;
        _reveal.Enabled = variant is not null;
        CommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task ShowImportWizardAsync(IEnumerable<string>? initialFiles = null)
    {
        if (_session.State.IsReadOnly) return Task.CompletedTask;
        using AssetImportWizardForm dialog = new(_session, initialFiles, SuggestedImportCategory);
        _theme?.Apply(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        QueueRebuild();
        MessageBox.Show(this, $"{dialog.ImportedCount:N0} asset(s) were staged as one undoable transaction. Use Save Project to write them to disk.", "Batch Import Staged", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return Task.CompletedTask;
    }

    private async Task ReplaceAsync()
    {
        AssetVariant? variant = SelectedVariant();
        if (variant is null || variant.IsEncrypted || variant.Status == AssetStatus.PendingDeletion) return;
        using OpenFileDialog open = new() { Title = "Choose replacement asset", CheckFileExists = true, Filter = $"{variant.Extension} files|*{variant.Extension}|All files|*.*" };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        await StageReplaceAsync(variant, open.FileName).ConfigureAwait(true);
    }

    private async Task StageReplaceAsync(AssetVariant variant, string replacement)
    {
        try
        {
            AssetManagementPreview preview = await _session.AssetOperations.PreviewReplaceAsync(variant, replacement).ConfigureAwait(true);
            using AssetOperationPreviewDialog dialog = new(preview);
            _theme?.Apply(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await _session.AssetOperations.ReplaceAsync(variant, replacement).ConfigureAwait(true);
            ShowStagedMessage("The replacement is staged. Use Save Project to commit it.");
        }
        catch (Exception exception) { ShowOperationError("Replacement failed", exception); }
    }

    private async Task RenameAsync()
    {
        AssetRecord? record = SelectedRecord();
        if (record is null || record.IsMissing || record.Status is AssetStatus.PendingDeletion or AssetStatus.Encrypted
            || record.Variants.Any(variant => variant.IsEncrypted)) return;
        string? value = TextPromptDialog.Show(this, "Rename Asset", "New logical name (subfolders are allowed; omit the extension):", record.Key.LogicalName);
        if (string.IsNullOrWhiteSpace(value) || value.Equals(record.Key.LogicalName, StringComparison.Ordinal)) return;
        try
        {
            AssetManagementPreview preview = await _session.AssetOperations.PreviewRenameAsync(record.Key, value).ConfigureAwait(true);
            using AssetOperationPreviewDialog dialog = new(preview);
            _theme?.Apply(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await _session.AssetOperations.RenameAsync(record.Key, value).ConfigureAwait(true);
            _pendingSelectionKey = new AssetKey(record.Key.Category, value);
            ShowStagedMessage("The rename and exact reference updates are staged. Heuristic references were not modified.");
        }
        catch (Exception exception) { ShowOperationError("Rename failed", exception); }
    }

    private async Task DeleteAsync()
    {
        AssetRecord? record = SelectedRecord();
        if (record is null || record.IsMissing || record.Status is AssetStatus.PendingDeletion or AssetStatus.Encrypted
            || record.Variants.Any(variant => variant.IsEncrypted)) return;
        if (record.Usage.ExactReferenceCount > 0)
        {
            MessageBox.Show(this, $"This asset has {record.Usage.ExactReferenceCount} exact reference(s). Replace or clear those references before deleting it. The Used By tab lists each location.", "Deletion Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (record.Usage.HeuristicReferenceCount > 0)
        {
            DialogResult proceed = MessageBox.Show(this, $"Playroma found {record.Usage.HeuristicReferenceCount} heuristic script/note reference(s). These cannot be proven safe. Stage deletion anyway?", "Heuristic References", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes) return;
        }
        try
        {
            AssetManagementPreview preview = await _session.AssetOperations.PreviewDeleteAsync(record.Key).ConfigureAwait(true);
            using AssetOperationPreviewDialog dialog = new(preview, "Stage Deletion");
            _theme?.Apply(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await _session.AssetOperations.DeleteAsync(record.Key).ConfigureAwait(true);
            ShowStagedMessage("The deletion is staged and recoverable until Save Project commits it.");
        }
        catch (Exception exception) { ShowOperationError("Deletion failed", exception); }
    }

    private async Task ExportAsync()
    {
        AssetVariant? variant = SelectedVariant();
        if (variant is null || variant.Status == AssetStatus.PendingDeletion) return;
        using SaveFileDialog save = new()
        {
            Title = "Export asset copy",
            FileName = Path.GetFileName(variant.RelativePath),
            Filter = $"{variant.Extension} files|*{variant.Extension}|All files|*.*",
            AddExtension = true,
            DefaultExt = variant.Extension.TrimStart('.'),
            OverwritePrompt = true
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await using Stream source = await _session.AssetContent.OpenReadAsync(variant.RelativePath).ConfigureAwait(true);
            await using FileStream destination = new(
                save.FileName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination).ConfigureAwait(true);
            await destination.FlushAsync().ConfigureAwait(true);
            MessageBox.Show(this, "The selected asset variant was exported.", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowOperationError("Export failed", exception);
        }
    }

    private void OpenSelectedExternallyCore()
    {
        AssetVariant? variant = SelectedVariant();
        if (variant is null) return;
        try
        {
            string path = _session.AssetContent.GetFullPath(variant.RelativePath);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) { ShowOperationError("Could not open asset", exception); }
    }

    private void RevealSelectedCore()
    {
        AssetVariant? variant = SelectedVariant();
        if (variant is null) return;
        try
        {
            string path = _session.AssetContent.GetFullPath(variant.RelativePath);
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (Exception exception) { ShowOperationError("Could not reveal asset", exception); }
    }

    private async Task RefreshAsync()
    {
        try
        {
            _refresh.Enabled = false;
            _summary.Text = "Refreshing catalogue…";
            await _session.RefreshAssetCatalogueAsync().ConfigureAwait(true);
            QueueRebuild();
        }
        catch (Exception exception) { ShowOperationError("Refresh failed", exception); }
        finally { _refresh.Enabled = true; }
    }

    private bool RestoreSelection(AssetKey? key, string? relativePath)
    {
        int index = -1;
        if (key.HasValue) index = _visible.ToList().FindIndex(record => AssetKeysEqual(record.Key, key.Value));
        if (index < 0 && !string.IsNullOrWhiteSpace(relativePath))
        {
            index = _visible.ToList().FindIndex(record => record.Variants.Any(variant => variant.RelativePath.Equals(relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)));
        }
        if (index < 0) return false;
        if (_viewMode.SelectedIndex > 0 && index < _thumbnailView.Items.Count)
        {
            _thumbnailView.Items[index].Selected = true;
            _thumbnailView.Items[index].EnsureVisible();
            return true;
        }
        if (index < _details.RowCount)
        {
            _details.Rows[index].Selected = true;
            _details.CurrentCell = _details.Rows[index].Cells[0];
            try { _details.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 2); }
            catch (InvalidOperationException) { }
            return true;
        }
        return false;
    }

    private void OnCatalogueChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (InvokeRequired) TryPostToUi(QueueRebuild); else QueueRebuild();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (InvokeRequired) TryPostToUi(UpdateCommandStates); else UpdateCommandStates();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (CanImport && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) _ = ShowImportWizardAsync(paths);
    }

    private bool TryPostToUi(Action action)
    {
        if (_disposed || IsDisposed || !IsHandleCreated) return false;
        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    private void ClearDynamicThumbnails()
    {
        string[] dynamicKeys = _thumbnailImages.Images.Keys.Cast<string>()
            .Where(key => key.StartsWith("asset:", StringComparison.Ordinal))
            .ToArray();
        foreach (string key in dynamicKeys) _thumbnailImages.Images.RemoveByKey(key);
    }

    private TreeNode? FindNodeByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (TreeNode root in _categories.Nodes)
        {
            TreeNode? found = FindNodeRecursive(root, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static TreeNode? FindNodeRecursive(TreeNode node, string name)
    {
        if (node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return node;
        foreach (TreeNode child in node.Nodes)
        {
            TreeNode? found = FindNodeRecursive(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool TryParseAssetKey(string? value, out AssetKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        int separator = value.IndexOf(':');
        if (separator <= 0 || separator >= value.Length - 1) return false;
        if (!Enum.TryParse(value[..separator], ignoreCase: true, out AssetCategory category)) return false;
        try { key = new AssetKey(category, value[(separator + 1)..]); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool AssetKeysEqual(AssetKey left, AssetKey right) => left.Category == right.Category
        && string.Equals(left.LogicalName, right.LogicalName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string FormatCategory(AssetCategory category) => System.Text.RegularExpressions.Regex.Replace(category.ToString(), "([a-z])([A-Z])", "$1 $2");

    private static string FormatMetadata(AssetMetadataSummary metadata)
    {
        if (metadata.Image is { } image) return $"{image.Width} × {image.Height}";
        if (metadata.Audio is { Duration: { } duration }) return duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        if (metadata.Movie is { } movie) return movie.Width.HasValue ? $"{movie.Width} × {movie.Height}" : movie.Duration?.ToString() ?? string.Empty;
        if (metadata.Font is { } font) return font.Family ?? font.Format;
        return string.Empty;
    }

    private static Bitmap CreateMediaPlaceholder(string text)
    {
        Bitmap image = new(112, 112);
        using Graphics graphics = Graphics.FromImage(image);
        graphics.Clear(SystemColors.ControlDark);
        using Pen border = new(SystemColors.ControlDarkDark, 2f);
        graphics.DrawRectangle(border, 1, 1, 109, 109);
        using Font font = new(FontFamily.GenericSansSerif, text.Length > 5 ? 10f : 14f, FontStyle.Bold);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(4, 4, 104, 104), SystemColors.ControlLightLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        return image;
    }

    private void ShowStagedMessage(string message)
    {
        QueueRebuild();
        MessageBox.Show(this, message, "Asset Operation Staged", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowOperationError(string title, Exception exception) =>
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private sealed record ResourceBrowseFilter(
        AssetCategory? Category = null,
        ResourceSmartCollection? SmartCollection = null)
    {
        public static ResourceBrowseFilter All { get; } = new(SmartCollection: ResourceSmartCollection.All);
    }

    private sealed class ObjectComparer : IComparer<object?>
    {
        public static ObjectComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is IComparable comparable && x.GetType() == y.GetType()) return comparable.CompareTo(y);
            return StringComparer.OrdinalIgnoreCase.Compare(Convert.ToString(x), Convert.ToString(y));
        }
    }
}
