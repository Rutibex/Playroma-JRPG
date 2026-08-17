using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Models;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class MaterialGraphCanvas : Control
{
    private readonly Dictionary<Guid, RectangleF> _bounds = new();
    private MaterialGraph? _graph;
    private MaterialNodeRegistry? _registry;
    private Guid? _dragNode;
    private Point _dragStart;
    private PointF _nodeStart;
    public event EventHandler? SelectedNodeChanged;
    public event EventHandler? GraphChanged;
    public Guid? SelectedNodeId { get; private set; }

    public MaterialGraph? Graph { get => _graph; set { _graph = value; SelectedNodeId = null; Invalidate(); } }
    public MaterialNodeRegistry? Registry { get => _registry; set { _registry = value; Invalidate(); } }
    public MaterialNodeInstance? SelectedNode => SelectedNodeId is Guid id ? _graph?.Find(id) : null;

    public MaterialGraphCanvas()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(35, 35, 38); AutoScrollOffset = Point.Empty;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; _bounds.Clear();
        if (_graph is null) return;
        foreach (var node in _graph.Nodes)
        {
            var rect = NodeRect(node); _bounds[node.Id] = rect;
            foreach (var input in node.Inputs.Values)
            {
                if (_graph.Find(input.SourceNodeId) is not { } source) continue;
                var sourceRect = NodeRect(source); var a = new PointF(sourceRect.Right, sourceRect.Top + 28); var b = new PointF(rect.Left, rect.Top + 28);
                using var pen = new Pen(Color.FromArgb(100, 185, 230), 2f); e.Graphics.DrawBezier(pen, a, new PointF(a.X + 45, a.Y), new PointF(b.X - 45, b.Y), b);
            }
        }
        foreach (var node in _graph.Nodes) DrawNode(e.Graphics, node, _bounds[node.Id]);
    }

    private void DrawNode(Graphics g, MaterialNodeInstance node, RectangleF rect)
    {
        var selected = SelectedNodeId == node.Id; var definition = _registry?.Get(node.TypeId);
        using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)); g.FillRectangle(shadow, rect.X + 4, rect.Y + 5, rect.Width, rect.Height);
        using var body = new SolidBrush(selected ? Color.FromArgb(64, 76, 88) : Color.FromArgb(52, 52, 56)); g.FillRectangle(body, rect);
        using var header = new SolidBrush(CategoryColor(definition?.Category ?? MaterialNodeCategory.Output)); g.FillRectangle(header, rect.X, rect.Y, rect.Width, 26);
        using var border = new Pen(selected ? Color.Gold : Color.FromArgb(100, 100, 106), selected ? 2f : 1f); g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(node.Name) ? definition?.DisplayName ?? node.TypeId : node.Name, Font, Rectangle.Round(new RectangleF(rect.X + 6, rect.Y + 4, rect.Width - 12, 20)), Color.White, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, definition?.TypeId ?? node.TypeId, Font, Rectangle.Round(new RectangleF(rect.X + 7, rect.Y + 33, rect.Width - 14, 18)), Color.Gainsboro, TextFormatFlags.EndEllipsis);
        var inputCount = definition?.Inputs.Count ?? 0; var outputCount = definition?.Outputs.Count ?? 0;
        TextRenderer.DrawText(g, $"{inputCount} in  •  {outputCount} out", Font, Rectangle.Round(new RectangleF(rect.X + 7, rect.Bottom - 23, rect.Width - 14, 17)), Color.Silver);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); var hit = _bounds.FirstOrDefault(x => x.Value.Contains(e.Location));
        SelectedNodeId = hit.Key == Guid.Empty ? null : hit.Key; SelectedNodeChanged?.Invoke(this, EventArgs.Empty); Invalidate();
        if (e.Button == MouseButtons.Left && SelectedNode is { } node) { _dragNode = node.Id; _dragStart = e.Location; _nodeStart = new PointF(node.EditorX, node.EditorY); Capture = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (_dragNode is not Guid id || _graph?.Find(id) is not { } node) return;
        node.EditorX = _nodeStart.X + e.X - _dragStart.X; node.EditorY = _nodeStart.Y + e.Y - _dragStart.Y; GraphChanged?.Invoke(this, EventArgs.Empty); Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragNode = null; Capture = false; }
    public void SelectNode(Guid? id) { SelectedNodeId = id; SelectedNodeChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
    private static RectangleF NodeRect(MaterialNodeInstance node) => new(node.EditorX, node.EditorY, 175, 82);
    private static Color CategoryColor(MaterialNodeCategory category) => category switch
    {
        MaterialNodeCategory.Sources => Color.FromArgb(62, 113, 79), MaterialNodeCategory.Noise => Color.FromArgb(95, 74, 142), MaterialNodeCategory.Patterns => Color.FromArgb(139, 91, 55),
        MaterialNodeCategory.Transform => Color.FromArgb(70, 105, 130), MaterialNodeCategory.Filter => Color.FromArgb(120, 70, 92), MaterialNodeCategory.Simulation => Color.FromArgb(130, 77, 50),
        MaterialNodeCategory.Synthesis => Color.FromArgb(80, 119, 119), MaterialNodeCategory.PixelArt => Color.FromArgb(120, 111, 57), _ => Color.FromArgb(64, 93, 134)
    };
}
