using Playroma.Tile.Materials.Buffers;

namespace Playroma.Tile.Editor.WinForms.Controls;

public sealed class MaterialPreviewControl : Control
{
    private ColorBuffer? _buffer; private Bitmap? _bitmap;
    public bool Tiled { get; set; } = true;
    public bool ShowGrid { get; set; }
    public ColorBuffer? Buffer { get=>_buffer; set { _buffer=value; _bitmap?.Dispose(); _bitmap=value?.ToBitmap(); Invalidate(); } }
    public MaterialPreviewControl(){DoubleBuffered=true;BackColor=Color.FromArgb(28,28,30);}
    protected override void Dispose(bool disposing){if(disposing)_bitmap?.Dispose();base.Dispose(disposing);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(_bitmap is null)return;e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;e.Graphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;if(Tiled){var scale=Math.Max(1,Math.Min(ClientSize.Width/Math.Max(1,_bitmap.Width*3),ClientSize.Height/Math.Max(1,_bitmap.Height*3)));var w=_bitmap.Width*scale;var h=_bitmap.Height*scale;var ox=(ClientSize.Width-w*3)/2;var oy=(ClientSize.Height-h*3)/2;for(var y=0;y<3;y++)for(var x=0;x<3;x++)e.Graphics.DrawImage(_bitmap,new Rectangle(ox+x*w,oy+y*h,w,h));if(ShowGrid){using var p=new Pen(Color.FromArgb(120,255,255,255));for(var i=0;i<=3;i++){e.Graphics.DrawLine(p,ox+i*w,oy,ox+i*w,oy+3*h);e.Graphics.DrawLine(p,ox,oy+i*h,ox+3*w,oy+i*h);}}}else{var scale=Math.Max(1,Math.Min(ClientSize.Width/_bitmap.Width,ClientSize.Height/_bitmap.Height));var w=_bitmap.Width*scale;var h=_bitmap.Height*scale;e.Graphics.DrawImage(_bitmap,new Rectangle((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h));}}
}
