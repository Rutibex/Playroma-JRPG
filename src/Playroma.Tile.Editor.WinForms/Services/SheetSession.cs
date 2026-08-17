using System.Drawing;
using Playroma.Tile.Core.Models;
using Playroma.Tile.RpgMakerMV.Imaging;

namespace Playroma.Tile.Editor.WinForms.Services;

public sealed class SheetSession : IDisposable
{
    private const int MaximumHistory = 20;
    private readonly LinkedList<Bitmap> _undo = new();
    private readonly LinkedList<Bitmap> _redo = new();

    public SheetSession(TileSheetDefinition definition, string imagePath)
    {
        Definition = definition;
        ImagePath = imagePath;
        Bitmap = BitmapFile.LoadEditable(imagePath);
    }

    public TileSheetDefinition Definition { get; }
    public string ImagePath { get; private set; }
    public Bitmap Bitmap { get; private set; }
    public bool IsImageDirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void CaptureBeforeEdit()
    {
        _undo.AddLast((Bitmap)Bitmap.Clone());
        while (_undo.Count > MaximumHistory)
        {
            var oldest = _undo.First!.Value;
            _undo.RemoveFirst();
            oldest.Dispose();
        }

        ClearHistory(_redo);
    }

    public void MarkEdited()
    {
        IsImageDirty = true;
    }

    public bool Undo()
    {
        if (_undo.Last is null)
        {
            return false;
        }

        _redo.AddLast((Bitmap)Bitmap.Clone());
        var replacement = _undo.Last.Value;
        _undo.RemoveLast();
        Bitmap.Dispose();
        Bitmap = replacement;
        IsImageDirty = true;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Last is null)
        {
            return false;
        }

        _undo.AddLast((Bitmap)Bitmap.Clone());
        var replacement = _redo.Last.Value;
        _redo.RemoveLast();
        Bitmap.Dispose();
        Bitmap = replacement;
        IsImageDirty = true;
        return true;
    }

    public void ReplaceBitmap(Bitmap replacement, bool captureBeforeEdit = true)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (captureBeforeEdit) CaptureBeforeEdit();
        var next = new Bitmap(replacement.Width, replacement.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(next))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(replacement, 0, 0);
        }
        Bitmap.Dispose();
        Bitmap = next;
        IsImageDirty = true;
    }

    public void Save(string imagePath)
    {
        BitmapFile.SavePngAtomic(Bitmap, imagePath);
        ImagePath = imagePath;
        Definition.PixelWidth = Bitmap.Width;
        Definition.PixelHeight = Bitmap.Height;
        IsImageDirty = false;
    }

    public void Dispose()
    {
        Bitmap.Dispose();
        ClearHistory(_undo);
        ClearHistory(_redo);
    }

    private static void ClearHistory(LinkedList<Bitmap> list)
    {
        foreach (var bitmap in list)
        {
            bitmap.Dispose();
        }

        list.Clear();
    }
}
