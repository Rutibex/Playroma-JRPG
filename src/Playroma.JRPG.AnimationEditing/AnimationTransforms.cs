namespace Playroma.JRPG.AnimationEditing;

public static class AnimationTweenEngine
{
    public static IReadOnlyList<MvAnimationFrameSnapshot> Apply(
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        AnimationTweenRequest request)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(request);
        if (frames.Count == 0) return [];
        int start = Math.Clamp(request.StartFrame, 0, frames.Count - 1);
        int end = Math.Clamp(request.EndFrame, 0, frames.Count - 1);
        if (end <= start) throw new ArgumentException("The tween end frame must be after the start frame.", nameof(request));
        HashSet<int> lanes = request.Lanes.Count == 0
            ? Enumerable.Range(0, Math.Max(frames[start].Cells.Count, frames[end].Cells.Count)).ToHashSet()
            : request.Lanes.ToHashSet();
        List<MvAnimationFrameSnapshot> result = frames.Select(CloneFrame).ToList();
        foreach (int lane in lanes.Order())
        {
            MvAnimationCellSnapshot? startCell = TryGet(frames[start], lane);
            MvAnimationCellSnapshot? endCell = TryGet(frames[end], lane);
            if (startCell is null && endCell is null) continue;
            if (startCell is null || endCell is null)
            {
                if (!request.CreateMissingLanes) continue;
                startCell ??= endCell! with { Opacity = 0 };
                endCell ??= startCell with { Opacity = 0 };
            }

            for (int frameIndex = start + 1; frameIndex < end; frameIndex++)
            {
                double rawT = (frameIndex - start) / (double)(end - start);
                double t = Ease(rawT, request.Interpolation);
                MvAnimationCellSnapshot interpolated = Interpolate(startCell, endCell, t, rawT, request);
                MvAnimationFrameSnapshot frame = result[frameIndex];
                if (!request.OverwriteIntermediate && TryGet(frame, lane)?.IsVisible == true) continue;
                result[frameIndex] = SetCell(frame, lane, interpolated);
            }
        }
        return result;
    }

    public static double Ease(double t, AnimationInterpolationMode mode)
    {
        t = Math.Clamp(t, 0d, 1d);
        return mode switch
        {
            AnimationInterpolationMode.Linear => t,
            AnimationInterpolationMode.EaseIn => t * t,
            AnimationInterpolationMode.EaseOut => 1d - (1d - t) * (1d - t),
            AnimationInterpolationMode.EaseInOut => t < 0.5 ? 2d * t * t : 1d - Math.Pow(-2d * t + 2d, 2d) / 2d,
            AnimationInterpolationMode.Step => t < 1d ? 0d : 1d,
            _ => t
        };
    }

    public static double InterpolateRotation(double start, double end, double t, AnimationRotationMode mode, int additionalRotations = 0)
    {
        double delta = end - start;
        switch (mode)
        {
            case AnimationRotationMode.ShortestPath:
                delta = (delta + 180d) % 360d;
                if (delta < 0) delta += 360d;
                delta -= 180d;
                break;
            case AnimationRotationMode.Clockwise:
                while (delta < 0) delta += 360d;
                delta += Math.Max(0, additionalRotations) * 360d;
                break;
            case AnimationRotationMode.CounterClockwise:
                while (delta > 0) delta -= 360d;
                delta -= Math.Max(0, additionalRotations) * 360d;
                break;
            case AnimationRotationMode.Numeric:
                delta += additionalRotations * 360d;
                break;
        }
        return start + delta * t;
    }

    private static MvAnimationCellSnapshot Interpolate(
        MvAnimationCellSnapshot start,
        MvAnimationCellSnapshot end,
        double t,
        double rawT,
        AnimationTweenRequest request)
    {
        return start with
        {
            Pattern = SelectDiscrete(start.Pattern, end.Pattern, rawT, request.PatternTransition),
            X = request.TweenX ? Lerp(start.X, end.X, t) : start.X,
            Y = request.TweenY ? Lerp(start.Y, end.Y, t) : start.Y,
            Scale = request.TweenScale ? Lerp(start.Scale, end.Scale, t) : start.Scale,
            Rotation = request.TweenRotation ? InterpolateRotation(start.Rotation, end.Rotation, t, request.RotationMode, request.AdditionalRotations) : start.Rotation,
            Mirror = SelectDiscrete(start.Mirror, end.Mirror, rawT, request.MirrorTransition),
            Opacity = request.TweenOpacity ? Lerp(start.Opacity, end.Opacity, t) : start.Opacity,
            BlendMode = SelectDiscrete(start.BlendMode, end.BlendMode, rawT, request.BlendTransition)
        };
    }

    private static int SelectDiscrete(int start, int end, double t, AnimationDiscreteTransition mode) =>
        mode switch
        {
            AnimationDiscreteTransition.SwitchAtMidpoint when t >= 0.5 => end,
            AnimationDiscreteTransition.SwitchAtEnd when t >= 1d => end,
            _ => start
        };

    private static bool SelectDiscrete(bool start, bool end, double t, AnimationDiscreteTransition mode) =>
        mode switch
        {
            AnimationDiscreteTransition.SwitchAtMidpoint when t >= 0.5 => end,
            AnimationDiscreteTransition.SwitchAtEnd when t >= 1d => end,
            _ => start
        };

    private static double Lerp(double start, double end, double t) => start + (end - start) * t;

    private static MvAnimationCellSnapshot? TryGet(MvAnimationFrameSnapshot frame, int lane) =>
        lane >= 0 && lane < frame.Cells.Count ? frame.Cells[lane] : null;

    public static MvAnimationFrameSnapshot SetCell(MvAnimationFrameSnapshot frame, int lane, MvAnimationCellSnapshot cell)
    {
        List<MvAnimationCellSnapshot> cells = frame.Cells.ToList();
        while (cells.Count <= lane) cells.Add(MvAnimationCellSnapshot.Hidden());
        cells[lane] = cell;
        return frame with { Cells = cells };
    }

    private static MvAnimationFrameSnapshot CloneFrame(MvAnimationFrameSnapshot frame) => frame with { Cells = frame.Cells.ToArray() };
}

public static class AnimationBatchEditor
{
    public static IReadOnlyList<MvAnimationFrameSnapshot> Apply(
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        IReadOnlyCollection<AnimationCellAddress> addresses,
        AnimationBatchEdit edit)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(edit);
        List<MvAnimationFrameSnapshot> result = frames.Select(frame => frame with { Cells = frame.Cells.ToArray() }).ToList();
        foreach (AnimationCellAddress address in addresses.Distinct())
        {
            if (address.FrameIndex < 0 || address.FrameIndex >= result.Count || address.LaneIndex < 0) continue;
            MvAnimationFrameSnapshot frame = result[address.FrameIndex];
            MvAnimationCellSnapshot? source = address.LaneIndex < frame.Cells.Count ? frame.Cells[address.LaneIndex] : null;
            if (source is null) continue;
            MvAnimationCellSnapshot changed = source with
            {
                Pattern = edit.ReplacePattern ?? source.Pattern,
                X = source.X + edit.OffsetX,
                Y = source.Y + edit.OffsetY,
                Scale = source.Scale * edit.ScaleMultiplier,
                Rotation = source.Rotation + edit.RotationOffset,
                Mirror = edit.ToggleMirror ? !source.Mirror : source.Mirror,
                Opacity = edit.SetOpacity ?? source.Opacity,
                BlendMode = edit.SetBlendMode ?? source.BlendMode
            };
            result[address.FrameIndex] = AnimationTweenEngine.SetCell(frame, address.LaneIndex, changed);
        }
        return result;
    }
}

public static class AnimationFrameOperations
{
    public static IReadOnlyList<MvAnimationFrameSnapshot> Insert(IReadOnlyList<MvAnimationFrameSnapshot> frames, int index, MvAnimationFrameSnapshot? source = null)
    {
        List<MvAnimationFrameSnapshot> result = frames.ToList();
        index = Math.Clamp(index, 0, result.Count);
        MvAnimationFrameSnapshot frame = source is null
            ? new MvAnimationFrameSnapshot(index, [], "[]")
            : source with { Index = index, Cells = source.Cells.ToArray() };
        result.Insert(index, frame);
        return Reindex(result);
    }

    public static IReadOnlyList<MvAnimationFrameSnapshot> Delete(IReadOnlyList<MvAnimationFrameSnapshot> frames, IReadOnlyCollection<int> indexes)
    {
        HashSet<int> delete = indexes.Where(index => index >= 0 && index < frames.Count).ToHashSet();
        return Reindex(frames.Where((_, index) => !delete.Contains(index)).ToList());
    }

    public static IReadOnlyList<MvAnimationFrameSnapshot> Move(IReadOnlyList<MvAnimationFrameSnapshot> frames, IReadOnlyCollection<int> indexes, int destination)
    {
        HashSet<int> selected = indexes.Where(index => index >= 0 && index < frames.Count).ToHashSet();
        if (selected.Count == 0) return frames.ToArray();
        List<MvAnimationFrameSnapshot> moving = frames.Where((_, index) => selected.Contains(index)).ToList();
        List<MvAnimationFrameSnapshot> remaining = frames.Where((_, index) => !selected.Contains(index)).ToList();
        int removedBefore = selected.Count(index => index < destination);
        destination = Math.Clamp(destination - removedBefore, 0, remaining.Count);
        remaining.InsertRange(destination, moving);
        return Reindex(remaining);
    }

    public static IReadOnlyList<MvAnimationFrameSnapshot> Reverse(IReadOnlyList<MvAnimationFrameSnapshot> frames, int start, int end)
    {
        List<MvAnimationFrameSnapshot> result = frames.ToList();
        if (result.Count == 0) return result;
        start = Math.Clamp(start, 0, result.Count - 1);
        end = Math.Clamp(end, start, result.Count - 1);
        result.Reverse(start, end - start + 1);
        return Reindex(result);
    }

    public static IReadOnlyList<MvAnimationFrameSnapshot> Reindex(IReadOnlyList<MvAnimationFrameSnapshot> frames) =>
        frames.Select((frame, index) => frame with { Index = index }).ToArray();
}
