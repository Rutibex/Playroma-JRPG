using Playroma.Sound.Authoring.Scenes;
using Playroma.Sound.Streaming.Definitions;

namespace Playroma.Sound.WinForms.Controls;

public sealed class SceneSelectionEventArgs : EventArgs
{
    public SceneSelectionEventArgs(StreamingSceneObjectKey selection) => Selection = selection;
    public StreamingSceneObjectKey Selection { get; }
}

/// <summary>
/// Logical scene timeline. It visualizes persistent layers, stochastic event cadence,
/// cluster offsets, and state-machine lanes rather than pretending an infinite scene is a fixed waveform.
/// </summary>
public sealed class SceneTimelineControl : ScrollableControl
{
    private const int LabelWidth = 190;
    private const int HeaderHeight = 34;
    private const int RowHeight = 30;
    private readonly List<TimelineRow> _rows = [];
    private SoundSceneDefinition? _scene;
    private StreamingSceneObjectKey _selection = StreamingSceneObjectKey.Scene;
    private double _visibleSeconds = 20;
    private long _currentFrame;
    private int _sampleRate = 48_000;

    public SceneTimelineControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public event EventHandler<SceneSelectionEventArgs>? SelectionRequested;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double VisibleSeconds
    {
        get => _visibleSeconds;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _visibleSeconds = Math.Clamp(value, 2, 300);
            Invalidate();
        }
    }

    public void SetScene(SoundSceneDefinition scene, StreamingSceneObjectKey selection)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _selection = selection;
        RebuildRows();
        Invalidate();
    }

    public void SetSelection(StreamingSceneObjectKey selection)
    {
        _selection = selection;
        Invalidate();
    }

    public void SetPlaybackFrame(long sampleFrame, int sampleRate = 48_000)
    {
        _currentFrame = Math.Max(0, sampleFrame);
        _sampleRate = Math.Max(1, sampleRate);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        Graphics graphics = eventArgs.Graphics;
        Point scroll = AutoScrollPosition;
        graphics.TranslateTransform(scroll.X, scroll.Y);

        int width = Math.Max(ClientSize.Width - SystemInformation.VerticalScrollBarWidth, LabelWidth + 360);
        int timelineWidth = Math.Max(1, width - LabelWidth);
        DrawHeader(graphics, width, timelineWidth);

        for (int index = 0; index < _rows.Count; index++)
        {
            int y = HeaderHeight + (index * RowHeight);
            DrawRow(graphics, _rows[index], index, y, width, timelineWidth);
        }

        double currentSeconds = _currentFrame / (double)_sampleRate;
        double wrappedSeconds = _visibleSeconds <= 0 ? 0 : currentSeconds % _visibleSeconds;
        int playheadX = LabelWidth + (int)Math.Round((wrappedSeconds / _visibleSeconds) * timelineWidth);
        using var playheadPen = new Pen(SystemColors.HotTrack, 2f);
        graphics.DrawLine(playheadPen, playheadX, 0, playheadX, HeaderHeight + (_rows.Count * RowHeight));
        TextRenderer.DrawText(graphics, $"{currentSeconds:0.00}s", Font, new Point(playheadX + 3, 2), SystemColors.HotTrack);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        int logicalY = eventArgs.Y - AutoScrollPosition.Y;
        int index = (logicalY - HeaderHeight) / RowHeight;
        if (logicalY >= HeaderHeight && index >= 0 && index < _rows.Count)
            SelectionRequested?.Invoke(this, new SceneSelectionEventArgs(_rows[index].Key));
    }

    private void RebuildRows()
    {
        _rows.Clear();
        if (_scene is null) return;
        _rows.AddRange(_scene.Layers.Select(item => new TimelineRow(new StreamingSceneObjectKey(StreamingSceneObjectKind.Layer, item.Id), "Layer", item.Id, item)));
        _rows.AddRange(_scene.Emitters.Select(item => new TimelineRow(new StreamingSceneObjectKey(StreamingSceneObjectKind.Emitter, item.Id), "Emitter", item.Id, item)));
        _rows.AddRange(_scene.EventClusters.Select(item => new TimelineRow(new StreamingSceneObjectKey(StreamingSceneObjectKind.EventCluster, item.Id), "Cluster", item.Id, item)));
        _rows.AddRange(_scene.StateMachines.Select(item => new TimelineRow(new StreamingSceneObjectKey(StreamingSceneObjectKind.StateMachine, item.Id), "State", item.Id, item)));
        AutoScrollMinSize = new Size(LabelWidth + 480, HeaderHeight + (_rows.Count * RowHeight) + 8);
    }

    private void DrawHeader(Graphics graphics, int width, int timelineWidth)
    {
        graphics.FillRectangle(SystemBrushes.Control, 0, 0, width, HeaderHeight);
        graphics.DrawLine(SystemPens.ControlDark, 0, HeaderHeight - 1, width, HeaderHeight - 1);
        TextRenderer.DrawText(graphics, "Scene object", Font, new Rectangle(8, 8, LabelWidth - 16, 20), SystemColors.ControlText, TextFormatFlags.EndEllipsis);
        int divisions = Math.Max(2, Math.Min(20, (int)Math.Ceiling(_visibleSeconds)));
        for (int division = 0; division <= divisions; division++)
        {
            float ratio = division / (float)divisions;
            int x = LabelWidth + (int)Math.Round(ratio * timelineWidth);
            graphics.DrawLine(SystemPens.ControlDark, x, HeaderHeight - 8, x, HeaderHeight);
            double seconds = ratio * _visibleSeconds;
            TextRenderer.DrawText(graphics, $"{seconds:0.#}s", Font, new Point(x + 2, 8), SystemColors.ControlText);
        }
    }

    private void DrawRow(Graphics graphics, TimelineRow row, int index, int y, int width, int timelineWidth)
    {
        bool selected = row.Key.Equals(_selection);
        Brush background = selected ? SystemBrushes.Highlight : index % 2 == 0 ? SystemBrushes.Window : SystemBrushes.ControlLightLight;
        Color textColor = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        graphics.FillRectangle(background, 0, y, width, RowHeight);
        graphics.DrawLine(SystemPens.ControlLight, 0, y + RowHeight - 1, width, y + RowHeight - 1);
        TextRenderer.DrawText(graphics, row.Category, Font, new Rectangle(6, y + 3, 55, RowHeight - 6), textColor, TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(graphics, row.Label, Font, new Rectangle(62, y + 3, LabelWidth - 68, RowHeight - 6), textColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

        Rectangle lane = new(LabelWidth + 2, y + 5, timelineWidth - 4, RowHeight - 10);
        switch (row.Value)
        {
            case SoundLayerDefinition layer:
                DrawLayer(graphics, layer, lane, selected);
                break;
            case SoundEventEmitterDefinition emitter:
                DrawEmitter(graphics, emitter, lane, selected);
                break;
            case SoundEventClusterDefinition cluster:
                DrawCluster(graphics, cluster, lane, selected);
                break;
            case SceneStateMachineDefinition machine:
                DrawStateMachine(graphics, machine, lane, selected);
                break;
        }
    }

    private void DrawLayer(Graphics graphics, SoundLayerDefinition layer, Rectangle lane, bool selected)
    {
        double start = Math.Clamp(layer.StartDelaySeconds, 0, _visibleSeconds);
        int x = lane.Left + (int)Math.Round((start / _visibleSeconds) * lane.Width);
        Rectangle bar = new(x, lane.Top, Math.Max(2, lane.Right - x), lane.Height);
        using var brush = new SolidBrush(selected ? SystemColors.HighlightText : SystemColors.ActiveCaption);
        graphics.FillRectangle(brush, bar);
        TextRenderer.DrawText(graphics, $"{layer.Kind} · {layer.SoundId}", Font, bar, selected ? SystemColors.Highlight : SystemColors.ActiveCaptionText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawEmitter(Graphics graphics, SoundEventEmitterDefinition emitter, Rectangle lane, bool selected)
    {
        double average = Math.Max(0.05, (emitter.MinimumIntervalSeconds + emitter.MaximumIntervalSeconds) * 0.5);
        using var pen = new Pen(selected ? SystemColors.HighlightText : SystemColors.HotTrack, 2f);
        for (double time = average; time <= _visibleSeconds; time += average)
        {
            int x = lane.Left + (int)Math.Round((time / _visibleSeconds) * lane.Width);
            graphics.DrawEllipse(pen, x - 3, lane.Top + (lane.Height / 2) - 3, 6, 6);
        }
        TextRenderer.DrawText(graphics, $"{emitter.MinimumIntervalSeconds:0.##}–{emitter.MaximumIntervalSeconds:0.##}s · p={emitter.Probability:0.##}", Font, lane, selected ? SystemColors.HighlightText : SystemColors.WindowText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void DrawCluster(Graphics graphics, SoundEventClusterDefinition cluster, Rectangle lane, bool selected)
    {
        using var pen = new Pen(selected ? SystemColors.HighlightText : SystemColors.ControlText, 2f);
        foreach (SoundClusterEventDefinition item in cluster.Events)
        {
            double time = Math.Clamp(item.OffsetSeconds, 0, _visibleSeconds);
            int x = lane.Left + (int)Math.Round((time / _visibleSeconds) * lane.Width);
            graphics.DrawLine(pen, x, lane.Top, x, lane.Bottom);
        }
        TextRenderer.DrawText(graphics, $"Trigger: {cluster.Trigger} · {cluster.Events.Count} event(s)", Font, lane, selected ? SystemColors.HighlightText : SystemColors.WindowText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void DrawStateMachine(Graphics graphics, SceneStateMachineDefinition machine, Rectangle lane, bool selected)
    {
        int stateCount = Math.Max(1, machine.States.Count);
        int segmentWidth = Math.Max(1, lane.Width / stateCount);
        for (int index = 0; index < stateCount; index++)
        {
            Rectangle segment = new(lane.Left + (index * segmentWidth), lane.Top, index == stateCount - 1 ? lane.Right - (lane.Left + (index * segmentWidth)) : segmentWidth - 2, lane.Height);
            using var brush = new SolidBrush(selected ? SystemColors.HighlightText : index % 2 == 0 ? SystemColors.InactiveCaption : SystemColors.ControlLight);
            graphics.FillRectangle(brush, segment);
            string label = machine.States.ElementAtOrDefault(index)?.Id ?? "No states";
            TextRenderer.DrawText(graphics, label, Font, segment, selected ? SystemColors.Highlight : SystemColors.ControlText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed record TimelineRow(StreamingSceneObjectKey Key, string Category, string Label, object Value);
}
