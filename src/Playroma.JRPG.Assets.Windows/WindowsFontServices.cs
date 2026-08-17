using System.Drawing.Text;
using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Assets.Windows;

public sealed class PrivateFontPreviewSession : IDisposable
{
    private readonly PrivateFontCollection _collection = new();
    private bool _disposed;

    public PrivateFontPreviewSession(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        try
        {
            _collection.AddFontFile(fullPath);
            Family = _collection.Families.FirstOrDefault()
                ?? throw new InvalidDataException("Windows could not load a font family from the selected file.");
        }
        catch
        {
            _collection.Dispose();
            throw;
        }
    }

    public FontFamily Family { get; }

    public Font CreateFont(float emSize, FontStyle style = FontStyle.Regular) =>
        new(Family, emSize, Family.IsStyleAvailable(style) ? style : FontStyle.Regular, GraphicsUnit.Point);

    public void Dispose()
    {
        if (_disposed) return;
        _collection.Dispose();
        _disposed = true;
    }
}
