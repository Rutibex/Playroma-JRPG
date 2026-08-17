using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playroma.Text.Core.Corpora;

public enum CorpusTokenMode
{
    Character,
    Word,
    Sentence
}

public sealed record CorpusNormalizationOptions(
    bool NormalizeUnicode = true,
    bool NormalizeNewlines = true,
    bool TrimLines = true,
    bool RemoveEmptyLines = true,
    bool CollapseWhitespace = true,
    bool RemoveDuplicateLines = false,
    bool Lowercase = false);

public sealed record CorpusSourceMetadata(
    string DisplayName,
    string? OriginalFileName = null,
    string? Author = null,
    string? License = null,
    string? SourceUri = null,
    string? Notes = null,
    string? Sha256 = null,
    DateTimeOffset? ImportedAtUtc = null);

public sealed record TextCorpus(
    string Id,
    int Version,
    string Name,
    string Text,
    IReadOnlyList<CorpusSourceMetadata> Sources,
    CorpusNormalizationOptions? Normalization = null,
    MarkovTrainingOptions? DefaultTraining = null,
    string? Description = null,
    IReadOnlySet<string>? Tags = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? UpdatedAtUtc = null)
{
    public CorpusNormalizationOptions EffectiveNormalization => Normalization ?? new CorpusNormalizationOptions();

    public string Fingerprint()
    {
        var canonical = JsonSerializer.Serialize(new { Version, Text, Normalization = EffectiveNormalization }, CorpusJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record MarkovTrainingOptions(
    CorpusTokenMode Mode = CorpusTokenMode.Character,
    int Order = 3,
    int MinimumOrder = 1,
    bool VariableOrderBackoff = true,
    bool PreserveCase = true,
    bool IncludePunctuation = true,
    int MinimumOutputTokens = 4,
    int MaximumOutputTokens = 40,
    bool PreventExactCorpusMatches = true,
    double MaximumCorpusSimilarity = 0.86,
    int CandidateAttempts = 64)
{
    public MarkovTrainingOptions Normalize() => this with
    {
        Order = Math.Clamp(Order, 1, 8),
        MinimumOrder = Math.Clamp(MinimumOrder, 1, Math.Clamp(Order, 1, 8)),
        MinimumOutputTokens = Math.Clamp(MinimumOutputTokens, 1, 10_000),
        MaximumOutputTokens = Math.Clamp(MaximumOutputTokens, Math.Clamp(MinimumOutputTokens, 1, 10_000), 10_000),
        MaximumCorpusSimilarity = Math.Clamp(MaximumCorpusSimilarity, 0, 1),
        CandidateAttempts = Math.Clamp(CandidateAttempts, 1, 512)
    };
}

public sealed record MarkovModelStatistics(
    CorpusTokenMode Mode,
    int Order,
    long InputCharacters,
    int SequenceCount,
    int TokenCount,
    int UniqueTokenCount,
    int StateCount,
    int TransitionCount,
    DateTimeOffset TrainedAtUtc,
    string CorpusFingerprint,
    string ModelFingerprint);

public sealed record CorpusSimilarityReport(
    bool IsExactMatch,
    double NormalizedEditSimilarity,
    double NGramOverlap,
    double LongestCommonSubstringRatio,
    double CombinedSimilarity,
    string? ClosestPassage = null);

public interface ICorpusResolver
{
    ValueTask<TextCorpus?> ResolveAsync(string corpusId, CancellationToken cancellationToken = default);
}

public static class CorpusJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
