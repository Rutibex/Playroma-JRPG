using System.Security.Cryptography;
using System.Text;

namespace Playroma.JRPG.Eventing;

public sealed class EventStructureProjector
{
    private static readonly IReadOnlyDictionary<int, int> ContinuationCodes = new Dictionary<int, int>
    {
        [101] = 401,
        [105] = 405,
        [108] = 408,
        [205] = 505,
        [302] = 605,
        [355] = 655
    };

    private static readonly HashSet<int> AllContinuationCodes =
    [401, 402, 403, 404, 405, 408, 411, 412, 413, 505, 601, 602, 603, 604, 605, 655];

    public EventStructureDocument Project(EventListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Parser parser = new(snapshot.Commands);
        IReadOnlyList<EventNode> nodes = parser.ParseSequence(0, new HashSet<int>());
        while (!parser.AtEnd)
        {
            RawEventCommand command = parser.Current;
            parser.AddDiagnostic(
                "PJRPG10001",
                EventDiagnosticSeverity.Error,
                $"Command {command.RawIndex} could not be attached to the structural projection.",
                command.RawIndex);
            parser.Advance();
        }
        return new EventStructureDocument(snapshot, nodes, parser.Diagnostics.ToArray());
    }

    public static string Fingerprint(IEnumerable<RawEventCommand> commands)
    {
        string value = string.Join("\n", commands.Select(command => command.RawJson));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<RawEventCommand> _commands;
        private int _index;

        public Parser(IReadOnlyList<RawEventCommand> commands)
        {
            _commands = commands;
        }

        public List<EventStructureDiagnostic> Diagnostics { get; } = [];
        public bool AtEnd => _index >= _commands.Count;
        public RawEventCommand Current => _commands[_index];

        public IReadOnlyList<EventNode> ParseSequence(int expectedIndent, IReadOnlySet<int> stopCodes)
        {
            List<EventNode> nodes = [];
            while (!AtEnd)
            {
                RawEventCommand command = Current;
                if (command.Indent < expectedIndent) break;
                if (command.Indent == expectedIndent && stopCodes.Contains(command.Code)) break;

                if (command.Indent > expectedIndent)
                {
                    AddDiagnostic(
                        "PJRPG10002",
                        EventDiagnosticSeverity.Error,
                        $"Command {command.RawIndex} jumps to indentation {command.Indent}; expected {expectedIndent}.",
                        command.RawIndex);
                    nodes.Add(new EventOpaqueNode(command, "Unexpected indentation"));
                    Advance();
                    continue;
                }

                if (AllContinuationCodes.Contains(command.Code))
                {
                    AddDiagnostic(
                        "PJRPG10003",
                        EventDiagnosticSeverity.Error,
                        $"Continuation command {command.Code} at row {command.RawIndex} has no compatible opening command.",
                        command.RawIndex);
                    nodes.Add(new EventOpaqueNode(command, "Orphaned continuation"));
                    Advance();
                    continue;
                }

                nodes.Add(command.Code switch
                {
                    111 => ParseConditional(),
                    112 => ParseLoop(),
                    102 => ParseChoices(),
                    301 => ParseBattle(),
                    _ when ContinuationCodes.ContainsKey(command.Code) => ParseContinuationGroup(),
                    _ => ParseSingle()
                });
            }
            return nodes;
        }

        public void Advance() => _index++;

        public void AddDiagnostic(
            string code,
            EventDiagnosticSeverity severity,
            string message,
            int? rawIndex = null,
            EventCommandSpan? span = null) =>
            Diagnostics.Add(new EventStructureDiagnostic(code, severity, message, rawIndex, span));

        private EventNode ParseSingle()
        {
            RawEventCommand command = Current;
            Advance();
            return new EventCommandNode(command);
        }

        private EventNode ParseContinuationGroup()
        {
            RawEventCommand opening = Current;
            Advance();
            int expectedCode = ContinuationCodes[opening.Code];
            List<RawEventCommand> continuations = [];
            while (!AtEnd && Current.Code == expectedCode && Current.Indent == opening.Indent)
            {
                continuations.Add(Current);
                Advance();
            }
            return new EventContinuationGroupNode(opening, continuations);
        }

        private EventNode ParseConditional()
        {
            int start = _index;
            RawEventCommand opening = Current;
            Advance();
            IReadOnlyList<EventNode> trueChildren = ParseSequence(opening.Indent + 1, new HashSet<int> { 411, 412 });
            List<EventBranchNode> branches = [new EventBranchNode("True", null, trueChildren)];
            if (!AtEnd && Current.Code == 411 && Current.Indent == opening.Indent)
            {
                RawEventCommand marker = Current;
                Advance();
                IReadOnlyList<EventNode> falseChildren = ParseSequence(opening.Indent + 1, new HashSet<int> { 412 });
                branches.Add(new EventBranchNode("Else", marker, falseChildren));
            }
            RawEventCommand? closing = ConsumeClosing(412, opening, "Conditional Branch");
            return CreateBlock(EventBlockKind.Conditional, opening, branches, closing, start);
        }

        private EventNode ParseLoop()
        {
            int start = _index;
            RawEventCommand opening = Current;
            Advance();
            IReadOnlyList<EventNode> children = ParseSequence(opening.Indent + 1, new HashSet<int> { 413 });
            RawEventCommand? closing = ConsumeClosing(413, opening, "Loop");
            return CreateBlock(
                EventBlockKind.Loop,
                opening,
                [new EventBranchNode("Body", null, children)],
                closing,
                start);
        }

        private EventNode ParseChoices()
        {
            int start = _index;
            RawEventCommand opening = Current;
            Advance();
            List<EventBranchNode> branches = [];
            while (!AtEnd && Current.Indent == opening.Indent && Current.Code is 402 or 403)
            {
                RawEventCommand marker = Current;
                Advance();
                string name = marker.Code == 403 ? "Cancel" : ChoiceLabel(marker);
                IReadOnlyList<EventNode> children = ParseSequence(opening.Indent + 1, new HashSet<int> { 402, 403, 404 });
                branches.Add(new EventBranchNode(name, marker, children));
            }
            if (branches.Count == 0)
            {
                AddDiagnostic(
                    "PJRPG10004",
                    EventDiagnosticSeverity.Warning,
                    $"Show Choices at row {opening.RawIndex} has no branch markers.",
                    opening.RawIndex);
            }
            RawEventCommand? closing = ConsumeClosing(404, opening, "Show Choices");
            return CreateBlock(EventBlockKind.Choices, opening, branches, closing, start);
        }

        private EventNode ParseBattle()
        {
            int start = _index;
            RawEventCommand opening = Current;
            Advance();
            List<EventBranchNode> branches = [];
            while (!AtEnd && Current.Indent == opening.Indent && Current.Code is 601 or 602 or 603)
            {
                RawEventCommand marker = Current;
                Advance();
                string name = marker.Code switch { 601 => "Win", 602 => "Escape", 603 => "Lose", _ => "Branch" };
                IReadOnlyList<EventNode> children = ParseSequence(opening.Indent + 1, new HashSet<int> { 601, 602, 603, 604 });
                branches.Add(new EventBranchNode(name, marker, children));
            }
            RawEventCommand? closing = ConsumeClosing(604, opening, "Battle Processing");
            return CreateBlock(EventBlockKind.Battle, opening, branches, closing, start);
        }

        private RawEventCommand? ConsumeClosing(int code, RawEventCommand opening, string name)
        {
            if (!AtEnd && Current.Code == code && Current.Indent == opening.Indent)
            {
                RawEventCommand closing = Current;
                Advance();
                return closing;
            }
            AddDiagnostic(
                "PJRPG10005",
                EventDiagnosticSeverity.Error,
                $"{name} at row {opening.RawIndex} has no closing command {code}.",
                opening.RawIndex);
            return null;
        }

        private EventBlockNode CreateBlock(
            EventBlockKind kind,
            RawEventCommand opening,
            IReadOnlyList<EventBranchNode> branches,
            RawEventCommand? closing,
            int start)
        {
            int end = _index;
            RawEventCommand[] raw = _commands.Skip(start).Take(end - start).ToArray();
            return new EventBlockNode(
                kind,
                opening,
                branches,
                closing,
                EventCommandSpan.FromBounds(start, end),
                raw);
        }

        private static string ChoiceLabel(RawEventCommand marker)
        {
            System.Text.Json.JsonElement p = JsonParameterReader.ParseArray(marker.ParametersRawJson);
            string text = JsonParameterReader.GetString(p, 1);
            return string.IsNullOrWhiteSpace(text) ? $"Choice {JsonParameterReader.GetInt(p, 0) + 1}" : text;
        }
    }
}
