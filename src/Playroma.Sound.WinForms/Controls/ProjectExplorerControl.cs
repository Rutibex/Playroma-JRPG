using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Workspace;

namespace Playroma.Sound.WinForms.Controls;

public sealed class AssetActivatedEventArgs : EventArgs
{
    public AssetActivatedEventArgs(string assetId) => AssetId = assetId;
    public string AssetId { get; }
}

public sealed class AssetSelectedEventArgs : EventArgs
{
    public AssetSelectedEventArgs(ProjectAssetEntry? asset) => Asset = asset;
    public ProjectAssetEntry? Asset { get; }
}

public sealed class ProjectExplorerControl : UserControl
{
    private readonly ToolStrip _toolStrip;
    private readonly ToolStripButton _refreshButton;
    private readonly ToolStripTextBox _searchBox;
    private readonly TreeView _tree;
    private readonly Font _boldFont;
    private AuthoringWorkspace? _workspace;

    public ProjectExplorerControl()
    {
        Dock = DockStyle.Fill;
        _boldFont = new Font(Font, FontStyle.Bold);
        _toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _refreshButton = new ToolStripButton("Refresh") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _searchBox = new ToolStripTextBox { AutoSize = false, Width = 150, ToolTipText = "Filter project assets" };
        _toolStrip.Items.Add(_refreshButton);
        _toolStrip.Items.Add(new ToolStripLabel("Find:"));
        _toolStrip.Items.Add(_searchBox);

        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            FullRowSelect = true,
            ShowNodeToolTips = true
        };

        Controls.Add(_tree);
        Controls.Add(_toolStrip);

        _refreshButton.Click += (_, _) => RefreshTree();
        _searchBox.TextChanged += (_, _) => RefreshTree();
        _tree.NodeMouseDoubleClick += (_, eventArgs) => ActivateNode(eventArgs.Node);
        _tree.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            ActivateNode(_tree.SelectedNode);
            eventArgs.Handled = true;
        };
        _tree.AfterSelect += (_, _) => SelectedAssetChanged?.Invoke(this, new AssetSelectedEventArgs(GetSelectedAsset()));
    }

    public event EventHandler<AssetActivatedEventArgs>? AssetActivated;
    public event EventHandler<AssetSelectedEventArgs>? SelectedAssetChanged;

    public string? SelectedAssetId => (_tree.SelectedNode?.Tag as AssetNodeTag)?.AssetId;

    public void SetWorkspace(AuthoringWorkspace? workspace)
    {
        _workspace = workspace;
        RefreshTree();
    }

    public void RefreshTree()
    {
        string? selection = SelectedAssetId;
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            if (_workspace is null)
            {
                _tree.Nodes.Add(new TreeNode("No project open") { ForeColor = SystemColors.GrayText });
                return;
            }

            string filter = _searchBox.Text.Trim();
            var root = new TreeNode(_workspace.Project.DisplayName)
            {
                NodeFont = _boldFont,
                ToolTipText = _workspace.ProjectFilePath
            };
            _tree.Nodes.Add(root);

            foreach ((AuthoringAssetKind kind, string title) in Groups())
            {
                ProjectAssetEntry[] assets = _workspace.Project.Assets
                    .Where(item => item.Kind == kind && Matches(item, filter))
                    .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .ToArray();
                if (assets.Length == 0) continue;

                var group = new TreeNode($"{title} ({assets.Length})") { NodeFont = _boldFont };
                root.Nodes.Add(group);
                foreach (ProjectAssetEntry asset in assets)
                {
                    bool isOpen = _workspace.OpenDocuments.Any(item => StringComparer.Ordinal.Equals(item.DocumentId, asset.Id));
                    bool isDirty = _workspace.OpenDocuments.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.DocumentId, asset.Id))?.IsDirty == true;
                    string suffix = isDirty ? " *" : isOpen ? " •" : "";
                    string fullPath = Path.GetFullPath(Path.Combine(_workspace.ProjectRoot, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    var node = new TreeNode(asset.DisplayName + suffix)
                    {
                        Tag = new AssetNodeTag(asset.Id),
                        ToolTipText = $"{asset.Id}{Environment.NewLine}{asset.RelativePath}",
                        ForeColor = File.Exists(fullPath) ? SystemColors.WindowText : Color.Firebrick
                    };
                    group.Nodes.Add(node);
                    if (StringComparer.Ordinal.Equals(asset.Id, selection)) _tree.SelectedNode = node;
                }
                group.Expand();
            }

            if (root.Nodes.Count == 0) root.Nodes.Add(new TreeNode("No matching assets") { ForeColor = SystemColors.GrayText });
            root.Expand();
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private ProjectAssetEntry? GetSelectedAsset()
    {
        if (_workspace is null || _tree.SelectedNode?.Tag is not AssetNodeTag tag) return null;
        return _workspace.Project.Assets.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, tag.AssetId));
    }

    private void ActivateNode(TreeNode? node)
    {
        if (node?.Tag is AssetNodeTag tag) AssetActivated?.Invoke(this, new AssetActivatedEventArgs(tag.AssetId));
    }

    private static bool Matches(ProjectAssetEntry asset, string filter) => string.IsNullOrWhiteSpace(filter)
        || asset.Id.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || asset.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || asset.RelativePath.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || asset.Tags.Any(item => item.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

    private static IEnumerable<(AuthoringAssetKind Kind, string Title)> Groups()
    {
        yield return (AuthoringAssetKind.SoundGraph, "Sound Graphs");
        yield return (AuthoringAssetKind.GeneratorRequest, "Generator Requests");
        yield return (AuthoringAssetKind.StreamingScene, "Streaming Scenes");
        yield return (AuthoringAssetKind.Sample, "Samples");
        yield return (AuthoringAssetKind.ImpulseResponse, "Impulse Responses");
        yield return (AuthoringAssetKind.Other, "Other Assets");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _boldFont.Dispose();
        base.Dispose(disposing);
    }

    private sealed record AssetNodeTag(string AssetId);
}
