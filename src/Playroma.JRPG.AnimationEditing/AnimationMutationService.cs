using System.Globalization;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.AnimationEditing;

public sealed class AnimationMutationService
{
    private readonly MvEditingSession _session;

    public AnimationMutationService(MvEditingSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public ValueTask SetGeneralAsync(
        string relativePath,
        int animationId,
        string name,
        string sheet1,
        int hue1,
        string sheet2,
        int hue2,
        int position,
        CancellationToken cancellationToken = default)
    {
        JsonPath root = Root(animationId);
        return ExecuteAsync(
            $"Change animation {animationId} general settings",
            [
                new SetJsonStringCommand(relativePath, root.Append("name"), name),
                new SetJsonStringCommand(relativePath, root.Append("animation1Name"), sheet1),
                new SetJsonInt32Command(relativePath, root.Append("animation1Hue"), hue1),
                new SetJsonStringCommand(relativePath, root.Append("animation2Name"), sheet2),
                new SetJsonInt32Command(relativePath, root.Append("animation2Hue"), hue2),
                new SetJsonInt32Command(relativePath, root.Append("position"), position)
            ],
            cancellationToken);
    }

    public ValueTask SetFramesAsync(
        string relativePath,
        int animationId,
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            description ?? $"Change animation {animationId} frames",
            [new SetJsonValueCommand(relativePath, Root(animationId).Append("frames"), MvAnimationSerializer.SerializeFrames(frames))],
            cancellationToken);

    public ValueTask SetTimingsAsync(
        string relativePath,
        int animationId,
        IReadOnlyList<MvAnimationTimingSnapshot> timings,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            description ?? $"Change animation {animationId} timings",
            [new SetJsonValueCommand(relativePath, Root(animationId).Append("timings"), MvAnimationSerializer.SerializeTimings(timings))],
            cancellationToken);


    public ValueTask SetFramesAndTimingsAsync(
        string relativePath,
        int animationId,
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        IReadOnlyList<MvAnimationTimingSnapshot> timings,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            description ?? $"Change animation {animationId} frames and timings",
            [
                new SetJsonValueCommand(relativePath, Root(animationId).Append("frames"), MvAnimationSerializer.SerializeFrames(frames)),
                new SetJsonValueCommand(relativePath, Root(animationId).Append("timings"), MvAnimationSerializer.SerializeTimings(timings))
            ],
            cancellationToken);

    public ValueTask SetCellAsync(
        string relativePath,
        int animationId,
        int frameIndex,
        int laneIndex,
        MvAnimationCellSnapshot cell,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            $"Change animation {animationId} frame {frameIndex + 1} lane {laneIndex + 1}",
            [new SetJsonValueCommand(relativePath, Root(animationId).Append("frames").Append(frameIndex).Append(laneIndex), MvAnimationSerializer.SerializeCell(cell))],
            cancellationToken);

    public ValueTask SetCellsAsync(
        string relativePath,
        int animationId,
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        IReadOnlyCollection<AnimationCellAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        List<IEditorCommand> commands = [];
        foreach (AnimationCellAddress address in addresses.Distinct())
        {
            if (address.FrameIndex < 0 || address.FrameIndex >= frames.Count) continue;
            MvAnimationFrameSnapshot frame = frames[address.FrameIndex];
            if (address.LaneIndex < 0 || address.LaneIndex >= frame.Cells.Count) continue;
            commands.Add(new SetJsonValueCommand(
                relativePath,
                Root(animationId).Append("frames").Append(address.FrameIndex).Append(address.LaneIndex),
                MvAnimationSerializer.SerializeCell(frame.Cells[address.LaneIndex])));
        }
        return ExecuteAsync($"Change {commands.Count} animation cells", commands, cancellationToken);
    }

    public ValueTask SetFrameCountAsync(
        string relativePath,
        int animationId,
        IReadOnlyList<MvAnimationFrameSnapshot> frames,
        CancellationToken cancellationToken = default) =>
        SetFramesAsync(relativePath, animationId, frames, $"Change animation {animationId} frame count", cancellationToken);

    private async ValueTask ExecuteAsync(string description, IReadOnlyList<IEditorCommand> commands, CancellationToken cancellationToken)
    {
        if (commands.Count == 0) return;
        await using ProjectTransaction transaction = _session.BeginTransaction(description);
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonPath Root(int animationId) => JsonPath.Root.Append(animationId > 0 ? animationId : throw new ArgumentOutOfRangeException(nameof(animationId)));
}
