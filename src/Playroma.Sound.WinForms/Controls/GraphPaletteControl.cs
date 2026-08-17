using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;

namespace Playroma.Sound.WinForms.Controls;

public sealed class NodeTypeSelectedEventArgs : EventArgs
{
    public NodeTypeSelectedEventArgs(NodeTypeDescriptor descriptor) => Descriptor = descriptor;
    public NodeTypeDescriptor Descriptor { get; }
}

public sealed class GraphPaletteControl : UserControl
{
    private readonly TextBox _search;
    private readonly TreeView _tree;
    private readonly NodeTypeDescriptor[] _descriptors;

    public GraphPaletteControl(GraphNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _descriptors = registry.Descriptors.OrderBy(item => item.Category).ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _search = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search nodes..." };
        _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, BorderStyle = BorderStyle.None };
        Controls.Add(_tree);
        Controls.Add(_search);
        _search.TextChanged += (_, _) => RefreshTree();
        _search.KeyDown += OnSearchKeyDown;
        _tree.NodeMouseDoubleClick += (_, eventArgs) => Activate(eventArgs.Node);
        _tree.KeyDown += (_, eventArgs) => { if (eventArgs.KeyCode == Keys.Enter) Activate(_tree.SelectedNode); };
        RefreshTree();
    }

    public event EventHandler<NodeTypeSelectedEventArgs>? NodeTypeActivated;

    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }

    private void RefreshTree()
    {
        string query = _search.Text.Trim();
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (IGrouping<GraphNodeCategory, NodeTypeDescriptor> category in _descriptors
                .Where(item => Matches(item, query))
                .GroupBy(item => item.Category))
            {
                var parent = new TreeNode(category.Key.ToString()) { NodeFont = new Font(_tree.Font, FontStyle.Bold) };
                foreach (NodeTypeDescriptor descriptor in category)
                    parent.Nodes.Add(new TreeNode(descriptor.DisplayName) { Tag = descriptor, ToolTipText = descriptor.Description });
                _tree.Nodes.Add(parent);
                parent.Expand();
            }
        }
        finally { _tree.EndUpdate(); }
    }

    private static bool Matches(NodeTypeDescriptor descriptor, string query) => string.IsNullOrEmpty(query) ||
        descriptor.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        descriptor.TypeId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        descriptor.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        descriptor.Category.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Down && _tree.Nodes.Count > 0)
        {
            _tree.Focus();
            _tree.SelectedNode = FirstLeaf(_tree.Nodes[0]);
            eventArgs.SuppressKeyPress = true;
        }
        else if (eventArgs.KeyCode == Keys.Enter)
        {
            TreeNode? first = _tree.Nodes.Count == 0 ? null : FirstLeaf(_tree.Nodes[0]);
            Activate(first);
            eventArgs.SuppressKeyPress = true;
        }
    }

    private void Activate(TreeNode? node)
    {
        if (node?.Tag is NodeTypeDescriptor descriptor) NodeTypeActivated?.Invoke(this, new NodeTypeSelectedEventArgs(descriptor));
    }

    private static TreeNode FirstLeaf(TreeNode node) => node.Nodes.Count == 0 ? node : FirstLeaf(node.Nodes[0]);
}
