using System.Numerics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Runtime;
using Playroma.Sound.Streaming.Snapshots;
using Playroma.Sound.Streaming.Spatial;
using Playroma.Sound.Streaming.Telemetry;
using Playroma.Sound.WinForms.Playback;

namespace Playroma.Sound.WinForms.Controls;

/// <summary>Control-thread surface for the exact production StreamingSceneInstance used by WASAPI preview.</summary>
public sealed class StreamingScenePreviewControl : UserControl
{
    private readonly StreamingSceneDocument _document;
    private readonly DocumentPreviewSession _preview;
    private readonly Label _status;
    private readonly Label _telemetry;
    private readonly FlowLayoutPanel _parameterPanel;
    private readonly FlowLayoutPanel _triggerPanel;
    private readonly FlowLayoutPanel _statePanel;
    private readonly DataGridView _mixGrid;
    private readonly ComboBox _zone;
    private readonly NumericUpDown _listenerX;
    private readonly NumericUpDown _listenerY;
    private readonly NumericUpDown _listenerZ;
    private readonly ComboBox _spatialLayer;
    private readonly NumericUpDown _spatialDistance;
    private readonly NumericUpDown _spatialOcclusion;
    private readonly NumericUpDown _spatialObstruction;
    private readonly NumericUpDown _seed;
    private readonly Button _restoreSnapshot;
    private readonly System.Windows.Forms.Timer _timer;
    private SoundSceneDefinition _scene;
    private SoundSceneSnapshot? _snapshot;
    private long _snapshotRequestId = -1;
    private bool _synchronizing;

    public StreamingScenePreviewControl(StreamingSceneDocument document, DocumentPreviewSession preview)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _scene = document.CreateEditableCopy();
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        var play = Button("Compile && Play", (_, _) => PlayRequested?.Invoke(this, new DocumentActionEventArgs(_document)));
        var stop = Button("Stop", (_, _) => { if (_preview.IsPreviewing(_document)) _preview.Stop(); });
        var reset = Button("Reset", (_, _) => ResetPreview());
        var capture = Button("Capture Snapshot", (_, _) => CaptureSnapshot());
        _restoreSnapshot = Button("Restore Snapshot", (_, _) => RestoreSnapshot());
        _restoreSnapshot.Enabled = false;
        header.Controls.AddRange([play, stop, reset, capture, _restoreSnapshot]);
        header.Controls.Add(new Label { Text = "Root seed", AutoSize = true, Margin = new Padding(18, 8, 3, 0) });
        _seed = new NumericUpDown { Minimum = 0, Maximum = decimal.MaxValue, Width = 130, ThousandsSeparator = true };
        header.Controls.Add(_seed);
        header.Controls.Add(Button("Apply Seed && Restart", (_, _) => ApplySeedAndRestart()));

        _status = new Label { Dock = DockStyle.Top, Height = 25, Padding = new Padding(8, 4, 8, 2), AutoEllipsis = true };
        _telemetry = new Label { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 2, 8, 4), AutoEllipsis = true };

        (GroupBox parametersGroup, FlowLayoutPanel parameterPanel) = Group("Live parameters");
        (GroupBox triggersGroup, FlowLayoutPanel triggerPanel) = Group("Triggers");
        (GroupBox statesGroup, FlowLayoutPanel statePanel) = Group("State machines");
        _parameterPanel = parameterPanel;
        _triggerPanel = triggerPanel;
        _statePanel = statePanel;

        _mixGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };
        _mixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Kind", Width = 70, ReadOnly = true });
        _mixGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Layer / emitter", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _mixGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Mute", HeaderText = "Mute", Width = 48 });
        _mixGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Solo", HeaderText = "Solo", Width = 48 });
        _mixGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_mixGrid.IsCurrentCellDirty) _mixGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _mixGrid.CellValueChanged += (_, eventArgs) => { if (!_synchronizing && eventArgs.RowIndex >= 0) ApplyPreviewMix(); };
        var mixGroup = new GroupBox { Text = "Preview mute and solo", Dock = DockStyle.Fill, Padding = new Padding(6) };
        mixGroup.Controls.Add(_mixGrid);
        mixGroup.Controls.Add(new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Controls = { Button("Clear Mute/Solo", (_, _) => ClearPreviewMix()) }
        });

        var listenerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        _zone = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        _listenerX = Coordinate();
        _listenerY = Coordinate();
        _listenerZ = Coordinate();
        listenerPanel.Controls.AddRange([
            new Label { Text = "Zone", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, _zone,
            new Label { Text = "X", AutoSize = true, Margin = new Padding(12, 8, 3, 0) }, _listenerX,
            new Label { Text = "Y", AutoSize = true, Margin = new Padding(6, 8, 3, 0) }, _listenerY,
            new Label { Text = "Z", AutoSize = true, Margin = new Padding(6, 8, 3, 0) }, _listenerZ,
            Button("Apply Listener", (_, _) => ApplyListener())
        ]);
        var listenerGroup = new GroupBox { Text = "Listener simulation", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        listenerGroup.Controls.Add(listenerPanel);

        var spatialPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        _spatialLayer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        _spatialDistance = Coordinate();
        _spatialDistance.Minimum = 0;
        _spatialDistance.Maximum = 10_000;
        _spatialOcclusion = UnitInterval();
        _spatialObstruction = UnitInterval();
        spatialPanel.Controls.AddRange([
            new Label { Text = "Layer", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, _spatialLayer,
            new Label { Text = "Distance", AutoSize = true, Margin = new Padding(12, 8, 3, 0) }, _spatialDistance,
            new Label { Text = "Occlusion", AutoSize = true, Margin = new Padding(6, 8, 3, 0) }, _spatialOcclusion,
            new Label { Text = "Obstruction", AutoSize = true, Margin = new Padding(6, 8, 3, 0) }, _spatialObstruction,
            Button("Apply Spatial Override", (_, _) => ApplySpatialOverride()),
            Button("Restore Authored Spatial", (_, _) => RestoreAuthoredSpatial())
        ]);
        var spatialGroup = new GroupBox { Text = "Layer distance and occlusion simulation", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        spatialGroup.Controls.Add(spatialPanel);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(parametersGroup, 0, 0);
        content.Controls.Add(triggersGroup, 1, 0);
        content.Controls.Add(statesGroup, 0, 1);
        content.SetColumnSpan(statesGroup, 2);
        content.Controls.Add(mixGroup, 0, 2);
        content.SetColumnSpan(mixGroup, 2);

        Controls.Add(content);
        Controls.Add(spatialGroup);
        Controls.Add(listenerGroup);
        Controls.Add(_telemetry);
        Controls.Add(_status);
        Controls.Add(header);

        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => UpdateRuntimeState();
        _timer.Start();
        _preview.StateChanged += OnPreviewStateChanged;
        RefreshScene(_scene);
    }

    public event EventHandler<DocumentActionEventArgs>? PlayRequested;
    private StreamingSceneController? ActiveController => _preview.IsPreviewing(_document) ? _preview.SceneController : null;
    public long CurrentSampleFrame => ActiveController?.CurrentSampleFrame ?? 0;

    public void RefreshScene(SoundSceneDefinition scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _synchronizing = true;
        try
        {
            _seed.Value = Math.Min((decimal)_scene.RootSeed, decimal.MaxValue);
            BuildParameters();
            BuildTriggers();
            BuildStates();
            BuildMixGrid();
            _zone.Items.Clear();
            _zone.Items.Add("(none)");
            foreach (AcousticZoneDefinition zone in _scene.AcousticZones) _zone.Items.Add(zone.Id);
            _zone.SelectedIndex = 0;
            _spatialLayer.Items.Clear();
            foreach (SoundLayerDefinition layer in _scene.Layers) _spatialLayer.Items.Add(layer.Id);
            if (_spatialLayer.Items.Count > 0) _spatialLayer.SelectedIndex = 0;
        }
        finally { _synchronizing = false; }
        UpdateRuntimeState();
    }

    private void BuildParameters()
    {
        _parameterPanel.Controls.Clear();
        foreach (SceneParameterDefinition parameter in _scene.Parameters)
        {
            var row = new TableLayoutPanel { AutoSize = true, Width = 360, ColumnCount = 3, Margin = new Padding(3) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            var label = new Label { Text = string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var slider = new TrackBar { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Dock = DockStyle.Fill, Tag = parameter };
            var value = new Label { Width = 58, TextAlign = ContentAlignment.MiddleRight, Tag = parameter.Id };
            slider.Value = ToSlider(parameter, parameter.DefaultValue);
            value.Text = FromSlider(parameter, slider.Value).ToString("0.###");
            slider.Scroll += (_, _) =>
            {
                float current = FromSlider(parameter, slider.Value);
                value.Text = current.ToString("0.###");
                ActiveController?.TrySetParameter(parameter.Id, current);
            };
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(slider, 1, 0);
            row.Controls.Add(value, 2, 0);
            _parameterPanel.Controls.Add(row);
        }
        if (_scene.Parameters.Count == 0) _parameterPanel.Controls.Add(EmptyLabel("No scene parameters."));
    }

    private void BuildTriggers()
    {
        _triggerPanel.Controls.Clear();
        string[] triggers = _scene.EventClusters.Select(item => item.Trigger)
            .Concat(_scene.StateMachines.SelectMany(item => item.Transitions).Select(item => item.Trigger))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (string trigger in triggers) _triggerPanel.Controls.Add(Button(trigger, (_, _) => ActiveController?.TryTrigger(trigger)));
        if (triggers.Length == 0) _triggerPanel.Controls.Add(EmptyLabel("No triggers."));
    }

    private void BuildStates()
    {
        _statePanel.Controls.Clear();
        foreach (SceneStateMachineDefinition machine in _scene.StateMachines)
        {
            var label = new Label { Text = machine.Id, Width = 150, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 8, 3, 0) };
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Tag = machine.Id };
            combo.Items.AddRange(machine.States.Select(item => item.Id).Cast<object>().ToArray());
            if (combo.Items.Count > 0) combo.SelectedItem = machine.InitialState;
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (!_synchronizing && combo.SelectedItem is string state) ActiveController?.TrySetState(machine.Id, state);
            };
            _statePanel.Controls.Add(label);
            _statePanel.Controls.Add(combo);
        }
        if (_scene.StateMachines.Count == 0) _statePanel.Controls.Add(EmptyLabel("No state machines."));
    }

    private void BuildMixGrid()
    {
        _mixGrid.Rows.Clear();
        foreach (SoundLayerDefinition layer in _scene.Layers) _mixGrid.Rows.Add("Layer", layer.Id, false, false);
        foreach (SoundEventEmitterDefinition emitter in _scene.Emitters) _mixGrid.Rows.Add("Emitter", emitter.Id, false, false);
    }

    private void ApplyPreviewMix()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null) return;
        bool anySolo = _mixGrid.Rows.Cast<DataGridViewRow>().Any(row => Convert.ToBoolean(row.Cells["Solo"].Value));
        foreach (DataGridViewRow row in _mixGrid.Rows)
        {
            bool mute = Convert.ToBoolean(row.Cells["Mute"].Value);
            bool solo = Convert.ToBoolean(row.Cells["Solo"].Value);
            float gain = mute || (anySolo && !solo) ? 0f : 1f;
            string id = Convert.ToString(row.Cells["Id"].Value) ?? "";
            string kind = Convert.ToString(row.Cells["Kind"].Value) ?? "";
            if (kind == "Layer") controller.TrySetLayerPreviewGain(id, gain);
            else controller.TrySetEmitterPreviewGain(id, gain);
        }
    }

    private void ClearPreviewMix()
    {
        _synchronizing = true;
        try
        {
            foreach (DataGridViewRow row in _mixGrid.Rows)
            {
                row.Cells["Mute"].Value = false;
                row.Cells["Solo"].Value = false;
            }
        }
        finally { _synchronizing = false; }
        ApplyPreviewMix();
    }

    private void ResetPreview()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null) return;
        controller.TryRequestReset();
        controller.TryStart();
        ApplyParameterValues(controller);
        ApplyPreviewMix();
        ApplyListener();
        ApplySelectedStates(controller);
        _snapshot = null;
        _snapshotRequestId = -1;
        _restoreSnapshot.Enabled = false;
    }


    private void ApplyParameterValues(StreamingSceneController controller)
    {
        foreach (Control rowControl in _parameterPanel.Controls)
        {
            if (rowControl is not TableLayoutPanel row) continue;
            TrackBar? slider = row.Controls.OfType<TrackBar>().FirstOrDefault();
            if (slider?.Tag is SceneParameterDefinition parameter)
                controller.TrySetParameter(parameter.Id, FromSlider(parameter, slider.Value));
        }
    }

    private void ApplySelectedStates(StreamingSceneController controller)
    {
        foreach (Control control in _statePanel.Controls)
            if (control is ComboBox combo && combo.Tag is string machineId && combo.SelectedItem is string stateId)
                controller.TrySetState(machineId, stateId);
    }

    private void CaptureSnapshot()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null) return;
        if (controller.TryRequestSnapshot(out long requestId))
        {
            _snapshotRequestId = requestId;
            _status.Text = $"Snapshot {requestId} requested…";
        }
    }

    private void RestoreSnapshot()
    {
        if (_snapshot is null) return;
        if (ActiveController?.TryRestoreSnapshot(_snapshot) == true) _status.Text = "Snapshot restore queued.";
    }

    private void ApplyListener()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null) return;
        var listener = SoundListenerState.Default with
        {
            Position = new Vector3((float)_listenerX.Value, (float)_listenerY.Value, (float)_listenerZ.Value)
        };
        controller.TrySetListener(listener);
        string zone = _zone.SelectedIndex <= 0 ? "" : Convert.ToString(_zone.SelectedItem) ?? "";
        controller.TrySetListenerZone(zone);
    }


    private void ApplySpatialOverride()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null || _spatialLayer.SelectedItem is not string layerId) return;
        float distance = (float)_spatialDistance.Value;
        controller.TrySetLayerSpatialState(layerId, new SoundEmitterState(
            true,
            new Vector3(distance, 0f, 0f),
            Vector3.Zero,
            1f,
            Math.Max(2f, distance + 1f),
            (float)_spatialOcclusion.Value,
            (float)_spatialObstruction.Value));
    }

    private void RestoreAuthoredSpatial()
    {
        StreamingSceneController? controller = ActiveController;
        if (controller is null || _spatialLayer.SelectedItem is not string layerId) return;
        SoundLayerDefinition? layer = _scene.Layers.FirstOrDefault(item => item.Id == layerId);
        if (layer is null) return;
        controller.TrySetLayerSpatialState(layerId, new SoundEmitterState(
            layer.Spatial.Enabled,
            layer.Spatial.Position,
            layer.Spatial.Velocity,
            layer.Spatial.MinimumDistance,
            layer.Spatial.MaximumDistance,
            layer.Spatial.Occlusion,
            layer.Spatial.Obstruction));
    }

    private void ApplySeedAndRestart()
    {
        ulong seed = decimal.ToUInt64(_seed.Value);
        _document.Mutate("Change scene seed", scene => scene.RootSeed = seed, "scene-root-seed");
        PlayRequested?.Invoke(this, new DocumentActionEventArgs(_document));
    }

    private void UpdateRuntimeState()
    {
        StreamingSceneController? controller = ActiveController;
        bool active = controller is not null && _preview.IsPreviewing(_document);
        Enabled = true;
        _status.Text = active ? "Production streaming-scene preview is active." : "Compile and play this scene to enable live controls.";
        if (!active)
        {
            _telemetry.Text = "No streaming telemetry.";
            return;
        }

        if (_snapshotRequestId >= 0 && controller!.TryGetSnapshot(_snapshotRequestId, out SoundSceneSnapshot? captured) && captured is not null)
        {
            _snapshot = captured;
            _snapshotRequestId = -1;
            _restoreSnapshot.Enabled = true;
            _status.Text = $"Snapshot captured at frame {captured.SampleFrame:N0}.";
        }

        StreamingTelemetrySnapshot telemetry = controller!.Telemetry;
        _telemetry.Text = $"Frame {telemetry.SampleFrame:N0} | voices {telemetry.ActiveVoices}/{telemetry.MaximumVoices} | virtual {telemetry.VirtualVoices} | " +
            $"stolen {telemetry.StolenVoices} | rejected {telemetry.RejectedVoices} | render {telemetry.LastRenderMilliseconds:0.000} ms ({telemetry.LastRenderLoad:P0})";

        _synchronizing = true;
        try
        {
            foreach (Control control in _statePanel.Controls)
            {
                if (control is not ComboBox combo || combo.Tag is not string machineId) continue;
                string? current = telemetry.StateMachines.FirstOrDefault(item => item.Id == machineId)?.CurrentState;
                if (current is not null && !Equals(combo.SelectedItem, current)) combo.SelectedItem = current;
            }
        }
        finally { _synchronizing = false; }
    }

    private void OnPreviewStateChanged(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke((Action)UpdateRuntimeState);
        else UpdateRuntimeState();
    }

    private static (GroupBox Group, FlowLayoutPanel Panel) Group(string title)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(4) };
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        group.Controls.Add(panel);
        return (group, panel);
    }

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        button.Click += click;
        return button;
    }

    private static Label EmptyLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(3) };

    private static NumericUpDown Coordinate() => new()
    {
        DecimalPlaces = 2,
        Increment = 0.25m,
        Minimum = -10_000,
        Maximum = 10_000,
        Width = 82
    };

    private static NumericUpDown UnitInterval() => new()
    {
        DecimalPlaces = 2,
        Increment = 0.05m,
        Minimum = 0,
        Maximum = 1,
        Width = 70
    };

    private static int ToSlider(SceneParameterDefinition parameter, float value)
    {
        float range = parameter.Maximum - parameter.Minimum;
        if (!float.IsFinite(range) || range <= 0f) return 0;
        return Math.Clamp((int)Math.Round(((value - parameter.Minimum) / range) * 1000), 0, 1000);
    }

    private static float FromSlider(SceneParameterDefinition parameter, int value) =>
        parameter.Minimum + ((parameter.Maximum - parameter.Minimum) * (value / 1000f));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _preview.StateChanged -= OnPreviewStateChanged;
        }
        base.Dispose(disposing);
    }
}
