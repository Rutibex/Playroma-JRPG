using System.Drawing.Drawing2D;
using Playroma.Sound.Authoring.Graphs;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;

namespace Playroma.Sound.WinForms.Controls;

public sealed class GraphConnectionRequestEventArgs : EventArgs
{
    public GraphConnectionRequestEventArgs(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        FromNodeId = fromNodeId;
        FromPortId = fromPortId;
        ToNodeId = toNodeId;
        ToPortId = toPortId;
    }
    public string FromNodeId { get; }
    public string FromPortId { get; }
    public string ToNodeId { get; }
    public string ToPortId { get; }
}

public sealed class GraphAddNodeEventArgs : EventArgs
{
    public GraphAddNodeEventArgs(GraphNodePosition position) => Position = position;
    public GraphNodePosition Position { get; }
}

public sealed class GraphNodeActivatedEventArgs : EventArgs
{
    public GraphNodeActivatedEventArgs(string nodeId) => NodeId = nodeId;
    public string NodeId { get; }
}

public sealed class GraphNodePositionsEventArgs : EventArgs
{
    public GraphNodePositionsEventArgs(IReadOnlyDictionary<string, GraphNodePosition> positions) => Positions = positions;
    public IReadOnlyDictionary<string, GraphNodePosition> Positions { get; }
}

public sealed class GraphCanvasControl : Control
{
    private const float NodeWidth = 190f;
    private const float HeaderHeight = 30f;
    private const float PortRowHeight = 22f;
    private const float PortRadius = 5.5f;
    private const float MinimumZoom = 0.2f;
    private const float MaximumZoom = 2.5f;
    private readonly GraphNodeRegistry _registry;
    private readonly HashSet<string> _selectedNodeIds = new(StringComparer.Ordinal);
    private SoundGraphDefinition _graph = new();
    private Point _lastMouse;
    private PointF _pan;
    private float _zoom = 1f;
    private bool _panning;
    private bool _moving;
    private bool _boxSelecting;
    private PointF _selectionStart;
    private RectangleF _selectionRectangle;
    private Dictionary<string, PointF>? _moveOrigins;
    private PortHit? _connectionStart;
    private PointF _connectionMouse;
    private string _hoverNodeId = "";
    private string _diagnosticNodeId = "";

    public GraphCanvasControl(GraphNodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        BackColor = Color.FromArgb(35, 38, 43);
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? GraphChanged;
    public event EventHandler<GraphNodePositionsEventArgs>? NodePositionsCommitted;
    public event EventHandler<GraphConnectionRequestEventArgs>? ConnectionRequested;
    public event EventHandler? ViewChanged;
    public event EventHandler<GraphAddNodeEventArgs>? AddNodeRequested;
    public event EventHandler<GraphNodeActivatedEventArgs>? NodeDoubleClicked;

    public SoundGraphDefinition Graph => _graph;
    public IReadOnlyCollection<string> SelectedNodeIds => _selectedNodeIds;
    public float Zoom => _zoom;
    public PointF Pan => _pan;

    public void SetGraph(SoundGraphDefinition graph, bool preserveSelection = true)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        if (!preserveSelection) _selectedNodeIds.Clear();
        else _selectedNodeIds.RemoveWhere(id => !_graph.Nodes.Any(node => node.Id == id));
        _zoom = Math.Clamp(graph.Editor.Zoom <= 0 ? 1f : graph.Editor.Zoom, MinimumZoom, MaximumZoom);
        _pan = new PointF(graph.Editor.ViewX, graph.Editor.ViewY);
        GraphChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SelectNodes(IEnumerable<string> nodeIds, bool center = false)
    {
        _selectedNodeIds.Clear();
        foreach (string id in nodeIds)
            if (_graph.Nodes.Any(node => node.Id == id)) _selectedNodeIds.Add(id);
        if (center && _selectedNodeIds.Count > 0) CenterOnNodes(_selectedNodeIds);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SelectDiagnosticNode(string nodeId)
    {
        _diagnosticNodeId = nodeId ?? "";
        if (!string.IsNullOrWhiteSpace(nodeId)) SelectNodes([nodeId], true);
        Invalidate();
    }

    public void FitAll()
    {
        RectangleF bounds = CalculateGraphBounds();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            _zoom = 1f;
            _pan = PointF.Empty;
        }
        else
        {
            float availableWidth = Math.Max(100, ClientSize.Width - 80);
            float availableHeight = Math.Max(100, ClientSize.Height - 80);
            _zoom = Math.Clamp(Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height), MinimumZoom, 1.5f);
            _pan = new PointF((ClientSize.Width / _zoom - bounds.Width) / 2f - bounds.X, (ClientSize.Height / _zoom - bounds.Height) / 2f - bounds.Y);
        }
        RaiseViewChanged();
    }

    public void CenterOnNodes(IEnumerable<string> nodeIds)
    {
        NodeDefinition[] nodes = _graph.Nodes.Where(node => nodeIds.Contains(node.Id, StringComparer.Ordinal)).ToArray();
        if (nodes.Length == 0) return;
        RectangleF bounds = BoundsOf(nodes);
        _pan = new PointF(ClientSize.Width / (2f * _zoom) - (bounds.Left + bounds.Width / 2f), ClientSize.Height / (2f * _zoom) - (bounds.Top + bounds.Height / 2f));
        RaiseViewChanged();
    }

    public GraphNodePosition ScreenToGraph(Point screen)
    {
        PointF graph = ScreenToGraphPoint(screen);
        return new GraphNodePosition(graph.X, graph.Y);
    }

    public void SetView(PointF pan, float zoom)
    {
        _pan = pan;
        _zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        RaiseViewChanged();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.TranslateTransform(_pan.X * _zoom, _pan.Y * _zoom);
        graphics.ScaleTransform(_zoom, _zoom);
        DrawGrid(graphics);
        DrawGroups(graphics);
        DrawComments(graphics);
        DrawConnections(graphics);
        DrawNodes(graphics);
        DrawConnectionPreview(graphics);
        DrawSelectionRectangle(graphics);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        PointF before = ScreenToGraphPoint(eventArgs.Location);
        float factor = eventArgs.Delta > 0 ? 1.12f : 1f / 1.12f;
        _zoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
        PointF after = ScreenToGraphPoint(eventArgs.Location);
        _pan.X += after.X - before.X;
        _pan.Y += after.Y - before.Y;
        RaiseViewChanged();
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        _lastMouse = eventArgs.Location;
        PointF graphPoint = ScreenToGraphPoint(eventArgs.Location);
        if (eventArgs.Button == MouseButtons.Middle || eventArgs.Button == MouseButtons.Right)
        {
            _panning = true;
            Cursor = Cursors.Hand;
            Capture = true;
            return;
        }
        if (eventArgs.Button != MouseButtons.Left) return;
        PortHit? port = HitTestPort(graphPoint);
        if (port is { Descriptor.Direction: GraphPortDirection.Output })
        {
            _connectionStart = port;
            _connectionMouse = graphPoint;
            Capture = true;
            return;
        }
        NodeDefinition? node = HitTestNode(graphPoint);
        if (node is not null)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (!_selectedNodeIds.Add(node.Id)) _selectedNodeIds.Remove(node.Id);
            }
            else if (!_selectedNodeIds.Contains(node.Id))
            {
                _selectedNodeIds.Clear();
                _selectedNodeIds.Add(node.Id);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            _moveOrigins = _selectedNodeIds.ToDictionary(id => id, id =>
            {
                NodeEditorMetadata metadata = _graph.Nodes.First(item => item.Id == id).Editor ??= new NodeEditorMetadata();
                return new PointF(metadata.X, metadata.Y);
            }, StringComparer.Ordinal);
            _selectionStart = graphPoint;
            _moving = true;
            Capture = true;
            Invalidate();
            return;
        }
        if ((ModifierKeys & Keys.Control) != Keys.Control) _selectedNodeIds.Clear();
        _boxSelecting = true;
        _selectionStart = graphPoint;
        _selectionRectangle = new RectangleF(graphPoint, SizeF.Empty);
        Capture = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        PointF graphPoint = ScreenToGraphPoint(eventArgs.Location);
        if (_panning)
        {
            _pan.X += (eventArgs.X - _lastMouse.X) / _zoom;
            _pan.Y += (eventArgs.Y - _lastMouse.Y) / _zoom;
            _lastMouse = eventArgs.Location;
            RaiseViewChanged();
            return;
        }
        if (_connectionStart is not null)
        {
            _connectionMouse = graphPoint;
            Invalidate();
            return;
        }
        if (_moving && _moveOrigins is not null)
        {
            float dx = graphPoint.X - _selectionStart.X;
            float dy = graphPoint.Y - _selectionStart.Y;
            bool snap = _graph.Editor.SnapToGrid && (ModifierKeys & Keys.Alt) != Keys.Alt;
            foreach ((string id, PointF origin) in _moveOrigins)
            {
                NodeDefinition node = _graph.Nodes.First(item => item.Id == id);
                node.Editor ??= new NodeEditorMetadata();
                node.Editor.X = snap ? Snap(origin.X + dx, 16) : origin.X + dx;
                node.Editor.Y = snap ? Snap(origin.Y + dy, 16) : origin.Y + dy;
            }
            Invalidate();
            return;
        }
        if (_boxSelecting)
        {
            _selectionRectangle = NormalizeRectangle(_selectionStart, graphPoint);
            Invalidate();
            return;
        }
        string hovered = HitTestNode(graphPoint)?.Id ?? "";
        if (hovered != _hoverNodeId)
        {
            _hoverNodeId = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        PointF graphPoint = ScreenToGraphPoint(eventArgs.Location);
        if (_panning)
        {
            _panning = false;
            Cursor = Cursors.Default;
            Capture = false;
            return;
        }
        if (_connectionStart is { } start)
        {
            PortHit? target = HitTestPort(graphPoint);
            _connectionStart = null;
            Capture = false;
            if (target is PortHit inputTarget && inputTarget.Descriptor.Direction == GraphPortDirection.Input)
                ConnectionRequested?.Invoke(this, new GraphConnectionRequestEventArgs(start.Node.Id, start.Descriptor.Id, inputTarget.Node.Id, inputTarget.Descriptor.Id));
            Invalidate();
            return;
        }
        if (_moving && _moveOrigins is not null)
        {
            _moving = false;
            Capture = false;
            var positions = _selectedNodeIds.ToDictionary(id => id, id =>
            {
                NodeEditorMetadata metadata = _graph.Nodes.First(item => item.Id == id).Editor ?? new NodeEditorMetadata();
                return new GraphNodePosition(metadata.X, metadata.Y);
            }, StringComparer.Ordinal);
            _moveOrigins = null;
            NodePositionsCommitted?.Invoke(this, new GraphNodePositionsEventArgs(positions));
            return;
        }
        if (_boxSelecting)
        {
            _boxSelecting = false;
            Capture = false;
            foreach (NodeDefinition node in _graph.Nodes)
            {
                if (_selectionRectangle.IntersectsWith(GetNodeBounds(node))) _selectedNodeIds.Add(node.Id);
            }
            _selectionRectangle = RectangleF.Empty;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs eventArgs)
    {
        base.OnMouseDoubleClick(eventArgs);
        PointF point = ScreenToGraphPoint(eventArgs.Location);
        NodeDefinition? node = HitTestNode(point);
        if (node is not null) NodeDoubleClicked?.Invoke(this, new GraphNodeActivatedEventArgs(node.Id));
        else AddNodeRequested?.Invoke(this, new GraphAddNodeEventArgs(new GraphNodePosition(point.X, point.Y)));
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.KeyCode == Keys.F && eventArgs.Control)
        {
            FitAll();
            eventArgs.SuppressKeyPress = true;
        }
    }

    private void DrawGrid(Graphics graphics)
    {
        if (!_graph.Editor.ShowGrid) return;
        RectangleF visible = GetVisibleGraphRectangle();
        using var minor = new Pen(Color.FromArgb(48, 53, 60), 1f / _zoom);
        using var major = new Pen(Color.FromArgb(60, 67, 76), 1.2f / _zoom);
        int startX = (int)Math.Floor(visible.Left / 16) * 16;
        int endX = (int)Math.Ceiling(visible.Right / 16) * 16;
        int startY = (int)Math.Floor(visible.Top / 16) * 16;
        int endY = (int)Math.Ceiling(visible.Bottom / 16) * 16;
        for (int x = startX; x <= endX; x += 16) graphics.DrawLine(x % 64 == 0 ? major : minor, x, startY, x, endY);
        for (int y = startY; y <= endY; y += 16) graphics.DrawLine(y % 64 == 0 ? major : minor, startX, y, endX, y);
    }

    private void DrawGroups(Graphics graphics)
    {
        using var fill = new SolidBrush(Color.FromArgb(30, 90, 130, 175));
        using var border = new Pen(Color.FromArgb(110, 120, 170, 210), 1.5f / _zoom) { DashStyle = DashStyle.Dash };
        using var font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f, FontStyle.Bold);
        using var text = new SolidBrush(Color.FromArgb(190, 215, 235));
        foreach (GraphGroupDefinition group in _graph.Editor.Groups)
        {
            var bounds = new RectangleF(group.X, group.Y, group.Width, group.Collapsed ? 32 : group.Height);
            graphics.FillRectangle(fill, bounds);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            graphics.DrawString(group.Title, font, text, bounds.X + 8, bounds.Y + 7);
        }
    }

    private void DrawComments(Graphics graphics)
    {
        using var fill = new SolidBrush(Color.FromArgb(210, 247, 228, 128));
        using var border = new Pen(Color.FromArgb(180, 170, 145, 70), 1f / _zoom);
        using var text = new SolidBrush(Color.FromArgb(55, 48, 24));
        foreach (GraphCommentDefinition comment in _graph.Editor.Comments)
        {
            var bounds = new RectangleF(comment.X, comment.Y, comment.Width, comment.Height);
            graphics.FillRectangle(fill, bounds);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            graphics.DrawString(comment.Text, SystemFonts.MessageBoxFont!, text, new RectangleF(bounds.X + 8, bounds.Y + 8, bounds.Width - 16, bounds.Height - 16));
        }
    }

    private void DrawConnections(Graphics graphics)
    {
        foreach (ConnectionDefinition connection in _graph.Connections)
        {
            NodeDefinition? from = _graph.Nodes.FirstOrDefault(node => node.Id == connection.FromNodeId);
            NodeDefinition? to = _graph.Nodes.FirstOrDefault(node => node.Id == connection.ToNodeId);
            if (from is null || to is null) continue;
            PortHit? fromPort = GetPortHit(from, connection.FromPortId, GraphPortDirection.Output);
            PortHit? toPort = GetPortHit(to, connection.ToPortId, GraphPortDirection.Input);
            if (fromPort is null || toPort is null) continue;
            Color color = PortColor(fromPort.Value.Descriptor.Type);
            using var pen = new Pen(Color.FromArgb(190, color), 2.2f / _zoom);
            DrawCurve(graphics, pen, fromPort.Value.Center, toPort.Value.Center);
        }
    }

    private void DrawNodes(Graphics graphics)
    {
        foreach (NodeDefinition node in _graph.Nodes)
        {
            NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(node.TypeId).Descriptor;
            RectangleF bounds = GetNodeBounds(node);
            bool selected = _selectedNodeIds.Contains(node.Id);
            bool hover = node.Id == _hoverNodeId;
            bool diagnostic = node.Id == _diagnosticNodeId;
            Color categoryColor = CategoryColor(descriptor.Category);
            using var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
            graphics.FillRectangle(shadow, bounds.X + 5, bounds.Y + 6, bounds.Width, bounds.Height);
            using var body = new SolidBrush(node.Editor?.Bypassed == true ? Color.FromArgb(58, 58, 58) : Color.FromArgb(57, 62, 70));
            using var header = new SolidBrush(categoryColor);
            using var border = new Pen(diagnostic ? Color.OrangeRed : selected ? Color.White : hover ? Color.LightSteelBlue : Color.FromArgb(105, 112, 123), (selected ? 2.2f : 1.2f) / _zoom);
            graphics.FillRectangle(body, bounds);
            graphics.FillRectangle(header, bounds.X, bounds.Y, bounds.Width, HeaderHeight);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            using var titleFont = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9f, FontStyle.Bold);
            using var idFont = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5f, FontStyle.Regular);
            using var white = new SolidBrush(Color.White);
            using var muted = new SolidBrush(Color.FromArgb(205, 215, 225));
            graphics.DrawString(descriptor.DisplayName, titleFont, white, bounds.X + 8, bounds.Y + 6);
            graphics.DrawString(node.Id, idFont, muted, bounds.X + 8, bounds.Y + HeaderHeight + 4);
            if (node.Editor?.Bypassed == true) graphics.DrawString("BYPASSED", idFont, Brushes.Gold, bounds.Right - 65, bounds.Y + 8);
            if (node.Editor?.Collapsed == true) continue;
            foreach (PortHit port in GetPorts(node))
            {
                using var portBrush = new SolidBrush(PortColor(port.Descriptor.Type));
                graphics.FillEllipse(portBrush, port.Center.X - PortRadius, port.Center.Y - PortRadius, PortRadius * 2, PortRadius * 2);
                string label = port.Descriptor.DisplayName;
                SizeF size = graphics.MeasureString(label, idFont);
                float x = port.Descriptor.Direction == GraphPortDirection.Input ? port.Center.X + 10 : port.Center.X - 10 - size.Width;
                graphics.DrawString(label, idFont, muted, x, port.Center.Y - size.Height / 2);
            }
        }
    }

    private void DrawConnectionPreview(Graphics graphics)
    {
        if (_connectionStart is not { } start) return;
        using var pen = new Pen(Color.FromArgb(220, PortColor(start.Descriptor.Type)), 2f / _zoom) { DashStyle = DashStyle.Dash };
        DrawCurve(graphics, pen, start.Center, _connectionMouse);
    }

    private void DrawSelectionRectangle(Graphics graphics)
    {
        if (!_boxSelecting || _selectionRectangle.IsEmpty) return;
        using var fill = new SolidBrush(Color.FromArgb(45, 100, 165, 230));
        using var border = new Pen(Color.FromArgb(190, 125, 190, 245), 1f / _zoom) { DashStyle = DashStyle.Dash };
        graphics.FillRectangle(fill, _selectionRectangle);
        graphics.DrawRectangle(border, _selectionRectangle.X, _selectionRectangle.Y, _selectionRectangle.Width, _selectionRectangle.Height);
    }

    private NodeDefinition? HitTestNode(PointF point) => _graph.Nodes.AsEnumerable().Reverse().FirstOrDefault(node => GetNodeBounds(node).Contains(point));

    private PortHit? HitTestPort(PointF point)
    {
        foreach (NodeDefinition node in _graph.Nodes.AsEnumerable().Reverse())
        foreach (PortHit port in GetPorts(node))
            if (DistanceSquared(point, port.Center) <= 100f) return port;
        return null;
    }

    private IEnumerable<PortHit> GetPorts(NodeDefinition node)
    {
        NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(node.TypeId).Descriptor;
        RectangleF bounds = GetNodeBounds(node);
        GraphPortDescriptor[] inputs = descriptor.Ports.Where(port => port.Direction == GraphPortDirection.Input).ToArray();
        GraphPortDescriptor[] outputs = descriptor.Ports.Where(port => port.Direction == GraphPortDirection.Output).ToArray();
        for (int index = 0; index < inputs.Length; index++) yield return new PortHit(node, inputs[index], new PointF(bounds.Left, bounds.Top + HeaderHeight + 28 + index * PortRowHeight));
        for (int index = 0; index < outputs.Length; index++) yield return new PortHit(node, outputs[index], new PointF(bounds.Right, bounds.Top + HeaderHeight + 28 + index * PortRowHeight));
    }

    private PortHit? GetPortHit(NodeDefinition node, string portId, GraphPortDirection direction)
    {
        foreach (PortHit port in GetPorts(node))
            if (port.Descriptor.Id == portId && port.Descriptor.Direction == direction) return port;
        return null;
    }

    private RectangleF GetNodeBounds(NodeDefinition node)
    {
        NodeEditorMetadata metadata = node.Editor ??= new NodeEditorMetadata();
        NodeTypeDescriptor descriptor = _registry.GetRequiredFactory(node.TypeId).Descriptor;
        int rows = Math.Max(descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Input), descriptor.Ports.Count(port => port.Direction == GraphPortDirection.Output));
        float height = metadata.Collapsed ? HeaderHeight : Math.Max(74, HeaderHeight + 20 + Math.Max(1, rows) * PortRowHeight);
        return new RectangleF(metadata.X, metadata.Y, NodeWidth, height);
    }

    private RectangleF CalculateGraphBounds()
    {
        var rectangles = _graph.Nodes.Select(GetNodeBounds)
            .Concat(_graph.Editor.Groups.Select(group => new RectangleF(group.X, group.Y, group.Width, group.Height)))
            .Concat(_graph.Editor.Comments.Select(comment => new RectangleF(comment.X, comment.Y, comment.Width, comment.Height)))
            .ToArray();
        if (rectangles.Length == 0) return new RectangleF(0, 0, 1, 1);
        RectangleF result = rectangles[0];
        foreach (RectangleF rectangle in rectangles.Skip(1)) result = RectangleF.Union(result, rectangle);
        result.Inflate(30, 30);
        return result;
    }

    private RectangleF BoundsOf(IEnumerable<NodeDefinition> nodes)
    {
        RectangleF[] bounds = nodes.Select(GetNodeBounds).ToArray();
        RectangleF result = bounds[0];
        foreach (RectangleF rectangle in bounds.Skip(1)) result = RectangleF.Union(result, rectangle);
        return result;
    }

    private RectangleF GetVisibleGraphRectangle()
    {
        PointF leftTop = ScreenToGraphPoint(Point.Empty);
        PointF rightBottom = ScreenToGraphPoint(new Point(ClientSize.Width, ClientSize.Height));
        return NormalizeRectangle(leftTop, rightBottom);
    }

    private PointF ScreenToGraphPoint(Point point) => new(point.X / _zoom - _pan.X, point.Y / _zoom - _pan.Y);
    private static RectangleF NormalizeRectangle(PointF a, PointF b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    private static float Snap(float value, float grid) => MathF.Round(value / grid) * grid;
    private static float DistanceSquared(PointF a, PointF b) { float x = a.X - b.X; float y = a.Y - b.Y; return x * x + y * y; }

    private static void DrawCurve(Graphics graphics, Pen pen, PointF from, PointF to)
    {
        float distance = Math.Max(50, Math.Abs(to.X - from.X) * 0.45f);
        using var path = new GraphicsPath();
        path.AddBezier(from, new PointF(from.X + distance, from.Y), new PointF(to.X - distance, to.Y), to);
        graphics.DrawPath(pen, path);
    }

    private static Color CategoryColor(GraphNodeCategory category) => category switch
    {
        GraphNodeCategory.Generator => Color.FromArgb(72, 128, 92),
        GraphNodeCategory.Processor => Color.FromArgb(90, 93, 155),
        GraphNodeCategory.Router => Color.FromArgb(155, 108, 62),
        GraphNodeCategory.Control => Color.FromArgb(150, 75, 132),
        GraphNodeCategory.Event => Color.FromArgb(163, 73, 73),
        GraphNodeCategory.Output => Color.FromArgb(45, 125, 145),
        _ => Color.FromArgb(95, 105, 115)
    };

    private static Color PortColor(GraphPortType type) => type switch
    {
        GraphPortType.AudioMono => Color.FromArgb(80, 205, 235),
        GraphPortType.AudioStereo => Color.FromArgb(65, 180, 235),
        GraphPortType.AudioAnyChannels => Color.FromArgb(100, 190, 230),
        GraphPortType.ControlScalar => Color.FromArgb(245, 194, 78),
        GraphPortType.Trigger => Color.FromArgb(239, 103, 97),
        GraphPortType.Event => Color.FromArgb(205, 122, 232),
        _ => Color.White
    };

    private void RaiseViewChanged()
    {
        _graph.Editor.ViewX = _pan.X;
        _graph.Editor.ViewY = _pan.Y;
        _graph.Editor.Zoom = _zoom;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private readonly record struct PortHit(NodeDefinition Node, GraphPortDescriptor Descriptor, PointF Center);
}
