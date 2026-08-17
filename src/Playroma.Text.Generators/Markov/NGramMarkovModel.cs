using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Markov;

public sealed record MarkovTransitionSnapshot(string Context, IReadOnlyDictionary<string, int> Next);

public sealed record MarkovModelSnapshot(
    int Version,
    string CorpusId,
    string CorpusFingerprint,
    MarkovTrainingOptions Options,
    IReadOnlyList<MarkovTransitionSnapshot> Transitions,
    MarkovModelStatistics Statistics);

public sealed class NGramMarkovModel
{
    public const int MaximumTrainingCharacters = 20_000_000;
    public const int MaximumTrainingTokens = 5_000_000;
    public const int MaximumStates = 3_000_000;
    private const string Start = "\u0002<START>";
    private const string End = "\u0003<END>";
    private const char Separator = '\u001f';
    private readonly Dictionary<string, Dictionary<string, int>> _transitions;

    private NGramMarkovModel(string corpusId, MarkovTrainingOptions options, Dictionary<string, Dictionary<string, int>> transitions, MarkovModelStatistics statistics)
    {
        CorpusId = corpusId;
        Options = options;
        _transitions = transitions;
        Statistics = statistics;
    }

    public string CorpusId { get; }
    public MarkovTrainingOptions Options { get; }
    public MarkovModelStatistics Statistics { get; }

    public static NGramMarkovModel Train(TextCorpus corpus, MarkovTrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        options = options.Normalize();
        var normalized = CorpusNormalizer.Normalize(corpus.Text, corpus.EffectiveNormalization);
        if (normalized.Length > MaximumTrainingCharacters) throw new InvalidDataException($"Corpus is too large for one Markov model ({normalized.Length:N0} characters; limit {MaximumTrainingCharacters:N0}). Split it into focused corpora.");
        var sequences = CorpusTokenizer.TokenizeSequences(normalized, options.Mode, options.IncludePunctuation);
        if (sequences.Count == 0 || sequences.All(x => x.Count == 0)) throw new InvalidDataException("The corpus contains no trainable text after normalization.");

        var transitions = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var tokenCount = 0;
        foreach (var sequence0 in sequences)
        {
            var sequence = options.PreserveCase ? sequence0 : sequence0.Select(x => x.ToLowerInvariant()).ToArray();
            foreach (var token in sequence) unique.Add(token);
            tokenCount += sequence.Count;
            if (tokenCount > MaximumTrainingTokens) throw new InvalidDataException($"Corpus has more than {MaximumTrainingTokens:N0} training tokens. Split it into focused corpora.");
            var padded = Enumerable.Repeat(Start, options.Order).Concat(sequence).Append(End).ToArray();
            for (var i = options.Order; i < padded.Length; i++)
            {
                var minContext = options.VariableOrderBackoff ? 0 : options.Order;
                for (var contextLength = minContext; contextLength <= options.Order; contextLength++)
                {
                    var key = EncodeContext(padded.Skip(i - contextLength).Take(contextLength));
                    if (!transitions.TryGetValue(key, out var next))
                    {
                        if (transitions.Count >= MaximumStates) throw new InvalidDataException($"Markov model exceeded the {MaximumStates:N0}-state safety limit. Lower the order or split the corpus.");
                        transitions[key] = next = new Dictionary<string, int>(StringComparer.Ordinal);
                    }
                    var token = padded[i];
                    next[token] = next.TryGetValue(token, out var count) ? count + 1 : 1;
                }
            }
        }

        var corpusFingerprint = corpus.Fingerprint();
        var modelFingerprint = ComputeModelFingerprint(corpusFingerprint, options);
        var stats = new MarkovModelStatistics(options.Mode, options.Order, normalized.Length, sequences.Count, tokenCount, unique.Count,
            transitions.Count, transitions.Values.Sum(x => x.Count), DateTimeOffset.UtcNow, corpusFingerprint, modelFingerprint);
        return new NGramMarkovModel(corpus.Id, options, transitions, stats);
    }

    public static NGramMarkovModel FromSnapshot(MarkovModelSnapshot snapshot)
    {
        var transitions = snapshot.Transitions.ToDictionary(x => x.Context, x => new Dictionary<string, int>(x.Next, StringComparer.Ordinal), StringComparer.Ordinal);
        return new NGramMarkovModel(snapshot.CorpusId, snapshot.Options.Normalize(), transitions, snapshot.Statistics);
    }

    public MarkovModelSnapshot ToSnapshot()
        => new(1, CorpusId, Statistics.CorpusFingerprint, Options,
            _transitions.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new MarkovTransitionSnapshot(x.Key, new Dictionary<string, int>(x.Value))).ToArray(), Statistics);

    public string Generate(IDeterministicRandom random, int? minimumTokens = null, int? maximumTokens = null)
    {
        var min = Math.Max(1, minimumTokens ?? Options.MinimumOutputTokens);
        var max = Math.Max(min, maximumTokens ?? Options.MaximumOutputTokens);
        var output = new List<string>(max);
        var context = Enumerable.Repeat(Start, Options.Order).ToList();
        for (var step = 0; step < max + Options.Order + 32; step++)
        {
            var next = PickNext(context, random);
            if (next == End)
            {
                if (output.Count >= min) break;
                output.Clear();
                context = Enumerable.Repeat(Start, Options.Order).ToList();
                continue;
            }
            if (next == Start) continue;
            output.Add(next);
            if (output.Count >= max) break;
            context.Add(next);
            while (context.Count > Options.Order) context.RemoveAt(0);
        }
        return CorpusTokenizer.Detokenize(output, Options.Mode);
    }

    private string PickNext(IReadOnlyList<string> context, IDeterministicRandom random)
    {
        var minimum = Options.VariableOrderBackoff ? Math.Max(0, Options.MinimumOrder) : Options.Order;
        for (var length = Math.Min(Options.Order, context.Count); length >= minimum; length--)
        {
            var key = EncodeContext(context.Skip(context.Count - length));
            if (!_transitions.TryGetValue(key, out var next) || next.Count == 0) continue;
            var total = next.Values.Sum();
            var roll = random.NextInt(total);
            foreach (var pair in next.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (roll < pair.Value) return pair.Key;
                roll -= pair.Value;
            }
        }
        if (_transitions.TryGetValue(string.Empty, out var fallback) && fallback.Count > 0)
        {
            var total = fallback.Values.Sum();
            var roll = random.NextInt(total);
            foreach (var pair in fallback.OrderBy(x => x.Key, StringComparer.Ordinal)) { if (roll < pair.Value) return pair.Key; roll -= pair.Value; }
        }
        return End;
    }

    private static string EncodeContext(IEnumerable<string> tokens) => string.Join(Separator, tokens);

    public static string ComputeModelFingerprint(string corpusFingerprint, MarkovTrainingOptions options)
    {
        var json = JsonSerializer.Serialize(options.Normalize(), CorpusJson.Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(corpusFingerprint + "\n" + json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
