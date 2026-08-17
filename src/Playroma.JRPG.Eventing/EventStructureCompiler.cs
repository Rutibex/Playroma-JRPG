namespace Playroma.JRPG.Eventing;

public sealed class EventStructureCompiler
{
    public IReadOnlyList<RawEventCommand> Compile(EventStructureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<RawEventCommand> result = [];
        foreach (EventNode node in document.Nodes.OrderBy(item => item.OriginalSpan.StartIndex))
        {
            result.AddRange(node.RawCommands);
        }
        return result.Select((command, index) => command.WithRawIndex(index)).ToArray();
    }

    public bool IsLosslessRoundTrip(EventStructureDocument document, out string? reason)
    {
        IReadOnlyList<RawEventCommand> compiled = Compile(document);
        IReadOnlyList<RawEventCommand> original = document.Snapshot.Commands;
        if (compiled.Count != original.Count)
        {
            reason = $"Command count changed from {original.Count} to {compiled.Count}.";
            return false;
        }
        for (int index = 0; index < original.Count; index++)
        {
            if (!string.Equals(original[index].RawJson, compiled[index].RawJson, StringComparison.Ordinal))
            {
                reason = $"Raw command {index} changed during projection round trip.";
                return false;
            }
        }
        reason = null;
        return true;
    }
}

public static class EventRowProjector
{
    public static IReadOnlyList<ProjectedEventRow> Build(EventStructureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<ProjectedEventRow> rows = [];
        AddNodes(document.Nodes, 0, rows);
        return rows.OrderBy(row => row.RawIndex).ToArray();
    }

    private static void AddNodes(
        IReadOnlyList<EventNode> nodes,
        int visualDepth,
        ICollection<ProjectedEventRow> rows)
    {
        foreach (EventNode node in nodes)
        {
            switch (node)
            {
                case EventCommandNode command:
                    rows.Add(new ProjectedEventRow(
                        command.Command.RawIndex,
                        visualDepth,
                        command.Command,
                        command.OriginalSpan,
                        false,
                        false,
                        false));
                    break;
                case EventOpaqueNode opaque:
                    rows.Add(new ProjectedEventRow(
                        opaque.Command.RawIndex,
                        visualDepth,
                        opaque.Command,
                        opaque.OriginalSpan,
                        false,
                        false,
                        false,
                        opaque.Reason));
                    break;
                case EventContinuationGroupNode group:
                    rows.Add(new ProjectedEventRow(
                        group.Opening.RawIndex,
                        visualDepth,
                        group.Opening,
                        group.OriginalSpan,
                        false,
                        false,
                        false));
                    foreach (RawEventCommand continuation in group.Continuations)
                    {
                        rows.Add(new ProjectedEventRow(
                            continuation.RawIndex,
                            visualDepth + 1,
                            continuation,
                            group.OriginalSpan,
                            false,
                            false,
                            true));
                    }
                    break;
                case EventBlockNode block:
                    rows.Add(new ProjectedEventRow(
                        block.Opening.RawIndex,
                        visualDepth,
                        block.Opening,
                        block.OriginalSpan,
                        true,
                        false,
                        false));
                    foreach (EventBranchNode branch in block.Branches)
                    {
                        if (branch.Marker is not null)
                        {
                            rows.Add(new ProjectedEventRow(
                                branch.Marker.RawIndex,
                                visualDepth,
                                branch.Marker,
                                block.OriginalSpan,
                                false,
                                false,
                                true,
                                branch.Name));
                        }
                        AddNodes(branch.Children, visualDepth + 1, rows);
                    }
                    if (block.Closing is not null)
                    {
                        rows.Add(new ProjectedEventRow(
                            block.Closing.RawIndex,
                            visualDepth,
                            block.Closing,
                            block.OriginalSpan,
                            false,
                            true,
                            true));
                    }
                    break;
            }
        }
    }
}
