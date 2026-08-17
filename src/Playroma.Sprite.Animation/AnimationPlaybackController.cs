using Playroma.Sprite.Core;

namespace Playroma.Sprite.Animation;

public sealed class AnimationPlaybackController
{
    private IReadOnlyList<int> sequence = [];
    private int cursor;
    private double elapsedMilliseconds;

    public bool IsPlaying { get; private set; }
    public double SpeedMultiplier { get; set; } = 1d;
    public bool Loop { get; set; } = true;
    public int CurrentFrameIndex => sequence.Count == 0 ? 0 : sequence[cursor];
    public IReadOnlyList<int> Sequence => sequence;

    public void Configure(SpriteDocument document, SpriteAnimationTag? tag = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        sequence = tag is null
            ? Enumerable.Range(0, document.Frames.Count).ToArray()
            : AnimationSequenceBuilder.Build(tag);
        cursor = Math.Clamp(cursor, 0, Math.Max(0, sequence.Count - 1));
        elapsedMilliseconds = 0;
    }

    public void Play() { if (sequence.Count > 0) IsPlaying = true; }
    public void Pause() => IsPlaying = false;
    public void Toggle() { if (IsPlaying) Pause(); else Play(); }

    public void SeekFrame(int frameIndex)
    {
        int found = sequence.ToList().IndexOf(frameIndex);
        cursor = found >= 0 ? found : Math.Clamp(frameIndex, 0, Math.Max(0, sequence.Count - 1));
        elapsedMilliseconds = 0;
    }

    public int Step(SpriteDocument document, int direction)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (sequence.Count == 0) Configure(document);
        cursor = Math.Clamp(cursor + Math.Sign(direction), 0, Math.Max(0, sequence.Count - 1));
        elapsedMilliseconds = 0;
        return CurrentFrameIndex;
    }

    public bool Advance(SpriteDocument document, double elapsedRealMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsPlaying || sequence.Count == 0 || elapsedRealMilliseconds <= 0) return false;
        elapsedMilliseconds += elapsedRealMilliseconds * Math.Clamp(SpeedMultiplier, .05d, 32d);
        bool changed = false;
        while (true)
        {
            int duration = Math.Max(1, document.Frames[CurrentFrameIndex].DurationMilliseconds);
            if (elapsedMilliseconds < duration) break;
            elapsedMilliseconds -= duration;
            if (cursor + 1 < sequence.Count) cursor++;
            else if (Loop) cursor = 0;
            else { IsPlaying = false; cursor = sequence.Count - 1; elapsedMilliseconds = 0; }
            changed = true;
            if (!IsPlaying) break;
        }
        return changed;
    }
}
