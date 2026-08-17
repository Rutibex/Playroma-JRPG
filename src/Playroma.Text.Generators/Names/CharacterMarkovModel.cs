using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Names;

public sealed class CharacterMarkovModel
{
    private const char Start = '\u0002';
    private const char End = '\u0003';
    private readonly Dictionary<string, Dictionary<char, int>> _transitions;
    public int Order { get; }
    public IReadOnlyList<string> Corpus { get; }

    private CharacterMarkovModel(int order, IReadOnlyList<string> corpus, Dictionary<string, Dictionary<char, int>> transitions)
    { Order = order; Corpus = corpus; _transitions = transitions; }

    public static CharacterMarkovModel Train(IEnumerable<string> corpus, int order = 3)
    {
        if (order is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(order));
        var cleaned = corpus.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (cleaned.Length == 0) throw new ArgumentException("Markov corpus cannot be empty.", nameof(corpus));
        var transitions = new Dictionary<string, Dictionary<char, int>>(StringComparer.Ordinal);
        foreach (var item in cleaned)
        {
            var normalized = item.ToLowerInvariant();
            var padded = new string(Start, order) + normalized + End;
            for (var i = order; i < padded.Length; i++)
            {
                for (var contextLength = 0; contextLength <= order; contextLength++)
                {
                    var context = contextLength == 0 ? string.Empty : padded.Substring(i - contextLength, contextLength);
                    if (!transitions.TryGetValue(context, out var counts)) transitions[context] = counts = new Dictionary<char, int>();
                    counts[padded[i]] = counts.TryGetValue(padded[i], out var count) ? count + 1 : 1;
                }
            }
        }
        return new CharacterMarkovModel(order, cleaned, transitions);
    }

    public string Generate(IDeterministicRandom random, int minLength, int maxLength, int maxAttempts = 12)
    {
        if (minLength < 1 || maxLength < minLength) throw new ArgumentOutOfRangeException(nameof(minLength));
        string best = string.Empty;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var output = new List<char>(maxLength);
            var context = new string(Start, Order);
            for (var step = 0; step < maxLength + Order + 8; step++)
            {
                var next = PickNext(context, random);
                if (next == End)
                {
                    if (output.Count >= minLength) break;
                    output.Clear();
                    context = new string(Start, Order);
                    continue;
                }
                if (next == Start) continue;
                output.Add(next);
                if (output.Count >= maxLength) break;
                context = (context + next);
                if (context.Length > Order) context = context[^Order..];
            }
            var candidate = new string(output.ToArray()).Trim();
            if (candidate.Length > best.Length) best = candidate;
            if (candidate.Length >= minLength && candidate.Length <= maxLength) return candidate;
        }
        return best.Length > maxLength ? best[..maxLength] : best;
    }

    private char PickNext(string context, IDeterministicRandom random)
    {
        for (var length = Math.Min(Order, context.Length); length >= 0; length--)
        {
            var key = length == 0 ? string.Empty : context[^length..];
            if (!_transitions.TryGetValue(key, out var counts) || counts.Count == 0) continue;
            var total = counts.Values.Sum();
            var roll = random.NextInt(total);
            foreach (var pair in counts.OrderBy(x => x.Key))
            {
                if (roll < pair.Value) return pair.Key;
                roll -= pair.Value;
            }
        }
        return End;
    }
}
