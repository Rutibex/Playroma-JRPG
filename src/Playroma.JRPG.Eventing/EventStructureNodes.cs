namespace Playroma.JRPG.Eventing;

public enum EventBlockKind
{
    Conditional,
    Loop,
    Choices,
    Battle
}

public abstract class EventNode
{
    protected EventNode(
        EventCommandSpan originalSpan,
        int originalIndent,
        string rawFingerprint,
        IReadOnlyList<RawEventCommand> rawCommands)
    {
        OriginalSpan = originalSpan;
        OriginalIndent = originalIndent;
        RawFingerprint = rawFingerprint;
        RawCommands = rawCommands ?? throw new ArgumentNullException(nameof(rawCommands));
    }

    public EventCommandSpan OriginalSpan { get; }
    public int OriginalIndent { get; }
    public string RawFingerprint { get; }
    public IReadOnlyList<RawEventCommand> RawCommands { get; }
    public virtual IReadOnlyList<EventNode> Children => [];
}

public sealed class EventCommandNode : EventNode
{
    public EventCommandNode(RawEventCommand command)
        : base(new EventCommandSpan(command.RawIndex, 1), command.Indent, command.Fingerprint, [command])
    {
        Command = command;
    }

    public RawEventCommand Command { get; }
}

public sealed class EventContinuationGroupNode : EventNode
{
    public EventContinuationGroupNode(
        RawEventCommand opening,
        IReadOnlyList<RawEventCommand> continuations)
        : base(
            new EventCommandSpan(opening.RawIndex, 1 + continuations.Count),
            opening.Indent,
            EventStructureProjector.Fingerprint([opening, .. continuations]),
            [opening, .. continuations])
    {
        Opening = opening;
        Continuations = continuations;
    }

    public RawEventCommand Opening { get; }
    public IReadOnlyList<RawEventCommand> Continuations { get; }
}

public sealed class EventBranchNode
{
    public EventBranchNode(
        string name,
        RawEventCommand? marker,
        IReadOnlyList<EventNode> children)
    {
        Name = name;
        Marker = marker;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    public string Name { get; }
    public RawEventCommand? Marker { get; }
    public IReadOnlyList<EventNode> Children { get; }
}

public sealed class EventBlockNode : EventNode
{
    public EventBlockNode(
        EventBlockKind kind,
        RawEventCommand opening,
        IReadOnlyList<EventBranchNode> branches,
        RawEventCommand? closing,
        EventCommandSpan span,
        IReadOnlyList<RawEventCommand> rawCommands)
        : base(span, opening.Indent, EventStructureProjector.Fingerprint(rawCommands), rawCommands)
    {
        Kind = kind;
        Opening = opening;
        Branches = branches;
        Closing = closing;
    }

    public EventBlockKind Kind { get; }
    public RawEventCommand Opening { get; }
    public IReadOnlyList<EventBranchNode> Branches { get; }
    public RawEventCommand? Closing { get; }
    public override IReadOnlyList<EventNode> Children => Branches.SelectMany(branch => branch.Children).ToArray();
}

public sealed class EventOpaqueNode : EventNode
{
    public EventOpaqueNode(RawEventCommand command, string reason)
        : base(new EventCommandSpan(command.RawIndex, 1), command.Indent, command.Fingerprint, [command])
    {
        Command = command;
        Reason = reason;
    }

    public RawEventCommand Command { get; }
    public string Reason { get; }
}

public sealed record EventStructureDocument(
    EventListSnapshot Snapshot,
    IReadOnlyList<EventNode> Nodes,
    IReadOnlyList<EventStructureDiagnostic> Diagnostics)
{
    public bool IsStructurallyValid => Diagnostics.All(item => item.Severity != EventDiagnosticSeverity.Error);

    public EventNode? FindNodeContaining(int rawIndex) => Find(Nodes, rawIndex);

    private static EventNode? Find(IEnumerable<EventNode> nodes, int rawIndex)
    {
        foreach (EventNode node in nodes)
        {
            if (!node.OriginalSpan.Contains(rawIndex)) continue;
            if (node is EventBlockNode block)
            {
                foreach (EventBranchNode branch in block.Branches)
                {
                    EventNode? nested = Find(branch.Children, rawIndex);
                    if (nested is not null) return nested;
                }
            }
            return node;
        }
        return null;
    }
}

public sealed record ProjectedEventRow(
    int RawIndex,
    int VisualDepth,
    RawEventCommand Command,
    EventCommandSpan StructuralSpan,
    bool IsBlockOpening,
    bool IsBlockClosing,
    bool IsContinuation,
    string? BranchLabel = null);
