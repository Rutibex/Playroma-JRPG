using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Animation;

public static class AnimationFrameRenderer
{
    public static PixelBuffer RenderFrame(SpritePackageData package, int frameIndex, bool includeReferenceLayers = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (frameIndex < 0 || frameIndex >= package.Document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        Guid frameId = package.Document.Frames[frameIndex].Id;
        return LayerCompositor.Composite(package.Document, package.PixelBuffers, frameId, includeReferenceLayers);
    }

    public static IReadOnlyList<PixelBuffer> RenderFrames(SpritePackageData package, IEnumerable<int>? frameIndexes = null, bool includeReferenceLayers = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        IEnumerable<int> indexes = frameIndexes ?? Enumerable.Range(0, package.Document.Frames.Count);
        return indexes.Select(index => RenderFrame(package, index, includeReferenceLayers)).ToArray();
    }
}
