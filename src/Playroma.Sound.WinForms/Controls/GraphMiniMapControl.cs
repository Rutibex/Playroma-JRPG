using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.WinForms.Controls;

public sealed class GraphMiniMapControl : Control
{
    private GraphCanvasControl? _canvas;
    public GraphMiniMapControl() { DoubleBuffered = true; BackColor = Color.FromArgb(25, 28, 32); }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public GraphCanvasControl? Canvas
    {
        get => _canvas;
        set
        {
            if (_canvas is not null)
            {
                _canvas.ViewChanged -= OnCanvasChanged;
                _canvas.GraphChanged -= OnCanvasChanged;
                _canvas.SelectionChanged -= OnCanvasChanged;
                _canvas.NodePositionsCommitted -= OnCanvasPositionsChanged;
            }
            _canvas = value;
            if (_canvas is not null)
            {
                _canvas.ViewChanged += OnCanvasChanged;
                _canvas.GraphChanged += OnCanvasChanged;
                _canvas.SelectionChanged += OnCanvasChanged;
                _canvas.NodePositionsCommitted += OnCanvasPositionsChanged;
            }
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (_canvas is null || _canvas.Graph.Nodes.Count == 0) return;
        RectangleF bounds = GetBounds(_canvas.Graph);
        float scale = Math.Min((ClientSize.Width - 8) / Math.Max(1, bounds.Width), (ClientSize.Height - 8) / Math.Max(1, bounds.Height));
        float ox = 4 - bounds.X * scale;
        float oy = 4 - bounds.Y * scale;
        foreach (GraphGroupDefinition group in _canvas.Graph.Editor.Groups)
        {
            using var brush = new SolidBrush(Color.FromArgb(50, 90, 135, 175));
            eventArgs.Graphics.FillRectangle(brush, ox + group.X * scale, oy + group.Y * scale, group.Width * scale, group.Height * scale);
        }
        foreach (NodeDefinition node in _canvas.Graph.Nodes)
        {
            NodeEditorMetadata metadata = node.Editor ?? new NodeEditorMetadata();
            using var brush = new SolidBrush(_canvas.SelectedNodeIds.Contains(node.Id) ? Color.White : Color.FromArgb(105, 150, 190));
            eventArgs.Graphics.FillRectangle(brush, ox + metadata.X * scale, oy + metadata.Y * scale, Math.Max(3, 190 * scale), Math.Max(2, 70 * scale));
        }
        using var border = new Pen(Color.FromArgb(130, 200, 220), 1f);
        eventArgs.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (_canvas is null || _canvas.Graph.Nodes.Count == 0) return;
        RectangleF bounds = GetBounds(_canvas.Graph);
        float scale = Math.Min((ClientSize.Width - 8) / Math.Max(1, bounds.Width), (ClientSize.Height - 8) / Math.Max(1, bounds.Height));
        float graphX = bounds.X + (eventArgs.X - 4) / scale;
        float graphY = bounds.Y + (eventArgs.Y - 4) / scale;
        PointF pan = new(_canvas.ClientSize.Width / (2f * _canvas.Zoom) - graphX, _canvas.ClientSize.Height / (2f * _canvas.Zoom) - graphY);
        _canvas.SetView(pan, _canvas.Zoom);
    }

    private void OnCanvasChanged(object? sender, EventArgs eventArgs) => Invalidate();
    private void OnCanvasPositionsChanged(object? sender, GraphNodePositionsEventArgs eventArgs) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing) Canvas = null;
        base.Dispose(disposing);
    }

    private static RectangleF GetBounds(SoundGraphDefinition graph)
    {
        NodeDefinition[] nodes = graph.Nodes.ToArray();
        float left = nodes.Min(node => node.Editor?.X ?? 0);
        float top = nodes.Min(node => node.Editor?.Y ?? 0);
        float right = nodes.Max(node => (node.Editor?.X ?? 0) + 190);
        float bottom = nodes.Max(node => (node.Editor?.Y ?? 0) + 90);
        return new RectangleF(left - 20, top - 20, right - left + 40, bottom - top + 40);
    }
}
