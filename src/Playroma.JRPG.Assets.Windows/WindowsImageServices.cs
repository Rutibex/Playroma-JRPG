using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets.Windows;

public static class AssetImageLoader
{
    /// <summary>Loads an owned bitmap while closing the source stream immediately.</summary>
    public static async ValueTask<Bitmap> LoadOwnedBitmapAsync(
        IAssetContentSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using Stream input = await source.OpenReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        using Image decoded = Image.FromStream(buffer, useEmbeddedColorManagement: false, validateImageData: true);
        Bitmap owned = new(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb);
        try
        {
            float horizontalResolution = decoded.HorizontalResolution > 0 ? decoded.HorizontalResolution : 96f;
            float verticalResolution = decoded.VerticalResolution > 0 ? decoded.VerticalResolution : 96f;
            owned.SetResolution(horizontalResolution, verticalResolution);
            using Graphics graphics = Graphics.FromImage(owned);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(decoded, new Rectangle(0, 0, owned.Width, owned.Height));
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    public static Bitmap CloneRegion(Bitmap source, Rectangle region)
    {
        Rectangle clipped = Rectangle.Intersect(new Rectangle(Point.Empty, source.Size), region);
        if (clipped.Width <= 0 || clipped.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        return source.Clone(clipped, PixelFormat.Format32bppPArgb);
    }
}

public sealed class WindowsImageThumbnailProvider : IAssetThumbnailProvider
{
    private readonly IAssetContentSource _content;

    public WindowsImageThumbnailProvider(IAssetContentSource content) =>
        _content = content ?? throw new ArgumentNullException(nameof(content));

    public string Id => "windows-image";
    public int Version => 2;

    public bool CanHandle(AssetRecord asset, AssetVariant variant) =>
        asset.MediaKind == AssetMediaKind.Image && !variant.IsEncrypted;

    public async ValueTask<AssetThumbnail> CreateAsync(
        AssetRecord asset,
        AssetVariant variant,
        AssetThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        using Bitmap source = await AssetImageLoader.LoadOwnedBitmapAsync(_content, variant.RelativePath, cancellationToken).ConfigureAwait(false);
        Size target = Fit(source.Size, new Size(request.EffectiveWidth, request.EffectiveHeight));
        using Bitmap output = new(request.EffectiveWidth, request.EffectiveHeight, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = request.PreservePixelEdges
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = request.PreservePixelEdges ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
            int x = (output.Width - target.Width) / 2;
            int y = (output.Height - target.Height) / 2;
            graphics.DrawImage(source, new Rectangle(x, y, target.Width, target.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        }

        using MemoryStream stream = new();
        output.Save(stream, ImageFormat.Png);
        return new AssetThumbnail(stream.ToArray(), output.Width, output.Height, string.Empty, []);
    }

    private static Size Fit(Size source, Size bounds)
    {
        if (source.Width <= 0 || source.Height <= 0) return bounds;
        double scale = Math.Min(bounds.Width / (double)source.Width, bounds.Height / (double)source.Height);
        scale = Math.Min(1d, scale);
        return new Size(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
    }
}

public sealed record SpriteFramePreview(
    string Name,
    int Index,
    Rectangle SourceRectangle);

public static class MvImagePreviewRecipes
{
    private static readonly string[] SideViewMotions =
    [
        "Walk", "Wait", "Chant", "Guard", "Damage", "Evade",
        "Thrust", "Swing", "Missile", "Skill", "Spell", "Item",
        "Escape", "Victory", "Dying", "Abnormal", "Sleep", "Dead"
    ];

    public static IReadOnlyList<SpriteFramePreview> CharacterFrames(AssetKey key, ImageAssetMetadata metadata)
    {
        CharacterSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeCharacter(key.LogicalName, metadata.Width, metadata.Height);
        if (!descriptor.IsValid) return [];
        return descriptor.Cells.Select(cell => new SpriteFramePreview($"Cell {cell.Index}", cell.Index, ToRectangle(cell))).ToArray();
    }

    public static IReadOnlyList<SpriteFramePreview> FaceFrames(ImageAssetMetadata metadata)
    {
        GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeFaces(metadata.Width, metadata.Height);
        return descriptor.IsValid
            ? descriptor.Cells.Select(cell => new SpriteFramePreview($"Face {cell.Index}", cell.Index, ToRectangle(cell))).ToArray()
            : [];
    }

    public static IReadOnlyList<SpriteFramePreview> AnimationCells(ImageAssetMetadata metadata)
    {
        GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeAnimation(metadata.Width, metadata.Height);
        return descriptor.IsValid
            ? descriptor.Cells.Select(cell => new SpriteFramePreview($"Cell {cell.Index}", cell.Index, ToRectangle(cell))).ToArray()
            : [];
    }

    public static IReadOnlyList<SpriteFramePreview> IconCells(ImageAssetMetadata metadata)
    {
        GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeIconSet(metadata.Width, metadata.Height);
        return descriptor.IsValid
            ? descriptor.Cells.Select(cell => new SpriteFramePreview($"Icon {cell.Index}", cell.Index, ToRectangle(cell))).ToArray()
            : [];
    }

    public static IReadOnlyList<SpriteFramePreview> SideViewActorFrames(ImageAssetMetadata metadata)
    {
        GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeSideViewActor(metadata.Width, metadata.Height);
        if (!descriptor.IsValid) return [];
        List<SpriteFramePreview> result = [];
        for (int motion = 0; motion < SideViewMotions.Length; motion++)
        {
            int blockX = motion % 3;
            int blockY = motion / 3;
            for (int frame = 0; frame < 3; frame++)
            {
                int column = blockX * 3 + frame;
                int index = blockY * 9 + column;
                SheetCell cell = descriptor.Cells[index];
                result.Add(new SpriteFramePreview($"{SideViewMotions[motion]} {frame + 1}", motion * 3 + frame, ToRectangle(cell)));
            }
        }
        return result;
    }

    private static Rectangle ToRectangle(SheetCell cell) => new(cell.X, cell.Y, cell.Width, cell.Height);
}

public sealed class CheckerboardPanel : Panel
{
    public CheckerboardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        const int cell = 12;
        for (int y = 0; y < Height; y += cell)
        {
            for (int x = 0; x < Width; x += cell)
            {
                bool alternate = ((x / cell) + (y / cell)) % 2 == 0;
                using SolidBrush brush = new(alternate ? Color.WhiteSmoke : Color.Gainsboro);
                e.Graphics.FillRectangle(brush, x, y, cell, cell);
            }
        }
    }
}
