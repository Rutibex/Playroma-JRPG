using Playroma.Text.Core.Names;

namespace Playroma.Text.Generators.Names;

public sealed class LanguageProfileRegistry
{
    private readonly Dictionary<string, LanguageProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public LanguageProfileRegistry(IEnumerable<LanguageProfile>? profiles = null)
    {
        if (profiles is not null)
            foreach (var profile in profiles) Register(profile);
    }

    public IReadOnlyCollection<string> Ids => _profiles.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(LanguageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Language profile id cannot be empty.", nameof(profile));
        _profiles[profile.Id] = profile;
    }

    public bool TryGetRaw(string id, out LanguageProfile? profile) => _profiles.TryGetValue(id, out profile);

    public LanguageProfile? Resolve(string id)
    {
        if (!_profiles.TryGetValue(id, out var profile)) return null;
        var resolved = Resolve(profile, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var errors = LanguageProfileValidator.Validate(resolved).Where(x => x.Severity == Playroma.Text.Core.Generation.GenerationDiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException($"Language profile '{id}' is invalid: {string.Join("; ", errors.Select(x => x.Message))}");
        return resolved;
    }

    private LanguageProfile Resolve(LanguageProfile child, HashSet<string> chain)
    {
        if (!chain.Add(child.Id)) throw new InvalidOperationException($"Language profile inheritance cycle detected at '{child.Id}'.");
        if (string.IsNullOrWhiteSpace(child.ParentId)) return child;
        if (!_profiles.TryGetValue(child.ParentId, out var parent)) throw new InvalidOperationException($"Language profile '{child.Id}' references missing parent '{child.ParentId}'.");
        var resolvedParent = Resolve(parent, chain);
        return Merge(resolvedParent, child);
    }

    internal static LanguageProfile Merge(LanguageProfile parent, LanguageProfile child)
    {
        var domains = parent.EffectiveDomains.ToDictionary(x => x.Key, x => x.Value);
        foreach (var pair in child.EffectiveDomains) domains[pair.Key] = pair.Value;

        var corpora = parent.EffectiveCorpora.ToDictionary(x => x.Key, x => x.Value);
        foreach (var pair in child.EffectiveCorpora) corpora[pair.Key] = pair.Value;

        var tags = new HashSet<string>(parent.EffectiveTags, StringComparer.OrdinalIgnoreCase);
        tags.UnionWith(child.EffectiveTags);

        return child with
        {
            ParentId = null,
            Onsets = child.Onsets.Count > 0 ? child.Onsets : parent.Onsets,
            Nuclei = child.Nuclei.Count > 0 ? child.Nuclei : parent.Nuclei,
            Codas = child.Codas.Count > 0 ? child.Codas : parent.Codas,
            SyllablePatterns = child.SyllablePatterns.Count > 0 ? child.SyllablePatterns : parent.SyllablePatterns,
            ForbiddenSequences = parent.EffectiveForbiddenSequences.Concat(child.EffectiveForbiddenSequences).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OrthographicRules = parent.EffectiveOrthographicRules.Concat(child.EffectiveOrthographicRules).ToArray(),
            Domains = domains,
            Corpora = corpora,
            Tags = tags
        };
    }

    public static LanguageProfileRegistry CreateBuiltIn() => new(BuiltInLanguageProfiles.All);
}
