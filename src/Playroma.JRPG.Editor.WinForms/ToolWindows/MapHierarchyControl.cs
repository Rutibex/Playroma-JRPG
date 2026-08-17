using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.ToolWindows;

/// <summary>Transactional MV map hierarchy editor with create, duplicate, delete, rename, reparent, reorder, search and import/export.</summary>
public sealed class MapHierarchyControl : UserControl
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _selection;
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, AllowDrop = true };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search maps…" };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private const string MetadataPath = ".playroma/maps.json";
    private readonly List<MvMapDescriptor> _maps = [];
    private MapEditorMetadataDocument _metadata = new();
    private MvProjectSession? _displayedSession;
    private int _metadataLoadVersion;

    public MapHierarchyControl(IWorkspaceService workspace, ISelectionService selection)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        BuildToolbar();
        Controls.Add(_tree);
        Controls.Add(_search);
        Controls.Add(_toolbar);
        _workspace.StateChanged += OnWorkspaceChanged;
        _search.TextChanged += (_, _) => RebuildTree();
        _tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is not MvMapDescriptor map) return;
            _selection.Select(new MapSelection(map.Id));
            MapSelected?.Invoke(this, map.Id);
        };
        _tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node.Tag is MvMapDescriptor map) OpenRequested?.Invoke(this, MapViewRequest(map));
        };
        _tree.NodeMouseClick += OnNodeMouseClick;
        _tree.ItemDrag += (_, e) =>
        {
            if (e.Item is TreeNode node && node.Tag is MvMapDescriptor) DoDragDrop(node, DragDropEffects.Move);
        };
        _tree.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true ? DragDropEffects.Move : DragDropEffects.None;
        _tree.DragOver += (_, e) =>
        {
            Point client = _tree.PointToClient(new Point(e.X, e.Y));
            _tree.SelectedNode = _tree.GetNodeAt(client);
            e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true ? DragDropEffects.Move : DragDropEffects.None;
        };
        _tree.DragDrop += async (_, e) => await HandleDropAsync(e).ConfigureAwait(true);
        Rebuild();
    }

    public event EventHandler<DocumentRequest>? OpenRequested;
    public event EventHandler<int>? MapSelected;

    public int? SelectedMapId => SelectedMap?.Id;
    public string SearchText
    {
        get => _search.Text;
        set => _search.Text = value ?? string.Empty;
    }

    public IReadOnlyList<int> CaptureExpandedMapIds() =>
        EnumerateNodes(_tree.Nodes)
            .Where(node => node.IsExpanded && node.Tag is MvMapDescriptor)
            .Select(node => ((MvMapDescriptor)node.Tag!).Id)
            .ToArray();

    public void RestoreExpandedMapIds(IEnumerable<int>? mapIds)
    {
        HashSet<int> expanded = mapIds?.ToHashSet() ?? [];
        foreach (TreeNode node in EnumerateNodes(_tree.Nodes))
        {
            if (node.Tag is MvMapDescriptor map && expanded.Contains(map.Id)) node.Expand();
        }
    }

    public bool SelectMap(int mapId)
    {
        TreeNode? node = FindNode(mapId);
        if (node is null) return false;
        _tree.SelectedNode = node;
        node.EnsureVisible();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _workspace.StateChanged -= OnWorkspaceChanged;
        base.Dispose(disposing);
    }

    public void Rebuild()
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, Rebuild)) return;
        _displayedSession = _workspace.CurrentSession;
        int metadataVersion = Interlocked.Increment(ref _metadataLoadVersion);
        _maps.Clear();
        if (_workspace.CurrentSession is MvProjectSession session)
        {
            _maps.AddRange(session.Workspace.Snapshot.Maps);
            _ = LoadMetadataAsync(session, metadataVersion);
        }
        else
        {
            _metadata = new MapEditorMetadataDocument();
        }
        RebuildTree();
    }

    private void BuildToolbar()
    {
        ToolStripButton create = new("New") { ToolTipText = "Create map" };
        ToolStripButton duplicate = new("Duplicate") { ToolTipText = "Duplicate selected map" };
        ToolStripButton rename = new("Rename") { ToolTipText = "Rename selected map" };
        ToolStripButton delete = new("Delete") { ToolTipText = "Delete selected map" };
        ToolStripButton up = new("↑") { ToolTipText = "Move selected map up among its siblings" };
        ToolStripButton down = new("↓") { ToolTipText = "Move selected map down among its siblings" };
        ToolStripButton import = new("Import") { ToolTipText = "Import an MV MapNNN.json file" };
        ToolStripButton export = new("Export") { ToolTipText = "Export selected map JSON" };
        create.Click += async (_, _) => await CreateMapAsync().ConfigureAwait(true);
        duplicate.Click += async (_, _) => await DuplicateSelectedAsync().ConfigureAwait(true);
        rename.Click += async (_, _) => await RenameSelectedAsync().ConfigureAwait(true);
        delete.Click += async (_, _) => await DeleteSelectedAsync().ConfigureAwait(true);
        up.Click += async (_, _) => await MoveSelectedAsync(-1).ConfigureAwait(true);
        down.Click += async (_, _) => await MoveSelectedAsync(1).ConfigureAwait(true);
        import.Click += async (_, _) => await ImportMapAsync().ConfigureAwait(true);
        export.Click += async (_, _) => await ExportSelectedAsync().ConfigureAwait(true);
        _toolbar.Items.AddRange([create, duplicate, rename, delete, new ToolStripSeparator(), up, down, new ToolStripSeparator(), import, export]);
    }

    private void RebuildTree(int? selectMapId = null)
    {
        string filter = _search.Text.Trim();
        int? selected = selectMapId ?? (_tree.SelectedNode?.Tag as MvMapDescriptor)?.Id;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        try
        {
            IEnumerable<MvMapDescriptor> visible = _maps;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                HashSet<int> matches = _maps
                    .Where(map => map.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || map.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal)
                        || _metadata.Get(map.Id).Tags.Any(tag => tag.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    .Select(map => map.Id)
                    .ToHashSet();
                HashSet<int> include = new(matches);
                foreach (MvMapDescriptor map in _maps.Where(map => matches.Contains(map.Id)))
                {
                    int parent = map.ParentId;
                    while (parent > 0 && include.Add(parent)) parent = _maps.FirstOrDefault(candidate => candidate.Id == parent)?.ParentId ?? 0;
                }
                visible = _maps.Where(map => include.Contains(map.Id));
            }
            Dictionary<int, TreeNode> nodes = visible.ToDictionary(
                map => map.Id,
                map =>
                {
                    MapEditorMetadataEntry metadata = _metadata.Get(map.Id);
                    string favorite = metadata.Favorite ? "★ " : string.Empty;
                    string tags = metadata.Tags.Length == 0 ? string.Empty : "  [" + string.Join(", ", metadata.Tags) + "]";
                    return new TreeNode($"{favorite}{map.Id:D3} {map.Name}{tags}") { Tag = map };
                });
            foreach (MvMapDescriptor map in visible.OrderBy(map => map.Order).ThenBy(map => map.Id))
            {
                TreeNode node = nodes[map.Id];
                if (map.ParentId > 0 && nodes.TryGetValue(map.ParentId, out TreeNode? parent)) parent.Nodes.Add(node);
                else _tree.Nodes.Add(node);
            }
            if (!string.IsNullOrWhiteSpace(filter)) _tree.ExpandAll();
            TreeNode? selectedNode = FindNode(selected);
            if (selectedNode is not null) _tree.SelectedNode = selectedNode;
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private async Task CreateMapAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        using MapCreateDialog dialog = new("New Map", 17, 13, SelectedMap?.Id ?? 0);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await CreateMapCoreAsync(session, dialog.MapName, dialog.WidthValue, dialog.HeightValue, dialog.ParentId, null).ConfigureAwait(true);
    }

    private async Task DuplicateSelectedAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        MvMapDescriptor? source = SelectedMap;
        if (session is null || source is null) return;
        ReadOnlyMemory<byte> bytes;
        await using (IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(source.RelativePath).ConfigureAwait(true))
        {
            bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        }
        await CreateMapCoreAsync(session, source.Name + " Copy", source.Width ?? 17, source.Height ?? 13, source.ParentId, bytes.ToArray()).ConfigureAwait(true);
    }

    private async Task CreateMapCoreAsync(MvProjectSession session, string name, int width, int height, int parentId, byte[]? sourceBytes)
    {
        MvDatabaseFileSummary mapInfos = session.Workspace.Snapshot.Database.Find(MvDocumentKind.MapInfos)
            ?? throw new FileNotFoundException("MapInfos.json was not found.");
        (int newId, int arrayLength) = await FindFreeMapIdAsync(session, mapInfos.RelativePath).ConfigureAwait(true);
        int order = _maps.Count == 0 ? 1 : _maps.Max(map => map.Order) + 1;
        string directory = Path.GetDirectoryName(mapInfos.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        string relativePath = string.IsNullOrEmpty(directory) ? $"Map{newId:D3}.json" : $"{directory}/Map{newId:D3}.json";
        byte[] mapBytes = sourceBytes ?? CreateBlankMap(width, height);
        string infoRaw = JsonSerializer.Serialize(new
        {
            id = newId,
            expanded = true,
            name,
            order,
            parentId,
            scrollX = 0,
            scrollY = 0
        });
        try
        {
            await using ProjectTransaction transaction = session.BeginTransaction("Create map");
            if (newId < arrayLength)
            {
                transaction.Add(new SetJsonValueCommand(mapInfos.RelativePath, JsonPath.Root.Append(newId), infoRaw, "Add map hierarchy entry"));
            }
            else
            {
                transaction.Add(new InsertJsonArrayElementCommand(mapInfos.RelativePath, JsonPath.Root, newId, infoRaw, "Add map hierarchy entry"));
            }
            transaction.Add(new CreateGeneratedProjectFileCommand(relativePath, mapBytes, $"Create {relativePath}"));
            await transaction.CommitAsync().ConfigureAwait(true);
            MvMapDescriptor descriptor = new(newId, name, parentId, order, relativePath, true, width, height, 0, 0, 0, [], []);
            _maps.Add(descriptor);
            RebuildTree(newId);
            OpenRequested?.Invoke(this, MapViewRequest(descriptor));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Create map failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RenameSelectedAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        MvMapDescriptor? map = SelectedMap;
        if (session is null || map is null) return;
        string? name = Prompt.Show(this, "Map name:", "Rename Map", map.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, map.Name, StringComparison.Ordinal)) return;
        MvDatabaseFileSummary mapInfos = GetMapInfos(session);
        await ExecuteMapInfoChangesAsync(session, "Rename map", [new SetJsonStringCommand(mapInfos.RelativePath, JsonPath.Root.Append(map.Id).Append("name"), name, "Rename map")]).ConfigureAwait(true);
        ReplaceLocal(map with { Name = name });
    }

    private async Task DeleteSelectedAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        MvMapDescriptor? map = SelectedMap;
        if (session is null || map is null) return;
        MvMapDescriptor[] directChildren = _maps.Where(candidate => candidate.ParentId == map.Id).OrderBy(candidate => candidate.Order).ToArray();
        using MapDeleteDialog dialog = new(map, directChildren.Length);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        MvMapDescriptor[] deleted = dialog.Mode == MapDeleteMode.DeleteSubtree
            ? GetSubtree(map.Id).ToArray()
            : [map];
        HashSet<int> deletedIds = deleted.Select(item => item.Id).ToHashSet();
        int exact = 0;
        int heuristic = 0;
        foreach (MvMapDescriptor candidate in deleted)
        {
            DependencyReport dependencies = session.GetDependencies(new ReferenceTarget(ReferenceTargetKind.Map, candidate.Id, candidate.Name));
            exact += dependencies.ExactReferences.Count;
            heuristic += dependencies.HeuristicReferences.Count;
        }
        string warning = $"Delete {deleted.Length} map(s), beginning with {map.Id:D3} {map.Name}?\n\nExact references: {exact}\nHeuristic references: {heuristic}\nThis is one undoable transaction.";
        if (MessageBox.Show(this, warning, "Confirm Map Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        MvDatabaseFileSummary mapInfos = GetMapInfos(session);
        List<IEditorCommand> commands = [];
        foreach (MvMapDescriptor deletedMap in deleted)
        {
            commands.Add(new SetJsonValueCommand(mapInfos.RelativePath, JsonPath.Root.Append(deletedMap.Id), "null", "Remove map hierarchy entry"));
            commands.Add(new DeleteAssetFileCommand(deletedMap.RelativePath));
        }
        if (dialog.Mode != MapDeleteMode.DeleteSubtree)
        {
            int newParent = dialog.Mode == MapDeleteMode.MoveChildrenToRoot ? 0 : map.ParentId;
            foreach (MvMapDescriptor child in directChildren)
            {
                commands.Add(new SetJsonInt32Command(mapInfos.RelativePath, JsonPath.Root.Append(child.Id).Append("parentId"), newParent, "Reparent child map"));
            }
        }
        if (!await ExecuteMapInfoChangesAsync(session, "Delete map", commands).ConfigureAwait(true)) return;

        _maps.RemoveAll(candidate => deletedIds.Contains(candidate.Id));
        foreach (int deletedId in deletedIds) _metadata = _metadata.Set(deletedId, new MapEditorMetadataEntry());
        if (dialog.Mode != MapDeleteMode.DeleteSubtree)
        {
            int newParent = dialog.Mode == MapDeleteMode.MoveChildrenToRoot ? 0 : map.ParentId;
            for (int index = 0; index < _maps.Count; index++)
            {
                if (_maps[index].ParentId == map.Id) _maps[index] = _maps[index] with { ParentId = newParent };
            }
        }
        await SaveMetadataAsync("Remove deleted map metadata").ConfigureAwait(true);
        RebuildTree();
    }

    private IEnumerable<MvMapDescriptor> GetSubtree(int rootId)
    {
        Queue<int> pending = new();
        HashSet<int> visited = [];
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            int id = pending.Dequeue();
            if (!visited.Add(id)) continue;
            MvMapDescriptor? current = _maps.FirstOrDefault(map => map.Id == id);
            if (current is null) continue;
            yield return current;
            foreach (MvMapDescriptor child in _maps.Where(map => map.ParentId == id)) pending.Enqueue(child.Id);
        }
    }

    private async Task MoveSelectedAsync(int direction)
    {
        MvProjectSession? session = _workspace.CurrentSession;
        MvMapDescriptor? map = SelectedMap;
        if (session is null || map is null) return;
        MvMapDescriptor[] siblings = _maps.Where(candidate => candidate.ParentId == map.ParentId).OrderBy(candidate => candidate.Order).ThenBy(candidate => candidate.Id).ToArray();
        int index = Array.FindIndex(siblings, candidate => candidate.Id == map.Id);
        int otherIndex = index + direction;
        if (index < 0 || otherIndex < 0 || otherIndex >= siblings.Length) return;
        MvMapDescriptor other = siblings[otherIndex];
        MvDatabaseFileSummary mapInfos = GetMapInfos(session);
        IEditorCommand[] commands =
        [
            new SetJsonInt32Command(mapInfos.RelativePath, JsonPath.Root.Append(map.Id).Append("order"), other.Order, "Move map"),
            new SetJsonInt32Command(mapInfos.RelativePath, JsonPath.Root.Append(other.Id).Append("order"), map.Order, "Move map")
        ];
        if (!await ExecuteMapInfoChangesAsync(session, "Reorder maps", commands).ConfigureAwait(true)) return;
        ReplaceLocal(map with { Order = other.Order });
        ReplaceLocal(other with { Order = map.Order });
        RebuildTree(map.Id);
    }

    private async Task HandleDropAsync(DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode sourceNode || sourceNode.Tag is not MvMapDescriptor source) return;
        Point client = _tree.PointToClient(new Point(e.X, e.Y));
        TreeNode? targetNode = _tree.GetNodeAt(client);
        int parentId = targetNode?.Tag is MvMapDescriptor target ? target.Id : 0;
        if (parentId == source.Id || IsDescendant(parentId, source.Id) || parentId == source.ParentId) return;
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        MvDatabaseFileSummary mapInfos = GetMapInfos(session);
        if (!await ExecuteMapInfoChangesAsync(session, "Reparent map", [new SetJsonInt32Command(mapInfos.RelativePath, JsonPath.Root.Append(source.Id).Append("parentId"), parentId, "Reparent map")]).ConfigureAwait(true)) return;
        ReplaceLocal(source with { ParentId = parentId });
        RebuildTree(source.Id);
    }

    private async Task ImportMapAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        using OpenFileDialog dialog = new() { Filter = "RPG Maker MV map (*.json)|*.json|All files (*.*)|*.*", Title = "Import MV Map" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(dialog.FileName).ConfigureAwait(true);
            using JsonDocument json = JsonDocument.Parse(bytes);
            int width = json.RootElement.GetProperty("width").GetInt32();
            int height = json.RootElement.GetProperty("height").GetInt32();
            string name = Path.GetFileNameWithoutExtension(dialog.FileName);
            await CreateMapCoreAsync(session, name, width, height, SelectedMap?.Id ?? 0, bytes).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportSelectedAsync()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        MvMapDescriptor? map = SelectedMap;
        if (session is null || map is null) return;
        using SaveFileDialog dialog = new() { Filter = "RPG Maker MV map (*.json)|*.json", FileName = $"Map{map.Id:D3}.json", Title = "Export MV Map" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(map.RelativePath).ConfigureAwait(true);
        ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        await File.WriteAllBytesAsync(dialog.FileName, bytes.ToArray()).ConfigureAwait(true);
    }

    private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.Node.Tag is not MvMapDescriptor map) return;
        _tree.SelectedNode = e.Node;
        ContextMenuStrip menu = new();
        menu.Items.Add("Open Map", null, (_, _) => OpenRequested?.Invoke(this, MapViewRequest(map)));
        menu.Items.Add("Open Properties", null, (_, _) => OpenRequested?.Invoke(this, MapPropertiesRequest(map)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Create Child Map…", null, async (_, _) => await CreateMapAsync().ConfigureAwait(true));
        menu.Items.Add("Duplicate", null, async (_, _) => await DuplicateSelectedAsync().ConfigureAwait(true));
        menu.Items.Add("Rename…", null, async (_, _) => await RenameSelectedAsync().ConfigureAwait(true));
        MapEditorMetadataEntry metadata = _metadata.Get(map.Id);
        menu.Items.Add(metadata.Favorite ? "Remove Favorite" : "Add Favorite", null, async (_, _) => await ToggleFavoriteAsync(map.Id).ConfigureAwait(true));
        menu.Items.Add("Edit Tags…", null, async (_, _) => await EditTagsAsync(map.Id).ConfigureAwait(true));
        menu.Items.Add("Delete…", null, async (_, _) => await DeleteSelectedAsync().ConfigureAwait(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Export Map…", null, async (_, _) => await ExportSelectedAsync().ConfigureAwait(true));
        ControlHelpers.DisposeTransientContextMenuAfterClose(menu, _tree);
        menu.Show(_tree, e.Location);
    }

    private async Task LoadMetadataAsync(MvProjectSession session, int version)
    {
        try
        {
            MapEditorMetadataDocument loaded;
            if (!session.Editing.Files.Exists(MetadataPath))
            {
                loaded = new MapEditorMetadataDocument();
            }
            else
            {
                await using Stream stream = await session.Editing.Files.OpenReadAsync(MetadataPath).ConfigureAwait(true);
                using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                string json = await reader.ReadToEndAsync().ConfigureAwait(true);
                loaded = MapEditorMetadataDocument.Deserialize(json);
            }

            if (IsDisposed
                || !ReferenceEquals(_workspace.CurrentSession, session)
                || version != Volatile.Read(ref _metadataLoadVersion))
            {
                return;
            }

            _metadata = loaded;
            RebuildTree();
        }
        catch (Exception exception)
        {
            if (IsDisposed
                || !ReferenceEquals(_workspace.CurrentSession, session)
                || version != Volatile.Read(ref _metadataLoadVersion))
            {
                return;
            }

            _metadata = new MapEditorMetadataDocument();
            MessageBox.Show(this, exception.Message, "Map metadata warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ToggleFavoriteAsync(int mapId)
    {
        MapEditorMetadataEntry current = _metadata.Get(mapId);
        _metadata = _metadata.Set(mapId, current with { Favorite = !current.Favorite });
        await SaveMetadataAsync("Update map favorite").ConfigureAwait(true);
        RebuildTree(mapId);
    }

    private async Task EditTagsAsync(int mapId)
    {
        MapEditorMetadataEntry current = _metadata.Get(mapId);
        string? raw = Prompt.Show(this, "Comma-separated tags:", "Edit Map Tags", string.Join(", ", current.Tags));
        if (raw is null) return;
        string[] tags = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _metadata = _metadata.Set(mapId, current with { Tags = tags });
        await SaveMetadataAsync("Update map tags").ConfigureAwait(true);
        RebuildTree(mapId);
    }

    private async Task SaveMetadataAsync(string description)
    {
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return;
        try
        {
            await using ProjectTransaction transaction = session.BeginTransaction(description);
            transaction.Add(new WriteGeneratedProjectFileCommand(
                MetadataPath,
                System.Text.Encoding.UTF8.GetBytes(_metadata.Serialize()),
                description));
            await transaction.CommitAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, description + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<(int Id, int ArrayLength)> FindFreeMapIdAsync(MvProjectSession session, string mapInfosPath)
    {
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(mapInfosPath).ConfigureAwait(true);
        ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync().ConfigureAwait(true);
        using JsonDocument json = JsonDocument.Parse(bytes);
        JsonElement root = json.RootElement;
        for (int index = 1; index < root.GetArrayLength(); index++)
        {
            if (root[index].ValueKind == JsonValueKind.Null) return (index, root.GetArrayLength());
        }
        return (root.GetArrayLength(), root.GetArrayLength());
    }

    private async Task<bool> ExecuteMapInfoChangesAsync(MvProjectSession session, string description, IEnumerable<IEditorCommand> commands)
    {
        try
        {
            await using ProjectTransaction transaction = session.BeginTransaction(description);
            foreach (IEditorCommand command in commands) transaction.Add(command);
            await transaction.CommitAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, description + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static byte[] CreateBlankMap(int width, int height)
    {
        int[] data = new int[checked(width * height * 6)];
        var map = new
        {
            autoplayBgm = false,
            autoplayBgs = false,
            battleback1Name = "",
            battleback2Name = "",
            bgm = new { name = "", pan = 0, pitch = 100, volume = 90 },
            bgs = new { name = "", pan = 0, pitch = 100, volume = 90 },
            disableDashing = false,
            displayName = "",
            encounterList = Array.Empty<object>(),
            encounterStep = 30,
            events = new object?[] { null },
            height,
            note = "",
            parallaxLoopX = false,
            parallaxLoopY = false,
            parallaxName = "",
            parallaxShow = true,
            parallaxSx = 0,
            parallaxSy = 0,
            scrollType = 0,
            specifyBattleback = false,
            tilesetId = 1,
            width,
            data
        };
        return JsonSerializer.SerializeToUtf8Bytes(map, new JsonSerializerOptions { WriteIndented = false });
    }

    private void ReplaceLocal(MvMapDescriptor replacement)
    {
        int index = _maps.FindIndex(map => map.Id == replacement.Id);
        if (index >= 0) _maps[index] = replacement;
        RebuildTree(replacement.Id);
    }

    private bool IsDescendant(int candidateId, int ancestorId)
    {
        int current = candidateId;
        HashSet<int> visited = [];
        while (current > 0 && visited.Add(current))
        {
            if (current == ancestorId) return true;
            current = _maps.FirstOrDefault(map => map.Id == current)?.ParentId ?? 0;
        }
        return false;
    }

    private TreeNode? FindNode(int? mapId)
    {
        if (!mapId.HasValue) return null;
        foreach (TreeNode root in _tree.Nodes)
        {
            TreeNode? found = FindNode(root, mapId.Value);
            if (found is not null) return found;
        }
        return null;
    }

    private static TreeNode? FindNode(TreeNode node, int mapId)
    {
        if (node.Tag is MvMapDescriptor map && map.Id == mapId) return node;
        foreach (TreeNode child in node.Nodes)
        {
            TreeNode? found = FindNode(child, mapId);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (TreeNode child in EnumerateNodes(node.Nodes)) yield return child;
        }
    }

    private MvMapDescriptor? SelectedMap => _tree.SelectedNode?.Tag as MvMapDescriptor;
    private static MvDatabaseFileSummary GetMapInfos(MvProjectSession session) => session.Workspace.Snapshot.Database.Find(MvDocumentKind.MapInfos)
        ?? throw new FileNotFoundException("MapInfos.json was not found.");

    private void OnWorkspaceChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceChanged(sender, e))) return;
        if (ReferenceEquals(_displayedSession, e.Session)) return;
        _displayedSession = e.Session;
        Rebuild();
    }

    private static DocumentRequest MapViewRequest(MvMapDescriptor map) => new(
        EditorDocumentKind.MapView,
        $"map-view:{map.Id}",
        map.RelativePath,
        MapId: map.Id,
        DisplayTitle: map.Name);

    private static DocumentRequest MapPropertiesRequest(MvMapDescriptor map) => new(
        EditorDocumentKind.MapProperties,
        $"map-properties:{map.Id}",
        map.RelativePath,
        MapId: map.Id,
        DisplayTitle: map.Name + " Properties");
}

internal sealed class MapCreateDialog : Form
{
    private readonly TextBox _name = new() { Width = 220 };
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 999, Width = 90 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 999, Width = 90 };
    private readonly NumericUpDown _parent = new() { Minimum = 0, Maximum = 9999, Width = 90 };

    public MapCreateDialog(string name, int width, int height, int parentId)
    {
        Text = "Create Map";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _name.Text = name;
        _width.Value = width;
        _height.Value = height;
        _parent.Value = parentId;
        TableLayoutPanel layout = new() { AutoSize = true, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
        AddRow(layout, 0, "Name:", _name);
        AddRow(layout, 1, "Width:", _width);
        AddRow(layout, 2, "Height:", _height);
        AddRow(layout, 3, "Parent map ID:", _parent);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button ok = new() { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string MapName => string.IsNullOrWhiteSpace(_name.Text) ? "New Map" : _name.Text.Trim();
    public int WidthValue => decimal.ToInt32(_width.Value);
    public int HeightValue => decimal.ToInt32(_height.Value);
    public int ParentId => decimal.ToInt32(_parent.Value);

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}

internal enum MapDeleteMode
{
    MoveChildrenToParent,
    MoveChildrenToRoot,
    DeleteSubtree
}

internal sealed class MapDeleteDialog : Form
{
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };

    public MapDeleteDialog(MvMapDescriptor map, int childCount)
    {
        Text = "Delete Map";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _mode.Items.Add(new DeleteChoice(MapDeleteMode.MoveChildrenToParent, $"Move {childCount} child map(s) to parent {map.ParentId:D3}"));
        _mode.Items.Add(new DeleteChoice(MapDeleteMode.MoveChildrenToRoot, $"Move {childCount} child map(s) to project root"));
        _mode.Items.Add(new DeleteChoice(MapDeleteMode.DeleteSubtree, "Delete this map and its complete subtree"));
        _mode.SelectedIndex = 0;
        _mode.Enabled = childCount > 0;
        TableLayoutPanel layout = new() { AutoSize = true, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        layout.Controls.Add(new Label { Text = $"Map {map.Id:D3} {map.Name}", AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label { Text = childCount == 0 ? "This map has no child maps." : "Choose how child maps should be handled:", AutoSize = true }, 0, 1);
        layout.Controls.Add(_mode, 0, 2);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button delete = new() { Text = "Continue", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(delete);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = delete;
        CancelButton = cancel;
    }

    public MapDeleteMode Mode => _mode.SelectedItem is DeleteChoice choice ? choice.Mode : MapDeleteMode.MoveChildrenToParent;

    private sealed record DeleteChoice(MapDeleteMode Mode, string Text)
    {
        public override string ToString() => Text;
    }
}

internal static class Prompt
{
    public static string? Show(IWin32Window owner, string label, string title, string value)
    {
        using Form form = new() { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        TextBox text = new() { Text = value, Width = 280 };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        TableLayoutPanel layout = new() { AutoSize = true, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        layout.Controls.Add(new Label { Text = label, AutoSize = true }, 0, 0);
        layout.Controls.Add(text, 1, 0);
        layout.Controls.Add(ok, 0, 1);
        layout.Controls.Add(cancel, 1, 1);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }
}
