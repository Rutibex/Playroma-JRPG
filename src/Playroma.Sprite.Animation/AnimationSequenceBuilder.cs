using Playroma.Sprite.Core;

namespace Playroma.Sprite.Animation;

public static class AnimationSequenceBuilder
{
    public static IReadOnlyList<int> Build(SpriteAnimationTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.FromFrame < 0 || tag.ToFrame < tag.FromFrame) throw new ArgumentOutOfRangeException(nameof(tag));
        int[] forward = Enumerable.Range(tag.FromFrame, tag.ToFrame - tag.FromFrame + 1).ToArray();
        return tag.Direction switch
        {
            AnimationPlaybackDirection.Forward => forward,
            AnimationPlaybackDirection.Reverse => forward.Reverse().ToArray(),
            AnimationPlaybackDirection.PingPong => PingPong(forward),
            AnimationPlaybackDirection.PingPongReverse => PingPong(forward.Reverse().ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(tag.Direction))
        };
    }

    private static int[] PingPong(int[] ordered)
    {
        if (ordered.Length <= 2) return ordered;
        return ordered.Concat(ordered.Skip(1).SkipLast(1).Reverse()).ToArray();
    }
}
