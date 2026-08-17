using Playroma.Sprite.Core;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class LayersPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true };
    private readonly TextBox nameBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox visibleBox = new() { Text = "Visible", AutoSize = true };
    private readonly CheckBox lockedBox = new() { Text = "Locked", AutoSize = true };
    private readonly CheckBox continuousBox = new() { Text = "Continuous linked cels", AutoSize = true };
    private readonly NumericUpDown opacityBox = new() { Minimum = 0, Maximum = 255, Value = 255, Dock = DockStyle.Fill };
    private readonly ComboBox blendBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label kindLabel = new() { AutoSize = true };
    private bool suppress;

    public LayersPanel(SpriteEditorSession editorSession)
    {
        session = editorSession ?? throw new ArgumentNullException(nameof(editorSession));
        Dock = DockStyle.Fill;
        blendBox.Items.AddRange(SpriteBlendModes.All.Cast<object>().ToArray());
        Controls.Add(BuildLayout());
        tree.AfterSelect += (_, e) => { if (!suppress && e.Node?.Tag is Guid id) session.SetActiveLayer(id); };
        nameBox.Leave += (_, _) => CommitName();
        nameBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { CommitName(); e.SuppressKeyPress = true; } };
        visibleBox.CheckedChanged += (_, _) => { if (!suppress) session.UpdateActiveLayer(visible: visibleBox.Checked); };
        lockedBox.CheckedChanged += (_, _) => { if (!suppress) session.UpdateActiveLayer(locked: lockedBox.Checked); };
        continuousBox.CheckedChanged += (_, _) => { if (!suppress) session.UpdateActiveLayer(continuous: continuousBox.Checked); };
        opacityBox.ValueChanged += (_, _) => { if (!suppress) session.UpdateActiveLayer(opacity: (byte)opacityBox.Value); };
        blendBox.SelectedIndexChanged += (_, _) => { if (!suppress && blendBox.SelectedItem is string mode) session.UpdateActiveLayer(blendMode: mode); };
        session.Changed += (_, _) => RefreshState();
        RefreshState();
    }

    public void RefreshState()
    {
        if (IsDisposed || session.Document.Layers.Count == 0) return;
        suppress = true;
        try
        {
            tree.BeginUpdate(); tree.Nodes.Clear();
            foreach (SpriteLayer layer in Children(null)) tree.Nodes.Add(CreateNode(layer));
            tree.ExpandAll();
            TreeNode? selectedNode = FindNode(tree.Nodes, session.SelectedLayerId);
            if (selectedNode is not null) tree.SelectedNode = selectedNode;
            SpriteLayer selected = session.SelectedLayer;
            nameBox.Text = selected.Name; visibleBox.Checked = selected.Visible; lockedBox.Checked = selected.Locked; continuousBox.Checked = selected.Continuous;
            opacityBox.Value = selected.Opacity; blendBox.SelectedItem = SpriteBlendModes.IsSupported(selected.BlendMode) ? selected.BlendMode : SpriteBlendModes.Normal;
            kindLabel.Text = selected.Kind == SpriteLayerKind.Group ? "Group container" : $"{selected.Kind} layer";
            nameBox.Enabled = true; opacityBox.Enabled = selected.Kind != SpriteLayerKind.Group; blendBox.Enabled = selected.Kind != SpriteLayerKind.Group; continuousBox.Enabled = selected.Kind != SpriteLayerKind.Group;
        }
        finally { tree.EndUpdate(); suppress = false; }
    }

    private Control BuildLayout()
    {
        FlowLayoutPanel commands = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(3) };
        commands.Controls.Add(Button("+ Layer", (_, _) => Add(SpriteLayerKind.Image)));
        commands.Controls.Add(Button("+ Background", (_, _) => Add(SpriteLayerKind.Background)));
        commands.Controls.Add(Button("+ Group", (_, _) => session.AddLayer(SpriteLayerKind.Group, parentId: ParentForNewLayer())));
        commands.Controls.Add(Button("+ Reference", (_, _) => Add(SpriteLayerKind.Reference)));
        commands.Controls.Add(Button("Duplicate", (_, _) => session.DuplicateActiveLayer()));
        commands.Controls.Add(Button("Delete", (_, _) => session.DeleteActiveLayer()));
        commands.Controls.Add(Button("▲", (_, _) => session.MoveActiveLayer(1)));
        commands.Controls.Add(Button("▼", (_, _) => session.MoveActiveLayer(-1)));
        commands.Controls.Add(Button("Group", (_, _) => session.GroupActiveLayer()));
        commands.Controls.Add(Button("Merge Down", (_, _) => TryAction(session.MergeDown)));
        commands.Controls.Add(Button("Flatten", (_, _) => TryAction(() => session.Flatten())));

        TableLayoutPanel properties = new() { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(5) };
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); properties.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(properties, 0, "Name", nameBox); AddRow(properties, 1, "Type", kindLabel);
        FlowLayoutPanel checks = new() { AutoSize = true, Dock = DockStyle.Fill }; checks.Controls.AddRange([visibleBox, lockedBox]); AddRow(properties, 2, "State", checks);
        AddRow(properties, 3, "Animation", continuousBox); AddRow(properties, 4, "Opacity", opacityBox); AddRow(properties, 5, "Blend", blendBox);

        Panel root = new() { Dock = DockStyle.Fill }; root.Controls.Add(tree); root.Controls.Add(properties); root.Controls.Add(commands); return root;
    }

    private void Add(SpriteLayerKind kind)
    {
        Guid? parent = ParentForNewLayer(); session.AddLayer(kind, parentId: parent);
    }

    private Guid? ParentForNewLayer()
    {
        SpriteLayer selected = session.SelectedLayer;
        return selected.Kind == SpriteLayerKind.Group ? selected.Id : selected.ParentId;
    }

    private IEnumerable<SpriteLayer> Children(Guid? parentId) => session.Document.Layers.Where(layer => layer.ParentId == parentId).Reverse();

    private TreeNode CreateNode(SpriteLayer layer)
    {
        string kind = layer.Kind == SpriteLayerKind.Tilemap ? "▦ " : layer.Kind == SpriteLayerKind.Group ? "▾ " : string.Empty;
        string state = $"{(layer.Visible ? "●" : "○")} {kind}{(layer.Locked ? "🔒 " : string.Empty)}{(layer.Continuous ? "↔ " : string.Empty)}";
        TreeNode node = new(state + layer.Name) { Tag = layer.Id, ToolTipText = $"{layer.Kind}; {layer.BlendMode}; opacity {layer.Opacity}" };
        foreach (SpriteLayer child in Children(layer.Id)) node.Nodes.Add(CreateNode(child));
        return node;
    }

    private static TreeNode? FindNode(TreeNodeCollection nodes, Guid id)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is Guid candidate && candidate == id) return node;
            TreeNode? nested = FindNode(node.Nodes, id); if (nested is not null) return nested;
        }
        return null;
    }

    private void CommitName()
    {
        if (!suppress) session.UpdateActiveLayer(name: nameBox.Text);
    }

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Layer operation", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private static Button Button(string text, EventHandler click)
    {
        Button button = new() { Text = text, AutoSize = true, Margin = new Padding(2) }; button.Click += click; return button;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, row);
        control.Margin = new Padding(3); table.Controls.Add(control, 1, row);
    }
}
