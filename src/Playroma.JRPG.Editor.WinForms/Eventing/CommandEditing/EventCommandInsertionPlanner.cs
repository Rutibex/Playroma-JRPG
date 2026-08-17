using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;

/// <summary>
/// Resolves structural insertion points for block arms such as conditional Else branches,
/// choice options, loop bodies, and Battle Processing Win/Escape/Lose branches.
/// </summary>
public static class EventCommandInsertionPlanner
{
    public static bool TryResolveInsideBranch(
        IReadOnlyList<EventNode> nodes,
        int selectedRawIndex,
        int endIndex,
        out EventCommandInsertionTarget target)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentOutOfRangeException.ThrowIfNegative(selectedRawIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(endIndex);

        foreach (EventNode node in nodes)
        {
            if (node is not EventBlockNode block) continue;

            // Selecting the block opener means its first arm/body.
            if (block.Opening.RawIndex == selectedRawIndex && block.Branches.FirstOrDefault() is { } first)
            {
                target = BranchStart(block, first, endIndex);
                return true;
            }

            foreach (EventBranchNode branch in block.Branches)
            {
                // Marker rows are Win/Escape/Lose, choice labels, and Else.  The first
                // child command belongs immediately after the marker at child indent.
                if (branch.Marker?.RawIndex == selectedRawIndex)
                {
                    target = BranchStart(block, branch, endIndex);
                    return true;
                }

                if (!branch.Children.Any(child => child.OriginalSpan.Contains(selectedRawIndex))) continue;

                // Prefer a nested block arm when the selection is inside one.
                if (TryResolveInsideBranch(branch.Children, selectedRawIndex, endIndex, out target))
                {
                    return true;
                }

                // Otherwise append to the containing arm before its next marker or
                // closing command.  EndExclusive already points at that boundary.
                int index = branch.Children.Count == 0
                    ? BranchStartIndex(block, branch)
                    : branch.Children.Max(child => child.OriginalSpan.EndExclusive);
                target = new EventCommandInsertionTarget(
                    Math.Min(index, endIndex),
                    Math.Max(0, block.OriginalIndent + 1),
                    EventCommandInsertionMode.InsideSelectedBranch);
                return true;
            }
        }

        target = default!;
        return false;
    }

    private static EventCommandInsertionTarget BranchStart(
        EventBlockNode block,
        EventBranchNode branch,
        int endIndex) => new(
            Math.Min(BranchStartIndex(block, branch), endIndex),
            Math.Max(0, block.OriginalIndent + 1),
            EventCommandInsertionMode.InsideSelectedBranch);

    private static int BranchStartIndex(EventBlockNode block, EventBranchNode branch) =>
        branch.Marker is not null ? branch.Marker.RawIndex + 1 : block.Opening.RawIndex + 1;
}
