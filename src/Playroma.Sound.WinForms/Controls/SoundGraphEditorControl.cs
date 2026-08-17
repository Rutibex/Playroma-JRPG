using System.Text.Json;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Graphs;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.WinForms.Playback;

namespace Playroma.Sound.WinForms.Controls;

public sealed class SoundGraphEditorControl : UserControl, IAuthoringEditorControl
{
    private const string ClipboardFormat = "Playroma.Sound.GraphNodes.v1";
    private readonly SoundGraphDocument _document;
    private readonly AuthoringWorkspace _workspace;
    private readonly DocumentPreviewSession _preview;
    private readonly GraphNodeRegistry _registry = BuiltInGraphRegistry.CreateDefault();
    private readonly SoundGraphSerializer _serializer = new();
    private readonly GraphPaletteControl _palette;
    private readonly GraphCanvasControl _canvas;
    private readonly GraphMiniMapControl _miniMap;
    private readonly DescriptorParameterEditorControl _parameters;
    private readonly DocumentEditorControl _rawEditor;
    private readonly TabControl _views;
    private readonly TabPage _visualPage;
    private readonly Label _selectionLabel;
    private readonly Label _diagnosticLabel;
    private readonly TextBox _nodeId;
    private readonly TextBox _nodeComment;
    private readonly CheckBox _nodeCollapsed;
    private readonly CheckBox _nodeBypassed;
    private readonly Button _setOutput;
    private readonly ListBox _macroList;
    private readonly PropertyGrid _macroProperties;
    private readonly System.Windows.Forms.Timer _viewCommitTimer;
    private SoundGraphDefinition _workingGraph;
    private GraphNodePosition _pendingAddPosition = new(80, 80);
    private bool _refreshing;
    private bool _committing;

    public SoundGraphEditorControl(SoundGraphDocument document, AuthoringWorkspace workspace, DocumentPreviewSession preview)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _workingGraph = document.CreateEditableCopy();
        Dock = DockStyle.Fill;

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        ToolStripButton add = Button("Add", "Focus the searchable node palette (Ctrl+Space)");
        ToolStripButton remove = Button("Delete", "Delete selected nodes");
        ToolStripButton duplicate = Button("Duplicate", "Duplicate selected nodes");
        ToolStripButton copy = Button("Copy", "Copy selected nodes");
        ToolStripButton paste = Button("Paste", "Paste nodes at the view centre");
        ToolStripButton disconnect = Button("Disconnect", "Remove connections touching selected nodes");
        ToolStripButton group = Button("Group", "Add a visual group around selected nodes");
        ToolStripButton comment = Button("Comment", "Add a comment card");
        ToolStripButton layout = Button("Auto Layout", "Arrange nodes by signal flow");
        ToolStripButton fit = Button("Fit", "Fit the whole graph (Ctrl+F)");
        ToolStripButton grid = Button("Grid", "Show or hide the graph grid");
        grid.CheckOnClick = true;
        ToolStripButton snap = Button("Snap", "Snap node movement to the grid; hold Alt to bypass temporarily");
        snap.CheckOnClick = true;
        ToolStripButton validate = Button("Validate", "Validate the graph");
        ToolStripButton compile = Button("Compile", "Compile the graph");
        ToolStripButton play = Button("Play", "Preview the complete graph");
        ToolStripButton audition = Button("Audition Node", "Preview the selected node and everything feeding it");
        tools.Items.AddRange([add, remove, duplicate, new ToolStripSeparator(), copy, paste, disconnect, new ToolStripSeparator(), group, comment, layout, fit, grid, snap, new ToolStripSeparator(), validate, compile, play, audition]);

        _palette = new GraphPaletteControl(_registry) { Dock = DockStyle.Fill };
        _canvas = new GraphCanvasControl(_registry) { Dock = DockStyle.Fill };
        _miniMap = new GraphMiniMapControl { Width = 190, Height = 125, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Canvas = _canvas };
        var canvasHost = new Panel { Dock = DockStyle.Fill };
        canvasHost.Controls.Add(_canvas);
        canvasHost.Controls.Add(_miniMap);
        canvasHost.Resize += (_, _) => _miniMap.Location = new Point(Math.Max(0, canvasHost.ClientSize.Width - _miniMap.Width - 12), Math.Max(0, canvasHost.ClientSize.Height - _miniMap.Height - 12));
        _miniMap.Location = new Point(Math.Max(0, canvasHost.ClientSize.Width - _miniMap.Width - 12), Math.Max(0, canvasHost.ClientSize.Height - _miniMap.Height - 12));
        _miniMap.BringToFront();

        _selectionLabel = new Label { Dock = DockStyle.Top, Height = 47, Padding = new Padding(6), Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), AutoEllipsis = true };
        _diagnosticLabel = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(6, 5, 6, 2), ForeColor = SystemColors.GrayText, AutoEllipsis = true };
        _nodeId = new TextBox { Dock = DockStyle.Top };
        _nodeComment = new TextBox { Dock = DockStyle.Top, Multiline = true, Height = 55, ScrollBars = ScrollBars.Vertical };
        _nodeCollapsed = new CheckBox { Text = "Collapsed", AutoSize = true };
        _nodeBypassed = new CheckBox { Text = "Bypassed", AutoSize = true };
        _setOutput = new Button { Text = "Set as graph output", Dock = DockStyle.Top, Height = 28 };
        _parameters = new DescriptorParameterEditorControl { Dock = DockStyle.Fill };

        var nodeIdentity = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(6) };
        nodeIdentity.Controls.Add(new Label { Text = "Node ID", AutoSize = true });
        nodeIdentity.Controls.Add(_nodeId);
        nodeIdentity.Controls.Add(new Label { Text = "Comment", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        nodeIdentity.Controls.Add(_nodeComment);
        var flags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        flags.Controls.AddRange([_nodeCollapsed, _nodeBypassed]);
        nodeIdentity.Controls.Add(flags);
        nodeIdentity.Controls.Add(_setOutput);

        var inspectorHost = new Panel { Dock = DockStyle.Fill };
        inspectorHost.Controls.Add(_parameters);
        inspectorHost.Controls.Add(nodeIdentity);
        inspectorHost.Controls.Add(_selectionLabel);
        inspectorHost.Controls.Add(_diagnosticLabel);

        var macroTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addMacro = new Button { Text = "Add Macro", AutoSize = true };
        var deleteMacro = new Button { Text = "Delete", AutoSize = true };
        macroTools.Controls.AddRange([addMacro, deleteMacro]);
        _macroList = new ListBox { Dock = DockStyle.Top, Height = 145 };
        _macroProperties = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
        var macroPanel = new Panel { Dock = DockStyle.Fill };
        macroPanel.Controls.Add(_macroProperties);
        macroPanel.Controls.Add(_macroList);
        macroPanel.Controls.Add(macroTools);
        var inspectorTabs = new TabControl { Dock = DockStyle.Fill };
        var nodePage = new TabPage("Node"); nodePage.Controls.Add(inspectorHost);
        var macroPage = new TabPage("Macros"); macroPage.Controls.Add(macroPanel);
        inspectorTabs.TabPages.AddRange([nodePage, macroPage]);

        var visualSplit = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1280, 760), Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 170, Panel2MinSize = 500, SplitterDistance = 225 };
        visualSplit.Panel1.Controls.Add(_palette);
        var canvasSplit = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1050, 760), Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2, Panel1MinSize = 400, Panel2MinSize = 235, SplitterDistance = 770 };
        canvasSplit.Panel1.Controls.Add(canvasHost);
        canvasSplit.Panel2.Controls.Add(inspectorTabs);
        visualSplit.Panel2.Controls.Add(canvasSplit);
        _visualPage = new TabPage("Visual Graph");
        _visualPage.Controls.Add(visualSplit);

        _rawEditor = new DocumentEditorControl(_document);
        _rawEditor.ValidateRequested += (_, eventArgs) => ValidateRequested?.Invoke(this, eventArgs);
        _rawEditor.CompileRequested += (_, eventArgs) => CompileRequested?.Invoke(this, eventArgs);
        _rawEditor.PlayRequested += (_, eventArgs) => PlayRequested?.Invoke(this, eventArgs);
        _rawEditor.EditorStateChanged += (_, _) => EditorStateChanged?.Invoke(this, EventArgs.Empty);
        var rawPage = new TabPage("Raw JSON"); rawPage.Controls.Add(_rawEditor);
        _views = new TabControl { Dock = DockStyle.Fill };
        _views.TabPages.AddRange([_visualPage, rawPage]);
        Controls.Add(_views);
        Controls.Add(tools);

        add.Click += (_, _) => _palette.FocusSearch();
        remove.Click += (_, _) => RemoveSelected();
        duplicate.Click += (_, _) => DuplicateSelected();
        copy.Click += (_, _) => CopySelected();
        paste.Click += (_, _) => PasteNodes();
        disconnect.Click += (_, _) => DisconnectSelected();
        group.Click += (_, _) => AddGroup();
        comment.Click += (_, _) => AddComment();
        layout.Click += (_, _) => Commit("Auto layout graph", graph => VisualGraphEditing.AutoLayout(graph, _registry));
        fit.Click += (_, _) => _canvas.FitAll();
        grid.CheckedChanged += (_, _) => { if (!_refreshing) Commit("Toggle graph grid", graph => graph.Editor.ShowGrid = grid.Checked, "graph-grid"); };
        snap.CheckedChanged += (_, _) => { if (!_refreshing) Commit("Toggle grid snapping", graph => graph.Editor.SnapToGrid = snap.Checked, "graph-snap"); };
        validate.Click += (_, _) => ValidateRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        compile.Click += (_, _) => CompileRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        play.Click += (_, _) => PlayRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        audition.Click += async (_, _) => await AuditionSelectedAsync();

        _palette.NodeTypeActivated += (_, eventArgs) => AddNode(eventArgs.Descriptor.TypeId, _pendingAddPosition);
        _canvas.AddNodeRequested += (_, eventArgs) => { _pendingAddPosition = eventArgs.Position; _palette.FocusSearch(); };
        _canvas.ConnectionRequested += OnConnectionRequested;
        _canvas.NodePositionsCommitted += (_, eventArgs) => Commit("Move graph nodes", graph => VisualGraphEditing.MoveNodes(graph, eventArgs.Positions), "move-nodes");
        _canvas.SelectionChanged += (_, _) => RefreshInspector();
        _canvas.NodeDoubleClicked += (_, eventArgs) => { _canvas.SelectNodes([eventArgs.NodeId]); RefreshInspector(); };
        _canvas.KeyDown += OnCanvasKeyDown;

        _nodeId.Validated += (_, _) => CommitNodeIdentity();
        _nodeComment.Validated += (_, _) => CommitNodeIdentity();
        _nodeCollapsed.CheckedChanged += (_, _) => { if (!_refreshing) CommitNodeFlags(); };
        _nodeBypassed.CheckedChanged += (_, _) => { if (!_refreshing) CommitNodeFlags(); };
        _setOutput.Click += (_, _) => SetSelectedAsOutput();
        _parameters.ParameterChanged += OnParameterChanged;
        _parameters.ExposeMacroRequested += (_, eventArgs) => ExposeMacro(eventArgs.Descriptor);
        addMacro.Click += (_, _) => AddMacro();
        deleteMacro.Click += (_, _) => DeleteMacro();
        _macroList.SelectedIndexChanged += (_, _) => RefreshMacroProperties();
        _macroProperties.PropertyValueChanged += (_, eventArgs) => { if (!_refreshing) Commit("Edit macro " + (eventArgs.ChangedItem?.PropertyDescriptor?.Name ?? "property"), graph => CopyMacros(_workingGraph, graph)); };

        _document.Changed += OnDocumentChanged;
        _viewCommitTimer = new System.Windows.Forms.Timer { Interval = 650 };
        _viewCommitTimer.Tick += (_, _) => { _viewCommitTimer.Stop(); CommitView(); };
        _canvas.ViewChanged += (_, _) => { _viewCommitTimer.Stop(); _viewCommitTimer.Start(); };
        RefreshStructured(false);
        _refreshing = true;
        try
        {
            grid.Checked = _workingGraph.Editor.ShowGrid;
            snap.Checked = _workingGraph.Editor.SnapToGrid;
        }
        finally { _refreshing = false; }
    }

    public event EventHandler<DocumentActionEventArgs>? ValidateRequested;
    public event EventHandler<DocumentActionEventArgs>? CompileRequested;
    public event EventHandler<DocumentActionEventArgs>? PlayRequested;
    public event EventHandler? EditorStateChanged;

    public IAuthoringDocument Document => _document;
    public bool HasUnappliedChanges => _rawEditor.HasUnappliedChanges;

    public bool TryApplyEdits(bool showError = true)
    {
        bool result = _rawEditor.TryApplyEdits(showError);
        if (result) RefreshStructured();
        return result;
    }

    public void RefreshFromDocument(bool force)
    {
        _rawEditor.RefreshFromDocument(force);
        if (force || !HasUnappliedChanges) RefreshStructured();
    }

    public void SetValidation(ValidationSummary summary)
    {
        _rawEditor.SetValidation(summary);
        int errors = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Error);
        int warnings = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Warning);
        _diagnosticLabel.Text = $"Validation: {errors} error(s), {warnings} warning(s)";
        _diagnosticLabel.ForeColor = errors > 0 ? Color.Firebrick : warnings > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
    }

    public void SetCompilationState(AuthoringCompilationResult result)
    {
        _rawEditor.SetCompilationState(result);
        _diagnosticLabel.Text = $"Compilation: {result.State} in {result.Duration.TotalMilliseconds:0.0} ms";
        _diagnosticLabel.ForeColor = result.Succeeded ? Color.DarkGreen : Color.Firebrick;
    }

    public void SelectDiagnostic(AuthoringDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!string.IsNullOrWhiteSpace(diagnostic.ObjectId) && _workingGraph.Nodes.Any(node => node.Id == diagnostic.ObjectId))
        {
            _views.SelectedTab = _visualPage;
            _canvas.SelectDiagnosticNode(diagnostic.ObjectId);
            _diagnosticLabel.Text = $"{diagnostic.Code}: {diagnostic.Message}";
            _diagnosticLabel.ForeColor = diagnostic.Severity == AuthoringDiagnosticSeverity.Error ? Color.Firebrick : Color.DarkGoldenrod;
        }
        else
        {
            _rawEditor.SelectDiagnostic(diagnostic);
            _views.SelectedIndex = 1;
        }
    }

    public void FormatJson() => _rawEditor.FormatJson();

    private void AddNode(string typeId, GraphNodePosition position)
    {
        string created = "";
        Commit("Add graph node", graph => created = VisualGraphEditing.AddNode(graph, _registry, typeId, position).Id);
        _canvas.SelectNodes([created], true);
    }

    private void RemoveSelected()
    {
        string[] selected = _canvas.SelectedNodeIds.ToArray();
        if (selected.Length == 0) return;
        Commit(selected.Length == 1 ? "Delete graph node" : "Delete graph nodes", graph => VisualGraphEditing.RemoveNodes(graph, selected));
        _canvas.SelectNodes([]);
    }

    private void DuplicateSelected()
    {
        string[] selected = _canvas.SelectedNodeIds.ToArray();
        if (selected.Length == 0) return;
        string[] created = [];
        Commit("Duplicate graph nodes", graph => created = VisualGraphEditing.DuplicateNodes(graph, selected).Select(node => node.Id).ToArray());
        _canvas.SelectNodes(created, true);
    }

    private void DisconnectSelected()
    {
        var ids = new HashSet<string>(_canvas.SelectedNodeIds, StringComparer.Ordinal);
        if (ids.Count == 0) return;
        Commit("Disconnect graph nodes", graph => graph.Connections.RemoveAll(connection => ids.Contains(connection.FromNodeId) || ids.Contains(connection.ToNodeId)));
    }

    private void CopySelected()
    {
        if (_canvas.SelectedNodeIds.Count == 0) return;
        GraphClipboardPackage package = VisualGraphEditing.Copy(_workingGraph, _canvas.SelectedNodeIds);
        string json = JsonSerializer.Serialize(package, _serializer.Options);
        var data = new DataObject();
        data.SetData(ClipboardFormat, json);
        data.SetText(json);
        Clipboard.SetDataObject(data, true);
        _diagnosticLabel.Text = $"Copied {package.Nodes.Count} node(s).";
    }

    private void PasteNodes()
    {
        IDataObject? clipboard = Clipboard.GetDataObject();
        string? json = clipboard?.GetDataPresent(ClipboardFormat) == true
            ? clipboard.GetData(ClipboardFormat) as string
            : null;
        if (string.IsNullOrWhiteSpace(json)) json = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            GraphClipboardPackage package = JsonSerializer.Deserialize<GraphClipboardPackage>(json, _serializer.Options) ?? throw new JsonException("The clipboard package is empty.");
            string[] created = [];
            GraphNodePosition anchor = _canvas.ScreenToGraph(new Point(_canvas.ClientSize.Width / 2, _canvas.ClientSize.Height / 2));
            Commit("Paste graph nodes", graph => created = VisualGraphEditing.Paste(graph, package, anchor).Select(node => node.Id).ToArray());
            _canvas.SelectNodes(created, true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, exception.Message, "Cannot paste graph nodes", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddGroup()
    {
        GraphNodePosition position = CenterPosition();
        Commit("Add graph group", graph =>
        {
            GraphGroupDefinition group = VisualGraphEditing.AddGroup(graph, position);
            NodeDefinition[] selected = graph.Nodes.Where(node => _canvas.SelectedNodeIds.Contains(node.Id)).ToArray();
            if (selected.Length > 0)
            {
                float left = selected.Min(node => node.Editor?.X ?? 0) - 30;
                float top = selected.Min(node => node.Editor?.Y ?? 0) - 48;
                float right = selected.Max(node => (node.Editor?.X ?? 0) + 190) + 30;
                float bottom = selected.Max(node => (node.Editor?.Y ?? 0) + 100) + 30;
                group.X = left; group.Y = top; group.Width = right - left; group.Height = bottom - top;
            }
        });
    }

    private void AddComment() => Commit("Add graph comment", graph => VisualGraphEditing.AddComment(graph, CenterPosition()));

    private void OnConnectionRequested(object? sender, GraphConnectionRequestEventArgs eventArgs)
    {
        try
        {
            Commit("Connect graph nodes", graph => VisualGraphEditing.Connect(graph, _registry, eventArgs.FromNodeId, eventArgs.FromPortId, eventArgs.ToNodeId, eventArgs.ToPortId));
        }
        catch (InvalidOperationException exception)
        {
            _diagnosticLabel.Text = exception.Message;
            _diagnosticLabel.ForeColor = Color.Firebrick;
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    private void OnParameterChanged(object? sender, GraphParameterChangedEventArgs eventArgs)
    {
        string? id = _canvas.SelectedNodeIds.SingleOrDefault();
        if (id is null) return;
        Commit($"Edit {eventArgs.Descriptor.DisplayName}", graph => VisualGraphEditing.SetParameter(graph, id, eventArgs.Descriptor, eventArgs.Value), eventArgs.MergeKey);
    }

    private void CommitNodeIdentity()
    {
        if (_refreshing) return;
        string? oldId = _canvas.SelectedNodeIds.SingleOrDefault();
        if (oldId is null) return;
        string newId = _nodeId.Text.Trim();
        if (string.IsNullOrWhiteSpace(newId)) { RefreshInspector(); return; }
        try
        {
            Commit("Edit graph node identity", graph =>
            {
                if (oldId != newId) VisualGraphEditing.RenameNode(graph, oldId, newId);
                NodeDefinition node = graph.Nodes.First(item => item.Id == newId);
                node.Editor ??= new NodeEditorMetadata();
                node.Editor.Comment = _nodeComment.Text;
            });
            _canvas.SelectNodes([newId]);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot rename node", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshInspector();
        }
    }

    private void CommitNodeFlags()
    {
        string? id = _canvas.SelectedNodeIds.SingleOrDefault();
        if (id is null) return;
        bool collapsed = _nodeCollapsed.Checked;
        bool bypassed = _nodeBypassed.Checked;
        Commit("Edit graph node flags", graph =>
        {
            NodeDefinition node = graph.Nodes.First(item => item.Id == id);
            node.Editor ??= new NodeEditorMetadata();
            node.Editor.Collapsed = collapsed;
            node.Editor.Bypassed = bypassed;
        });
    }

    private void SetSelectedAsOutput()
    {
        string? id = _canvas.SelectedNodeIds.SingleOrDefault();
        if (id is null) return;
        NodeDefinition node = _workingGraph.Nodes.First(item => item.Id == id);
        NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(node.TypeId).Descriptor;
        if (descriptor.Category != GraphNodeCategory.Output)
        {
            MessageBox.Show(this, "The production graph output must be a node in the Output category. Use Audition Node to listen to an intermediate node.", "Cannot set output", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        GraphPortDescriptor port = descriptor.Ports.First(item => item.Direction == GraphPortDirection.Output);
        Commit("Set graph output", graph => { graph.Output.NodeId = id; graph.Output.PortId = port.Id; });
    }

    private void ExposeMacro(GraphParameterDescriptor descriptor)
    {
        string? nodeId = _canvas.SelectedNodeIds.SingleOrDefault();
        if (nodeId is null) return;
        Commit("Expose graph parameter as macro", graph =>
        {
            string baseId = nodeId + "-" + descriptor.Id;
            string id = baseId;
            int suffix = 2;
            while (graph.Macros.Any(macro => macro.Id == id)) id = baseId + "-" + suffix++;
            float minimum = (float)(descriptor.Minimum ?? 0);
            float maximum = (float)(descriptor.Maximum ?? 1);
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum >= maximum) { minimum = 0; maximum = 1; }
            NodeDefinition node = graph.Nodes.First(item => item.Id == nodeId);
            float current = node.Parameters.TryGetValue(descriptor.Id, out GraphValue value) ? value.AsFloat(minimum) : descriptor.DefaultValue.AsFloat(minimum);
            graph.Macros.Add(new MacroDefinition
            {
                Id = id,
                DisplayName = descriptor.DisplayName,
                Minimum = 0,
                Maximum = 1,
                DefaultValue = maximum == minimum ? 0 : Math.Clamp((current - minimum) / (maximum - minimum), 0, 1),
                Bindings = [new MacroBinding { TargetPath = $"node:{nodeId}/parameter:{descriptor.Id}", InputMinimum = 0, InputMaximum = 1, OutputMinimum = minimum, OutputMaximum = maximum }]
            });
        });
        RefreshMacros();
    }

    private void AddMacro()
    {
        Commit("Add graph macro", graph =>
        {
            string id = "macro"; int suffix = 2;
            while (graph.Macros.Any(item => item.Id == id)) id = "macro-" + suffix++;
            graph.Macros.Add(new MacroDefinition { Id = id, DisplayName = "Macro", Minimum = 0, Maximum = 1, DefaultValue = 0.5f });
        });
    }

    private void DeleteMacro()
    {
        if (_macroList.SelectedItem is not MacroDefinition selected) return;
        Commit("Delete graph macro", graph => graph.Macros.RemoveAll(item => item.Id == selected.Id));
    }

    private async Task AuditionSelectedAsync()
    {
        string? nodeId = _canvas.SelectedNodeIds.SingleOrDefault();
        if (nodeId is null)
        {
            MessageBox.Show(this, "Select exactly one node to audition.", "Audition node", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            SoundGraphDefinition graph = VisualGraphEditing.CreateAuditionGraph(_workingGraph, _registry, nodeId);
            AuthoringCompilationResult result = await _preview.PlayGraphAsync(_workspace, _document, graph);
            SetCompilationState(result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PreviewCompilationException or NotSupportedException)
        {
            MessageBox.Show(this, exception.Message, "Node audition failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshStructured(bool preserveSelection = true)
    {
        if (_committing) return;
        string[] selected = preserveSelection ? _canvas.SelectedNodeIds.ToArray() : [];
        _refreshing = true;
        try
        {
            _workingGraph = _document.CreateEditableCopy();
            _canvas.SetGraph(_workingGraph, false);
            if (selected.Length > 0) _canvas.SelectNodes(selected);
            RefreshInspector();
            RefreshMacros();
        }
        finally { _refreshing = false; }
    }

    private void RefreshInspector()
    {
        _refreshing = true;
        try
        {
            string[] selected = _canvas.SelectedNodeIds.ToArray();
            if (selected.Length != 1)
            {
                _selectionLabel.Text = selected.Length == 0 ? $"{_workingGraph.DisplayName}\r\n{_workingGraph.Nodes.Count} nodes, {_workingGraph.Connections.Count} connections" : $"{selected.Length} nodes selected";
                _nodeId.Enabled = _nodeComment.Enabled = _nodeCollapsed.Enabled = _nodeBypassed.Enabled = _setOutput.Enabled = false;
                _nodeId.Text = _nodeComment.Text = "";
                _nodeCollapsed.Checked = _nodeBypassed.Checked = false;
                _parameters.SetNode(null, null);
                return;
            }
            NodeDefinition node = _workingGraph.Nodes.First(item => item.Id == selected[0]);
            NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(node.TypeId).Descriptor;
            _selectionLabel.Text = $"{descriptor.DisplayName}\r\n{descriptor.TypeId}";
            _nodeId.Enabled = _nodeComment.Enabled = _nodeCollapsed.Enabled = true;
            int audioInputs = descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Input && (port.Type is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels));
            int audioOutputs = descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Output && (port.Type is GraphPortType.AudioMono or GraphPortType.AudioStereo or GraphPortType.AudioAnyChannels));
            _nodeBypassed.Enabled = audioInputs == 1 && audioOutputs == 1 && descriptor.Category != GraphNodeCategory.Output;
            _setOutput.Enabled = descriptor.Category == GraphNodeCategory.Output;
            _nodeId.Text = node.Id;
            _nodeComment.Text = node.Editor?.Comment ?? "";
            _nodeCollapsed.Checked = node.Editor?.Collapsed == true;
            _nodeBypassed.Checked = node.Editor?.Bypassed == true;
            _parameters.SetNode(node, descriptor);
        }
        finally { _refreshing = false; }
    }

    private void RefreshMacros()
    {
        string? selectedId = (_macroList.SelectedItem as MacroDefinition)?.Id;
        _macroList.BeginUpdate();
        try
        {
            _macroList.Items.Clear();
            foreach (MacroDefinition macro in _workingGraph.Macros) _macroList.Items.Add(macro);
            _macroList.DisplayMember = nameof(MacroDefinition.DisplayName);
            if (selectedId is not null)
            {
                for (int index = 0; index < _macroList.Items.Count; index++)
                    if ((_macroList.Items[index] as MacroDefinition)?.Id == selectedId) { _macroList.SelectedIndex = index; break; }
            }
        }
        finally { _macroList.EndUpdate(); }
        RefreshMacroProperties();
    }

    private void RefreshMacroProperties() => _macroProperties.SelectedObject = _macroList.SelectedItem;

    private void Commit(string description, Action<SoundGraphDefinition> mutation, string mergeKey = "")
    {
        if (_rawEditor.HasUnappliedChanges && !_rawEditor.TryApplyEdits()) return;
        _committing = true;
        try { _document.Mutate(description, mutation, mergeKey); }
        finally { _committing = false; }
        _workingGraph = _document.CreateEditableCopy();
        _canvas.SetGraph(_workingGraph);
        RefreshInspector();
        RefreshMacros();
        _rawEditor.RefreshFromDocument(true);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitView()
    {
        if (_rawEditor.HasUnappliedChanges) return;
        PointF pan = _canvas.Pan;
        float zoom = _canvas.Zoom;
        _committing = true;
        try { _document.Mutate("Change graph view", graph => { graph.Editor.ViewX = pan.X; graph.Editor.ViewY = pan.Y; graph.Editor.Zoom = zoom; }, "graph-view"); }
        finally { _committing = false; }
        _workingGraph.Editor.ViewX = pan.X;
        _workingGraph.Editor.ViewY = pan.Y;
        _workingGraph.Editor.Zoom = zoom;
        _rawEditor.RefreshFromDocument(true);
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => OnDocumentChanged(sender, eventArgs))); return; }
        if (!_committing) RefreshStructured();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCanvasKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Delete) { RemoveSelected(); eventArgs.SuppressKeyPress = true; }
        else if (eventArgs.Control && eventArgs.KeyCode == Keys.C) { CopySelected(); eventArgs.SuppressKeyPress = true; }
        else if (eventArgs.Control && eventArgs.KeyCode == Keys.V) { PasteNodes(); eventArgs.SuppressKeyPress = true; }
        else if (eventArgs.Control && eventArgs.KeyCode == Keys.D) { DuplicateSelected(); eventArgs.SuppressKeyPress = true; }
        else if (eventArgs.Control && eventArgs.KeyCode == Keys.Space) { _palette.FocusSearch(); eventArgs.SuppressKeyPress = true; }
    }

    private GraphNodePosition CenterPosition() => _canvas.ScreenToGraph(new Point(_canvas.ClientSize.Width / 2, _canvas.ClientSize.Height / 2));

    private static void CopyMacros(SoundGraphDefinition source, SoundGraphDefinition destination)
    {
        destination.Macros = source.Macros.Select(macro => new MacroDefinition
        {
            Id = macro.Id, DisplayName = macro.DisplayName, Minimum = macro.Minimum, Maximum = macro.Maximum, DefaultValue = macro.DefaultValue,
            Bindings = macro.Bindings.Select(binding => new MacroBinding { TargetPath = binding.TargetPath, InputMinimum = binding.InputMinimum, InputMaximum = binding.InputMaximum, OutputMinimum = binding.OutputMinimum, OutputMaximum = binding.OutputMaximum, Curve = binding.Curve, Steps = binding.Steps }).ToList()
        }).ToList();
    }

    private static ToolStripButton Button(string text, string tooltip) => new(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tooltip };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _viewCommitTimer.Stop();
            _viewCommitTimer.Dispose();
            _document.Changed -= OnDocumentChanged;
            _selectionLabel.Font.Dispose();
        }
        base.Dispose(disposing);
    }
}
