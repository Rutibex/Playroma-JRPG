using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed class SpritePackageData
{
    public SpritePackageData(SpriteDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public SpriteDocument Document { get; }
    public Dictionary<Guid, PixelBuffer> PixelBuffers { get; } = [];

    public PixelBuffer GetRequired(Guid contentId) => PixelBuffers.TryGetValue(contentId, out PixelBuffer? buffer)
        ? buffer
        : throw new KeyNotFoundException($"Pixel content '{contentId}' is not loaded.");

    public SpritePackageData DeepClone()
    {
        SpriteDocument document = SpriteDocumentJsonSerializer.Deserialize(SpriteDocumentJsonSerializer.Serialize(Document));
        SpritePackageData clone = new(document);
        foreach ((Guid id, PixelBuffer buffer) in PixelBuffers) clone.PixelBuffers[id] = buffer.Clone();
        return clone;
    }

    public long EstimateMemoryBytes() => PixelBuffers.Values.Sum(buffer => checked((long)buffer.Width * buffer.Height * 4L));

    public static SpritePackageData CreateEmpty(string name, int width, int height)
    {
        SpriteDocument document = SpriteDocument.CreateEmpty(name, width, height);
        SpritePackageData package = new(document);
        foreach (SpritePixelContentDescriptor descriptor in document.PixelContents)
            package.PixelBuffers[descriptor.Id] = new PixelBuffer(descriptor.Width, descriptor.Height);
        return package;
    }
}
