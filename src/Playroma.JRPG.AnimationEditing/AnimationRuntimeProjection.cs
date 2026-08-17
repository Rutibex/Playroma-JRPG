namespace Playroma.JRPG.AnimationEditing;

public static class MvAnimationSheetMapper
{
    public static MvAnimationPatternCell Map(int pattern)
    {
        bool second = pattern >= MvAnimationConstants.SecondSheetPatternMinimum;
        int local = pattern % 100;
        if (local < 0) local += 100;
        return new MvAnimationPatternCell(
            pattern,
            second,
            local,
            local % MvAnimationConstants.RuntimeColumns * MvAnimationConstants.CellSize,
            local / MvAnimationConstants.RuntimeColumns * MvAnimationConstants.CellSize,
            MvAnimationConstants.CellSize,
            MvAnimationConstants.CellSize);
    }

    public static MvAnimationSheetDescriptor Describe(int physicalWidth, int physicalHeight, bool secondSheet)
    {
        int minimum = secondSheet ? 100 : 0;
        MvAnimationPatternCell[] patterns = Enumerable.Range(minimum, 100).Select(Map).ToArray();
        return new MvAnimationSheetDescriptor(
            Math.Max(0, physicalWidth),
            Math.Max(0, physicalHeight),
            MvAnimationConstants.RuntimeColumns,
            100 / MvAnimationConstants.RuntimeColumns,
            MvAnimationConstants.CellSize,
            patterns);
    }

    public static bool IsStandardPattern(int pattern) => pattern is >= 0 and <= 199;
}

public static class MvAnimationPositionCalculator
{
    public static AnimationPoint Calculate(int position, AnimationPreviewTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return position switch
        {
            (int)MvAnimationPosition.Head => new AnimationPoint(target.X + target.Width / 2d, target.Y),
            (int)MvAnimationPosition.Center => new AnimationPoint(target.X + target.Width / 2d, target.Y + target.Height / 2d),
            (int)MvAnimationPosition.Feet => new AnimationPoint(target.X + target.Width / 2d, target.Y + target.Height),
            (int)MvAnimationPosition.Screen => new AnimationPoint(target.LogicalWidth / 2d, target.LogicalHeight / 2d),
            _ => new AnimationPoint(target.X + target.Width / 2d, target.Y + target.Height / 2d)
        };
    }
}

public static class MvAnimationFrameProjector
{
    public static AnimationRenderFrame Project(
        MvAnimationSnapshot animation,
        int frameIndex,
        AnimationPreviewTarget target,
        int firstSheetWidth,
        int firstSheetHeight,
        int secondSheetWidth,
        int secondSheetHeight)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(target);
        if (animation.Frames.Count == 0) return new AnimationRenderFrame(0, MvAnimationPositionCalculator.Calculate(animation.Position, target), [], []);
        frameIndex = Math.Clamp(frameIndex, 0, animation.Frames.Count - 1);
        MvAnimationFrameSnapshot frame = animation.Frames[frameIndex];
        AnimationPoint basePosition = MvAnimationPositionCalculator.Calculate(animation.Position, target);
        List<AnimationRenderCell> cells = [];
        for (int lane = 0; lane < frame.Cells.Count; lane++)
        {
            MvAnimationCellSnapshot cell = frame.Cells[lane];
            if (!cell.IsVisible) continue;
            MvAnimationPatternCell source = MvAnimationSheetMapper.Map(cell.Pattern);
            int width = source.UsesSecondSheet ? secondSheetWidth : firstSheetWidth;
            int height = source.UsesSecondSheet ? secondSheetHeight : firstSheetHeight;
            bool inBounds = source.SourceX >= 0 && source.SourceY >= 0 && source.SourceX + source.Width <= width && source.SourceY + source.Height <= height;
            double x = target.MirrorAnimation ? basePosition.X - cell.X : basePosition.X + cell.X;
            bool mirror = target.MirrorAnimation ? !cell.Mirror : cell.Mirror;
            cells.Add(new AnimationRenderCell(
                lane,
                cell.Pattern,
                source,
                new AnimationPoint(x, basePosition.Y + cell.Y),
                cell.Scale / 100d,
                cell.Rotation,
                mirror,
                Math.Clamp(cell.Opacity / 255d, 0d, 1d),
                cell.BlendMode,
                lane < MvAnimationConstants.RuntimeCellCount,
                inBounds));
        }

        MvAnimationTimingSnapshot[] timings = animation.Timings.Where(timing => timing.Frame == frameIndex).ToArray();
        return new AnimationRenderFrame(frameIndex, basePosition, cells, timings);
    }
}

public static class AnimationPlaybackClock
{
    public static TimeSpan FrameDuration(double speed = 1d)
    {
        speed = speed <= 0 ? 1d : speed;
        return TimeSpan.FromSeconds(1d / (MvAnimationConstants.RuntimeFramesPerSecond * speed));
    }

    public static int FrameAt(TimeSpan elapsed, int frameCount, bool loop, double speed = 1d)
    {
        if (frameCount <= 0) return 0;
        long frame = (long)Math.Floor(elapsed.TotalSeconds * MvAnimationConstants.RuntimeFramesPerSecond * Math.Max(0.01, speed));
        if (loop) return (int)(frame % frameCount);
        return (int)Math.Min(frameCount - 1, frame);
    }
}
