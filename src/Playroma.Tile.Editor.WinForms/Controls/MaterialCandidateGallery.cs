using Playroma.Tile.Materials.Services;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class MaterialCandidateGallery : Control
{
    private readonly List<Bitmap> _thumbnails = new();
    private IReadOnlyList<MaterialCandidate> _candidates = Array.Empty<MaterialCandidate>();
    public event EventHandler? SelectionChanged;
    public int SelectedIndex { get; private set; } = -1;
    public MaterialCandidate? SelectedCandidate => SelectedIndex >= 0 && SelectedIndex < _candidates.Count ? _candidates[SelectedIndex] : null;

    public MaterialCandidateGallery() { DoubleBuffered = true; BackColor = Color.FromArgb(42,42,45); AutoScrollOffset = Point.Empty; }
    public void SetCandidates(IReadOnlyList<MaterialCandidate> candidates)
    {
        foreach (var image in _thumbnails) image.Dispose(); _thumbnails.Clear(); _candidates = candidates;
        foreach (var candidate in candidates)
        {
            using var source = candidate.Result.BaseColor?.ToBitmap(); var thumb = new Bitmap(128,128);
            using var g = Graphics.FromImage(thumb); g.Clear(Color.Transparent); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            if (source is not null) g.DrawImage(source,new Rectangle(0,0,128,128)); _thumbnails.Add(thumb);
        }
        SelectedIndex = candidates.Count > 0 ? 0 : -1; SelectionChanged?.Invoke(this,EventArgs.Empty); Invalidate();
    }
    protected override void Dispose(bool disposing){if(disposing)foreach(var image in _thumbnails)image.Dispose();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var cell = 150; var cols=Math.Max(1,ClientSize.Width/cell);
        for(var i=0;i<_candidates.Count;i++){var x=(i%cols)*cell+8;var y=(i/cols)*cell+8;var rect=new Rectangle(x,y,132,132);e.Graphics.FillRectangle(i==SelectedIndex?Brushes.Goldenrod:Brushes.DimGray,rect);if(i<_thumbnails.Count)e.Graphics.DrawImageUnscaled(_thumbnails[i],x+2,y+2);var text=$"#{i+1}  Seed {_candidates[i].Seed}";TextRenderer.DrawText(e.Graphics,text,Font,new Rectangle(x,y+134,142,18),Color.White,TextFormatFlags.EndEllipsis);}
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);var cell=150;var cols=Math.Max(1,ClientSize.Width/cell);var index=e.Y/cell*cols+e.X/cell;if(index>=0&&index<_candidates.Count){SelectedIndex=index;SelectionChanged?.Invoke(this,EventArgs.Empty);Invalidate();}}
    public override Size GetPreferredSize(Size proposedSize){var cols=Math.Max(1,proposedSize.Width/150);var rows=(int)Math.Ceiling(_candidates.Count/(double)cols);return new Size(proposedSize.Width,Math.Max(160,rows*150));}
}
