using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Scenes;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.WinForms.Playback;

namespace Playroma.Sound.WinForms.Controls;

/// <summary>
/// Structured editor for .playstream documents. All edits are committed through the
/// platform-neutral document model, so history, dirty state, autosave, validation, and background compilation remain authoritative.
/// </summary>
public sealed class StreamingSceneEditorControl : UserControl, IAuthoringEditorControl
{
    private readonly StreamingSceneDocument _document;
    private readonly ToolStrip _tools;
    private readonly ToolStripDropDownButton _addButton;
    private readonly ToolStripButton _duplicateButton;
    private readonly ToolStripButton _removeButton;
    private readonly ToolStripButton _upButton;
    private readonly ToolStripButton _downButton;
    private readonly ToolStripLabel _stateLabel;
    private readonly TreeView _tree;
    private readonly SceneTimelineControl _timeline;
    private readonly PropertyGrid _properties;
    private readonly StreamingScenePreviewControl _previewControl;
    private readonly DocumentEditorControl _rawEditor;
    private readonly TabControl _views;
    private readonly TabPage _timelinePage;
    private readonly Label _diagnosticLabel;
    private readonly System.Windows.Forms.Timer _timelineTimer;
    private SoundSceneDefinition _workingScene;
    private StreamingSceneObjectKey _selection = StreamingSceneObjectKey.Scene;
    private AddContext? _addContext;
    private bool _refreshing;
    private bool _committing;

    public StreamingSceneEditorControl(StreamingSceneDocument document, DocumentPreviewSession preview)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _workingScene = document.CreateEditableCopy();
        Dock = DockStyle.Fill;

        _tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _addButton = new ToolStripDropDownButton("Add") { ToolTipText = "Add a scene object" };
        AddMenu("Sound Reference", StreamingSceneObjectKind.Sound);
        AddMenu("Bus", StreamingSceneObjectKind.Bus);
        AddMenu("Bus Effect", StreamingSceneObjectKind.BusEffect);
        AddMenu("Bus Send", StreamingSceneObjectKind.BusSend);
        _addButton.DropDownItems.Add(new ToolStripSeparator());
        AddMenu("Layer", StreamingSceneObjectKind.Layer);
        AddMenu("Stochastic Emitter", StreamingSceneObjectKind.Emitter);
        AddMenu("Event Cluster", StreamingSceneObjectKind.EventCluster);
        AddMenu("Cluster Event", StreamingSceneObjectKind.ClusterEvent);
        _addButton.DropDownItems.Add(new ToolStripSeparator());
        AddMenu("State Machine", StreamingSceneObjectKind.StateMachine);
        AddMenu("State", StreamingSceneObjectKind.State);
        AddMenu("Transition", StreamingSceneObjectKind.Transition);
        AddMenu("Parameter", StreamingSceneObjectKind.Parameter);
        AddMenu("Parameter Binding", StreamingSceneObjectKind.ParameterBinding);
        _addButton.DropDownItems.Add(new ToolStripSeparator());
        AddMenu("Acoustic Zone", StreamingSceneObjectKind.AcousticZone);
        AddMenu("Acoustic Portal", StreamingSceneObjectKind.AcousticPortal);

        _duplicateButton = ToolButton("Duplicate", (_, _) => DuplicateSelected());
        _removeButton = ToolButton("Delete", (_, _) => RemoveSelected());
        _upButton = ToolButton("Move Up", (_, _) => MoveSelected(-1));
        _downButton = ToolButton("Move Down", (_, _) => MoveSelected(1));
        var validate = ToolButton("Validate", (_, _) => ValidateRequested?.Invoke(this, new DocumentActionEventArgs(_document)));
        var compile = ToolButton("Compile", (_, _) => CompileRequested?.Invoke(this, new DocumentActionEventArgs(_document)));
        var play = ToolButton("Play", (_, _) => PlayRequested?.Invoke(this, new DocumentActionEventArgs(_document)));
        _stateLabel = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right };
        _tools.Items.AddRange([_addButton, _duplicateButton, _removeButton, _upButton, _downButton, new ToolStripSeparator(), validate, compile, play, _stateLabel]);

        _timeline = new SceneTimelineControl { Dock = DockStyle.Fill };
        _properties = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = true, PropertySort = PropertySort.Categorized };

        _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
        _tree.AfterSelect += (_, eventArgs) =>
        {
            if (_refreshing) return;
            TreeNode? node = eventArgs.Node;
            if (node?.Tag is StreamingSceneObjectKey key)
            {
                _addContext = null;
                SetSelection(key, selectTree: false);
            }
            else if (node?.Tag is AddContext context)
            {
                _addContext = context;
                _properties.SelectedObject = null;
                _timeline.SetSelection(StreamingSceneObjectKey.Scene);
                UpdateToolbarState();
            }
        };
        _tree.NodeMouseDoubleClick += (_, eventArgs) =>
        {
            if (eventArgs.Node?.Tag is StreamingSceneObjectKey) _properties.Focus();
        };

        _timeline.SelectionRequested += (_, eventArgs) => SetSelection(eventArgs.Selection, selectTree: true);
        var zoom = new TrackBar { Minimum = 2, Maximum = 120, Value = 20, TickFrequency = 10, Dock = DockStyle.Right, Width = 220 };
        zoom.Scroll += (_, _) => _timeline.VisibleSeconds = zoom.Value;
        var timelineHeader = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 2, 6, 2) };
        timelineHeader.Controls.Add(zoom);
        timelineHeader.Controls.Add(new Label { Text = "Logical timeline", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) });

        _properties.PropertyValueChanged += OnPropertyValueChanged;
        _diagnosticLabel = new Label { Dock = DockStyle.Bottom, Height = 25, Padding = new Padding(6, 4, 6, 2), AutoEllipsis = true, ForeColor = SystemColors.GrayText };
        var propertiesHost = new Panel { Dock = DockStyle.Fill };
        propertiesHost.Controls.Add(_properties);
        propertiesHost.Controls.Add(_diagnosticLabel);

        var timelineSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(900, 620),
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 150,
            Panel2MinSize = 160,
            SplitterDistance = 320
        };
        timelineSplit.Panel1.Controls.Add(_timeline);
        timelineSplit.Panel1.Controls.Add(timelineHeader);
        timelineSplit.Panel2.Controls.Add(propertiesHost);
        _timelinePage = new TabPage("Timeline && Inspector");
        _timelinePage.Controls.Add(timelineSplit);

        _previewControl = new StreamingScenePreviewControl(_document, preview);
        _previewControl.PlayRequested += (_, eventArgs) => PlayRequested?.Invoke(this, eventArgs);
        var previewPage = new TabPage("Live Preview");
        previewPage.Controls.Add(_previewControl);

        _rawEditor = new DocumentEditorControl(_document);
        _rawEditor.ValidateRequested += (_, eventArgs) => ValidateRequested?.Invoke(this, eventArgs);
        _rawEditor.CompileRequested += (_, eventArgs) => CompileRequested?.Invoke(this, eventArgs);
        _rawEditor.PlayRequested += (_, eventArgs) => PlayRequested?.Invoke(this, eventArgs);
        _rawEditor.EditorStateChanged += (_, _) => EditorStateChanged?.Invoke(this, EventArgs.Empty);
        var rawPage = new TabPage("Raw JSON");
        rawPage.Controls.Add(_rawEditor);

        _views = new TabControl { Dock = DockStyle.Fill };
        _views.TabPages.AddRange([_timelinePage, previewPage, rawPage]);

        var main = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1100, 700),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 190,
            Panel2MinSize = 480,
            SplitterDistance = 275
        };
        main.Panel1.Controls.Add(_tree);
        main.Panel2.Controls.Add(_views);

        Controls.Add(main);
        Controls.Add(_tools);

        _document.Changed += OnDocumentChanged;
        _timelineTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _timelineTimer.Tick += (_, _) => _timeline.SetPlaybackFrame(_previewControl.CurrentSampleFrame);
        _timelineTimer.Start();
        RefreshStructured();
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
        TreeNode? match = FindNode(_tree.Nodes, node => node.Tag is StreamingSceneObjectKey key && key.Id == diagnostic.ObjectId);
        if (match is not null)
        {
            _views.SelectedTab = _timelinePage;
            _tree.SelectedNode = match;
            match.EnsureVisible();
            _diagnosticLabel.Text = $"{diagnostic.Code}: {diagnostic.Message}";
            _diagnosticLabel.ForeColor = diagnostic.Severity == AuthoringDiagnosticSeverity.Error ? Color.Firebrick : Color.DarkGoldenrod;
        }
        else
        {
            _rawEditor.SelectDiagnostic(diagnostic);
            _views.SelectedIndex = 2;
        }
    }

    public void FormatJson() => _rawEditor.FormatJson();

    private void AddMenu(string text, StreamingSceneObjectKind kind)
    {
        var item = new ToolStripMenuItem(text) { Tag = kind };
        item.Click += (_, _) => AddObject(kind);
        _addButton.DropDownItems.Add(item);
    }

    private void AddObject(StreamingSceneObjectKind kind)
    {
        string parent = _addContext is { } context && context.Kind == kind ? context.ParentId : ResolveParent(kind);
        StreamingSceneObjectKey created = StreamingSceneObjectKey.Scene;
        try
        {
            Commit($"Add {kind}", scene => created = StreamingSceneEditing.Add(scene, kind, parent));
            _selection = created;
            _addContext = null;
            RefreshStructured();
        }
        catch (KeyNotFoundException exception)
        {
            MessageBox.Show(this, exception.Message + Environment.NewLine + Environment.NewLine +
                "Create or select the required parent object first.", "Cannot add scene object", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void DuplicateSelected()
    {
        if (_selection.IsScene) return;
        StreamingSceneObjectKey duplicated = _selection;
        Commit($"Duplicate {_selection.Kind}", scene => duplicated = StreamingSceneEditing.Duplicate(scene, _selection));
        _selection = duplicated;
        RefreshStructured();
    }

    private void RemoveSelected()
    {
        if (_selection.IsScene) return;
        StreamingSceneObjectKey removing = _selection;
        if (removing.Kind == StreamingSceneObjectKind.Bus && removing.Id == "master")
        {
            MessageBox.Show(this, "The master bus is required and cannot be deleted.", "Delete bus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Commit($"Delete {removing.Kind}", scene => StreamingSceneEditing.Remove(scene, removing));
        _selection = StreamingSceneObjectKey.Scene;
        RefreshStructured();
    }

    private void MoveSelected(int offset)
    {
        if (_selection.IsScene) return;
        StreamingSceneObjectKey moving = _selection;
        Commit($"Move {moving.Kind}", scene => StreamingSceneEditing.Move(scene, moving, offset), mergeKey: "");
        if (moving.Index >= 0) _selection = moving with { Index = Math.Max(0, moving.Index + offset) };
        RefreshStructured();
    }

    private void Commit(string description, Action<SoundSceneDefinition> mutation, string mergeKey = "")
    {
        _committing = true;
        try { _document.Mutate(description, mutation, mergeKey); }
        finally { _committing = false; }
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyValueChanged(object? sender, PropertyValueChangedEventArgs eventArgs)
    {
        if (_refreshing) return;
        StreamingSceneObjectKey selection = _selection;
        string property = eventArgs.ChangedItem?.PropertyDescriptor?.Name ?? "Property";
        if ((property == nameof(SoundReferenceDefinition.SoundId) && selection.Kind == StreamingSceneObjectKind.Sound) || property == "Id")
        {
            // The PropertyGrid has already assigned the new identifier, so resolving through the
            // old key may no longer find the object. Use the selected object itself, then repair
            // every dependent reference before committing the detached scene snapshot.
            object? selected = _properties.SelectedObject;
            string newId = selected switch
            {
                SoundReferenceDefinition sound => sound.SoundId,
                SoundBusDefinition bus => bus.Id,
                SoundLayerDefinition layer => layer.Id,
                SoundEventEmitterDefinition emitter => emitter.Id,
                SoundEventClusterDefinition cluster => cluster.Id,
                SceneStateMachineDefinition machine => machine.Id,
                SceneStateDefinition state => state.Id,
                SceneParameterDefinition parameter => parameter.Id,
                AcousticZoneDefinition zone => zone.Id,
                AcousticPortalDefinition portal => portal.Id,
                _ => selection.Id
            };
            selection = StreamingSceneEditing.PropagateRename(_workingScene, selection, newId);
        }

        _selection = selection;
        _committing = true;
        try { _document.ReplaceModel(_workingScene, $"Edit {StreamingSceneEditing.DisplayName(_workingScene, selection)} {property}"); }
        finally { _committing = false; }
        RefreshStructured();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (_committing || IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke((Action)RefreshStructured);
        else RefreshStructured();
    }

    private void RefreshStructured()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            _workingScene = _document.CreateEditableCopy();
            if (StreamingSceneEditing.Resolve(_workingScene, _selection) is null) _selection = StreamingSceneObjectKey.Scene;
            BuildTree();
            object? selected = StreamingSceneEditing.Resolve(_workingScene, _selection);
            _properties.SelectedObject = selected;
            _timeline.SetScene(_workingScene, _selection);
            _previewControl.RefreshScene(_workingScene);
            _stateLabel.Text = $"{_workingScene.Layers.Count} layers · {_workingScene.Emitters.Count} emitters · {_workingScene.MaximumVoices} voices";
            UpdateToolbarState();
        }
        finally { _refreshing = false; }
    }

    private void BuildTree()
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            var sceneNode = Node(_workingScene.DisplayName, StreamingSceneObjectKey.Scene);
            sceneNode.ToolTipText = _workingScene.Id;
            _tree.Nodes.Add(sceneNode);
            AddSimpleGroup(sceneNode, "Sound references", StreamingSceneObjectKind.Sound, _workingScene.Sounds.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.Sound, item.SoundId)));

            TreeNode buses = Group(sceneNode, $"Buses ({_workingScene.Buses.Count})", StreamingSceneObjectKind.Bus);
            foreach (SoundBusDefinition bus in _workingScene.Buses)
            {
                TreeNode busNode = Node(bus.Id, new StreamingSceneObjectKey(StreamingSceneObjectKind.Bus, bus.Id));
                buses.Nodes.Add(busNode);
                TreeNode effects = Group(busNode, $"Effects ({bus.Effects.Count})", StreamingSceneObjectKind.BusEffect, bus.Id);
                for (int i = 0; i < bus.Effects.Count; i++) effects.Nodes.Add(Node($"{i + 1}. {bus.Effects[i].Kind}", new StreamingSceneObjectKey(StreamingSceneObjectKind.BusEffect, ParentId: bus.Id, Index: i)));
                TreeNode sends = Group(busNode, $"Sends ({bus.Sends.Count})", StreamingSceneObjectKind.BusSend, bus.Id);
                for (int i = 0; i < bus.Sends.Count; i++) sends.Nodes.Add(Node($"{i + 1}. → {bus.Sends[i].TargetBusId}", new StreamingSceneObjectKey(StreamingSceneObjectKind.BusSend, ParentId: bus.Id, Index: i)));
            }

            AddSimpleGroup(sceneNode, "Layers", StreamingSceneObjectKind.Layer, _workingScene.Layers.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.Layer, item.Id)));
            AddSimpleGroup(sceneNode, "Stochastic emitters", StreamingSceneObjectKind.Emitter, _workingScene.Emitters.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.Emitter, item.Id)));

            TreeNode clusters = Group(sceneNode, $"Event clusters ({_workingScene.EventClusters.Count})", StreamingSceneObjectKind.EventCluster);
            foreach (SoundEventClusterDefinition cluster in _workingScene.EventClusters)
            {
                TreeNode clusterNode = Node($"{cluster.Id} [{cluster.Trigger}]", new StreamingSceneObjectKey(StreamingSceneObjectKind.EventCluster, cluster.Id));
                clusters.Nodes.Add(clusterNode);
                TreeNode events = Group(clusterNode, $"Events ({cluster.Events.Count})", StreamingSceneObjectKind.ClusterEvent, cluster.Id);
                for (int i = 0; i < cluster.Events.Count; i++) events.Nodes.Add(Node($"{cluster.Events[i].OffsetSeconds:0.###}s — {cluster.Events[i].SoundId}", new StreamingSceneObjectKey(StreamingSceneObjectKind.ClusterEvent, ParentId: cluster.Id, Index: i)));
            }

            TreeNode machines = Group(sceneNode, $"State machines ({_workingScene.StateMachines.Count})", StreamingSceneObjectKind.StateMachine);
            foreach (SceneStateMachineDefinition machine in _workingScene.StateMachines)
            {
                TreeNode machineNode = Node(machine.Id, new StreamingSceneObjectKey(StreamingSceneObjectKind.StateMachine, machine.Id));
                machines.Nodes.Add(machineNode);
                TreeNode states = Group(machineNode, $"States ({machine.States.Count})", StreamingSceneObjectKind.State, machine.Id);
                foreach (SceneStateDefinition state in machine.States) states.Nodes.Add(Node(state.Id, new StreamingSceneObjectKey(StreamingSceneObjectKind.State, state.Id, machine.Id)));
                TreeNode transitions = Group(machineNode, $"Transitions ({machine.Transitions.Count})", StreamingSceneObjectKind.Transition, machine.Id);
                for (int i = 0; i < machine.Transitions.Count; i++)
                {
                    SceneTransitionDefinition transition = machine.Transitions[i];
                    transitions.Nodes.Add(Node($"{transition.FromState} → {transition.ToState} [{transition.Trigger}]", new StreamingSceneObjectKey(StreamingSceneObjectKind.Transition, ParentId: machine.Id, Index: i)));
                }
            }

            AddSimpleGroup(sceneNode, "Parameters", StreamingSceneObjectKind.Parameter, _workingScene.Parameters.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.Parameter, item.Id)));
            TreeNode bindings = Group(sceneNode, $"Parameter bindings ({_workingScene.ParameterBindings.Count})", StreamingSceneObjectKind.ParameterBinding);
            for (int i = 0; i < _workingScene.ParameterBindings.Count; i++)
            {
                SceneParameterBindingDefinition binding = _workingScene.ParameterBindings[i];
                bindings.Nodes.Add(Node($"{binding.ParameterId} → {binding.TargetId}", new StreamingSceneObjectKey(StreamingSceneObjectKind.ParameterBinding, Index: i)));
            }
            AddSimpleGroup(sceneNode, "Acoustic zones", StreamingSceneObjectKind.AcousticZone, _workingScene.AcousticZones.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.AcousticZone, item.Id)));
            AddSimpleGroup(sceneNode, "Acoustic portals", StreamingSceneObjectKind.AcousticPortal, _workingScene.AcousticPortals.Select(item => new StreamingSceneObjectKey(StreamingSceneObjectKind.AcousticPortal, item.Id)));
            sceneNode.Expand();

            TreeNode? selected = FindNode(_tree.Nodes, node => node.Tag is StreamingSceneObjectKey key && key.Equals(_selection));
            if (selected is not null)
            {
                _tree.SelectedNode = selected;
                selected.EnsureVisible();
            }
        }
        finally { _tree.EndUpdate(); }
    }

    private void AddSimpleGroup(TreeNode parent, string title, StreamingSceneObjectKind kind, IEnumerable<StreamingSceneObjectKey> keys)
    {
        StreamingSceneObjectKey[] materialized = keys.ToArray();
        TreeNode group = Group(parent, $"{title} ({materialized.Length})", kind);
        foreach (StreamingSceneObjectKey key in materialized) group.Nodes.Add(Node(StreamingSceneEditing.DisplayName(_workingScene, key), key));
    }

    private void SetSelection(StreamingSceneObjectKey key, bool selectTree)
    {
        if (StreamingSceneEditing.Resolve(_workingScene, key) is null) return;
        _addContext = null;
        _selection = key;
        _properties.SelectedObject = StreamingSceneEditing.Resolve(_workingScene, key);
        _timeline.SetSelection(key);
        if (selectTree)
        {
            TreeNode? node = FindNode(_tree.Nodes, item => item.Tag is StreamingSceneObjectKey candidate && candidate.Equals(key));
            if (node is not null) _tree.SelectedNode = node;
        }
        UpdateToolbarState();
    }

    private string ResolveParent(StreamingSceneObjectKind kind)
    {
        return kind switch
        {
            StreamingSceneObjectKind.BusEffect or StreamingSceneObjectKind.BusSend =>
                _selection.Kind == StreamingSceneObjectKind.Bus ? _selection.Id :
                _selection.Kind is StreamingSceneObjectKind.BusEffect or StreamingSceneObjectKind.BusSend ? _selection.ParentId :
                _workingScene.Buses.FirstOrDefault()?.Id ?? "master",
            StreamingSceneObjectKind.ClusterEvent =>
                _selection.Kind == StreamingSceneObjectKind.EventCluster ? _selection.Id :
                _selection.Kind == StreamingSceneObjectKind.ClusterEvent ? _selection.ParentId :
                _workingScene.EventClusters.FirstOrDefault()?.Id ?? "",
            StreamingSceneObjectKind.State or StreamingSceneObjectKind.Transition =>
                _selection.Kind == StreamingSceneObjectKind.StateMachine ? _selection.Id :
                _selection.Kind is StreamingSceneObjectKind.State or StreamingSceneObjectKind.Transition ? _selection.ParentId :
                _workingScene.StateMachines.FirstOrDefault()?.Id ?? "",
            _ => ""
        };
    }

    private void UpdateToolbarState()
    {
        bool selected = _addContext is null && !_selection.IsScene;
        _duplicateButton.Enabled = selected;
        _removeButton.Enabled = selected && !(_selection.Kind == StreamingSceneObjectKind.Bus && _selection.Id == "master");
        _upButton.Enabled = selected;
        _downButton.Enabled = selected;
    }

    private static TreeNode Node(string text, StreamingSceneObjectKey key) => new(text) { Tag = key };

    private static TreeNode Group(TreeNode parent, string text, StreamingSceneObjectKind kind, string parentId = "")
    {
        var node = new TreeNode(text) { Tag = new AddContext(kind, parentId), ForeColor = SystemColors.GrayText };
        parent.Nodes.Add(node);
        return node;
    }

    private static TreeNode? FindNode(TreeNodeCollection nodes, Predicate<TreeNode> predicate)
    {
        foreach (TreeNode node in nodes)
        {
            if (predicate(node)) return node;
            TreeNode? nested = FindNode(node.Nodes, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static ToolStripButton ToolButton(string text, EventHandler click)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += click;
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timelineTimer.Stop();
            _timelineTimer.Dispose();
            _document.Changed -= OnDocumentChanged;
            _timelinePage.Controls.Clear();
            _rawEditor.Dispose();
            _previewControl.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record AddContext(StreamingSceneObjectKind Kind, string ParentId);
}
