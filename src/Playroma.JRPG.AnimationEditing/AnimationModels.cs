using System.Text.Json.Nodes;

namespace Playroma.JRPG.AnimationEditing;

public enum MvAnimationPosition
{
    Head = 0,
    Center = 1,
    Feet = 2,
    Screen = 3
}

public enum MvAnimationBlendMode
{
    Normal = 0,
    Additive = 1,
    Multiply = 2,
    Screen = 3
}

public enum MvAnimationFlashScope
{
    None = 0,
    Target = 1,
    Screen = 2,
    HideTarget = 3
}

public enum AnimationInterpolationMode
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Step
}

public enum AnimationRotationMode
{
    Numeric,
    ShortestPath,
    Clockwise,
    CounterClockwise
}

public enum AnimationDiscreteTransition
{
    HoldStart,
    SwitchAtMidpoint,
    SwitchAtEnd
}

public sealed record MvAnimationAudioSnapshot(
    string Name,
    int Volume,
    int Pitch,
    int Pan,
    string RawJson);

public sealed record MvAnimationCellSnapshot(
    int Pattern,
    double X,
    double Y,
    double Scale,
    double Rotation,
    bool Mirror,
    double Opacity,
    int BlendMode,
    string RawJson)
{
    public bool IsVisible => Pattern >= 0 && Opacity > 0 && Scale != 0;
    public bool IsRuntimeLane(int laneIndex) => laneIndex is >= 0 and < MvAnimationConstants.RuntimeCellCount;

    public static MvAnimationCellSnapshot Hidden(string rawJson = "[-1,0,0,100,0,false,255,0]") =>
        new(-1, 0, 0, 100, 0, false, 255, 0, rawJson);
}

public sealed record MvAnimationFrameSnapshot(
    int Index,
    IReadOnlyList<MvAnimationCellSnapshot> Cells,
    string RawJson)
{
    public IReadOnlyList<MvAnimationCellSnapshot> RuntimeCells => Cells.Take(MvAnimationConstants.RuntimeCellCount).ToArray();
    public IReadOnlyList<MvAnimationCellSnapshot> ExtensionCells => Cells.Skip(MvAnimationConstants.RuntimeCellCount).ToArray();
}

public sealed record MvAnimationTimingSnapshot(
    int Index,
    int Frame,
    int FlashScope,
    IReadOnlyList<double> FlashColor,
    int FlashDuration,
    MvAnimationAudioSnapshot? SoundEffect,
    IReadOnlyDictionary<string, string> ExtensionFields,
    string RawJson);

public sealed record MvAnimationSnapshot(
    int Id,
    string Name,
    string Animation1Name,
    int Animation1Hue,
    string Animation2Name,
    int Animation2Hue,
    int Position,
    IReadOnlyList<MvAnimationFrameSnapshot> Frames,
    IReadOnlyList<MvAnimationTimingSnapshot> Timings,
    IReadOnlyDictionary<string, string> ExtensionFields,
    string RawJson)
{
    public int FrameCount => Frames.Count;
    public int TimingCount => Timings.Count;
    public int StoredCellCount => Frames.Sum(frame => frame.Cells.Count);
    public int ActiveCellCount => Frames.Sum(frame => frame.Cells.Count(cell => cell.IsVisible));
    public bool HasExtensionCells => Frames.Any(frame => frame.Cells.Count > MvAnimationConstants.RuntimeCellCount);
}

public sealed record MvAnimationPatternCell(
    int Pattern,
    bool UsesSecondSheet,
    int LocalPattern,
    int SourceX,
    int SourceY,
    int Width,
    int Height);

public sealed record MvAnimationSheetDescriptor(
    int PhysicalWidth,
    int PhysicalHeight,
    int RuntimeColumns,
    int RuntimeRows,
    int CellSize,
    IReadOnlyList<MvAnimationPatternCell> Patterns)
{
    public bool Contains(MvAnimationPatternCell pattern) =>
        pattern.SourceX >= 0 && pattern.SourceY >= 0
        && pattern.SourceX + pattern.Width <= PhysicalWidth
        && pattern.SourceY + pattern.Height <= PhysicalHeight;
}

public readonly record struct AnimationPoint(double X, double Y);
public readonly record struct AnimationSize(double Width, double Height);
public readonly record struct AnimationRectangle(double X, double Y, double Width, double Height);

public sealed record AnimationPreviewTarget(
    double X,
    double Y,
    double Width,
    double Height,
    int LogicalWidth = 816,
    int LogicalHeight = 624,
    bool MirrorAnimation = false);

public sealed record AnimationRenderCell(
    int Lane,
    int Pattern,
    MvAnimationPatternCell Source,
    AnimationPoint Origin,
    double Scale,
    double Rotation,
    bool Mirror,
    double Opacity,
    int BlendMode,
    bool RuntimeVisible,
    bool SourceInBounds);

public sealed record AnimationRenderFrame(
    int FrameIndex,
    AnimationPoint BasePosition,
    IReadOnlyList<AnimationRenderCell> Cells,
    IReadOnlyList<MvAnimationTimingSnapshot> Timings);

public sealed record AnimationCellAddress(int FrameIndex, int LaneIndex);

public sealed record AnimationCellEdit(
    int? Pattern = null,
    double? X = null,
    double? Y = null,
    double? Scale = null,
    double? Rotation = null,
    bool? Mirror = null,
    double? Opacity = null,
    int? BlendMode = null)
{
    public MvAnimationCellSnapshot Apply(MvAnimationCellSnapshot source) => source with
    {
        Pattern = Pattern ?? source.Pattern,
        X = X ?? source.X,
        Y = Y ?? source.Y,
        Scale = Scale ?? source.Scale,
        Rotation = Rotation ?? source.Rotation,
        Mirror = Mirror ?? source.Mirror,
        Opacity = Opacity ?? source.Opacity,
        BlendMode = BlendMode ?? source.BlendMode
    };
}

public sealed record AnimationTweenRequest(
    int StartFrame,
    int EndFrame,
    IReadOnlySet<int> Lanes,
    bool TweenX = true,
    bool TweenY = true,
    bool TweenScale = true,
    bool TweenRotation = true,
    bool TweenOpacity = true,
    AnimationInterpolationMode Interpolation = AnimationInterpolationMode.Linear,
    AnimationRotationMode RotationMode = AnimationRotationMode.ShortestPath,
    AnimationDiscreteTransition PatternTransition = AnimationDiscreteTransition.HoldStart,
    AnimationDiscreteTransition MirrorTransition = AnimationDiscreteTransition.HoldStart,
    AnimationDiscreteTransition BlendTransition = AnimationDiscreteTransition.HoldStart,
    bool CreateMissingLanes = false,
    bool OverwriteIntermediate = true,
    int AdditionalRotations = 0);

public sealed record AnimationBatchEdit(
    double OffsetX = 0,
    double OffsetY = 0,
    double ScaleMultiplier = 1,
    double RotationOffset = 0,
    double? SetOpacity = null,
    bool ToggleMirror = false,
    int? ReplacePattern = null,
    int? SetBlendMode = null);

public sealed record AnimationClipboardEnvelope(
    string Format,
    int Version,
    string SourceProject,
    int SourceAnimation,
    string Sheet1,
    string Sheet2,
    IReadOnlyList<MvAnimationFrameSnapshot> Frames,
    IReadOnlyList<MvAnimationTimingSnapshot> Timings)
{
    public const string CurrentFormat = "playroma.mv.animation-frames";
    public const int CurrentVersion = 1;
}

public sealed record AnimationDependencyReport(
    IReadOnlyList<string> MissingSheets,
    IReadOnlyList<string> MissingSoundEffects,
    IReadOnlyList<int> OutOfRangePatterns)
{
    public bool HasProblems => MissingSheets.Count > 0 || MissingSoundEffects.Count > 0 || OutOfRangePatterns.Count > 0;
}

public static class MvAnimationConstants
{
    public const int CellSize = 192;
    public const int RuntimeColumns = 5;
    public const int RuntimeCellCount = 16;
    public const int FirstSheetPatternMinimum = 0;
    public const int FirstSheetPatternMaximum = 99;
    public const int SecondSheetPatternMinimum = 100;
    public const int SecondSheetPatternMaximum = 199;
    public const int RuntimeTicksPerAnimationFrame = 4;
    public const int RuntimeFramesPerSecond = 15;

    public static readonly JsonArray HiddenCell = [-1, 0, 0, 100, 0, false, 255, 0];
}
