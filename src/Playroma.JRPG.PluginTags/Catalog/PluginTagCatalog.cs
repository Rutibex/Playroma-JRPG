using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public interface IPluginTagProvider
{
    PluginProviderManifest Manifest { get; }
    IReadOnlyList<PluginFeatureDefinition> Features { get; }
}

public sealed class StaticPluginTagProvider : IPluginTagProvider
{
    public StaticPluginTagProvider(PluginProviderManifest manifest, IEnumerable<PluginFeatureDefinition> features)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Features = features?.ToArray() ?? throw new ArgumentNullException(nameof(features));
    }

    public PluginProviderManifest Manifest { get; }
    public IReadOnlyList<PluginFeatureDefinition> Features { get; }
}

public sealed class PluginTagCatalogException : Exception
{
    public PluginTagCatalogException(string message) : base(message)
    {
    }
}

public sealed class PluginTagCatalog
{
    private readonly IReadOnlyDictionary<string, PluginProviderManifest> _providers;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureDefinition> _features;
    private readonly IReadOnlyList<PluginFeatureDefinition> _orderedFeatures;

    internal PluginTagCatalog(
        IReadOnlyDictionary<string, PluginProviderManifest> providers,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureDefinition> features)
    {
        _providers = providers;
        _features = features;
        _orderedFeatures = features.Values
            .OrderBy(feature => feature.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyCollection<PluginProviderManifest> Providers => _providers.Values.ToArray();
    public IReadOnlyList<PluginFeatureDefinition> Features => _orderedFeatures;

    public bool TryGetFeature(PluginFeatureKey key, out PluginFeatureDefinition definition)
    {
        if (_features.TryGetValue(key, out PluginFeatureDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public PluginFeatureDefinition GetRequiredFeature(PluginFeatureKey key) =>
        TryGetFeature(key, out PluginFeatureDefinition definition)
            ? definition
            : throw new KeyNotFoundException("Plugin feature is not registered: " + key);

    public IReadOnlyList<PluginFeatureDefinition> Query(
        PluginTagContext context,
        string? searchText = null,
        bool includeOtherDocumentKinds = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        IEnumerable<PluginFeatureDefinition> query = _orderedFeatures.Where(feature => feature.Surface == context.Surface);
        if (!includeOtherDocumentKinds)
        {
            query = query.Where(feature =>
                feature.DocumentKinds.Count == 0 ||
                feature.DocumentKinds.Contains(PluginDocumentKind.Unknown) ||
                feature.DocumentKinds.Contains(context.DocumentKind));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string needle = searchText.Trim();
            query = query.Where(feature =>
                feature.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                feature.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                feature.PluginId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                feature.FeatureId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                feature.EffectiveSearchTerms.Any(term => term.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToArray();
    }

    public IReadOnlyList<PluginFeatureDefinition> ForSurface(PluginAuthoringSurface surface) =>
        _orderedFeatures
            .Where(feature => feature.Surface == surface)
            .OrderByDescending(feature => feature.MatchPriority)
            .ThenBy(feature => feature.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.FeatureId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static PluginTagCatalog Empty { get; } = new PluginTagCatalogBuilder().Build();
}

public sealed class PluginTagCatalogBuilder
{
    private readonly Dictionary<string, PluginProviderManifest> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PluginFeatureKey, PluginFeatureDefinition> _features = new();

    public PluginTagCatalogBuilder AddProvider(IPluginTagProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        AddProvider(provider.Manifest);
        foreach (PluginFeatureDefinition feature in provider.Features)
        {
            if (!string.Equals(feature.ProviderId, provider.Manifest.ProviderId, StringComparison.OrdinalIgnoreCase))
                throw new PluginTagCatalogException($"Feature {feature.Key} does not belong to provider {provider.Manifest.ProviderId}.");
            AddFeature(feature);
        }
        return this;
    }

    public PluginTagCatalogBuilder AddProvider(PluginProviderManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.ProviderId))
            throw new PluginTagCatalogException("Provider ID cannot be empty.");
        if (_providers.TryGetValue(manifest.ProviderId, out PluginProviderManifest? existing) && existing != manifest)
            throw new PluginTagCatalogException("A different provider manifest is already registered for " + manifest.ProviderId + ".");
        _providers[manifest.ProviderId] = manifest;
        return this;
    }

    public PluginTagCatalogBuilder AddFeature(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ValidateFeature(feature);
        if (!_providers.ContainsKey(feature.ProviderId))
            throw new PluginTagCatalogException("Feature provider is not registered: " + feature.ProviderId);
        if (!_features.TryAdd(feature.Key, feature))
            throw new PluginTagCatalogException("Duplicate plugin feature key: " + feature.Key);
        return this;
    }

    public PluginTagCatalog Build() => new(
        new Dictionary<string, PluginProviderManifest>(_providers, StringComparer.OrdinalIgnoreCase),
        new Dictionary<PluginFeatureKey, PluginFeatureDefinition>(_features));

    private static void ValidateFeature(PluginFeatureDefinition feature)
    {
        if (string.IsNullOrWhiteSpace(feature.ProviderId)) throw new PluginTagCatalogException("Feature provider ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(feature.PluginId)) throw new PluginTagCatalogException("Feature plugin ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(feature.FeatureId)) throw new PluginTagCatalogException("Feature ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(feature.DisplayName)) throw new PluginTagCatalogException("Feature display name cannot be empty.");
        if (string.IsNullOrWhiteSpace(feature.Syntax.CanonicalTemplate)) throw new PluginTagCatalogException("Canonical syntax cannot be empty: " + feature.Key);
        if (feature.Syntax.Patterns.Count == 0) throw new PluginTagCatalogException("At least one syntax pattern is required: " + feature.Key);
        if (feature.Syntax.IsBlock && (string.IsNullOrWhiteSpace(feature.Syntax.ClosingTemplate) || feature.Syntax.ClosingPatterns is null || feature.Syntax.ClosingPatterns.Count == 0))
            throw new PluginTagCatalogException("Block syntax requires a closing template and pattern: " + feature.Key);

        foreach (PluginSyntaxPattern pattern in feature.Syntax.Patterns)
            ValidatePattern(feature.Key, pattern);
        foreach (PluginSyntaxPattern pattern in feature.Syntax.ClosingPatterns ?? [])
            ValidatePattern(feature.Key, pattern);

        HashSet<string> arguments = new(StringComparer.OrdinalIgnoreCase);
        foreach (PluginArgumentDefinition argument in feature.Arguments)
        {
            if (!arguments.Add(argument.Name)) throw new PluginTagCatalogException("Duplicate argument name " + argument.Name + " in " + feature.Key + ".");
            if (argument.Minimum is not null && argument.Maximum is not null && argument.Minimum > argument.Maximum)
                throw new PluginTagCatalogException("Argument minimum exceeds maximum for " + feature.Key + "/" + argument.Name + ".");
        }

        HashSet<string> placeholders = ExtractPlaceholders(feature.Syntax.CanonicalTemplate);
        if (feature.Syntax.ClosingTemplate is not null)
            placeholders.UnionWith(ExtractPlaceholders(feature.Syntax.ClosingTemplate));
        foreach (string placeholder in placeholders)
        {
            if (!arguments.Contains(placeholder))
                throw new PluginTagCatalogException($"Template placeholder {{{placeholder}}} has no argument definition in {feature.Key}.");
        }
    }

    private static void ValidatePattern(PluginFeatureKey key, PluginSyntaxPattern pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern.RegularExpression))
            throw new PluginTagCatalogException("Syntax pattern cannot be empty: " + key);
        try
        {
            _ = new Regex(pattern.RegularExpression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException exception)
        {
            throw new PluginTagCatalogException($"Invalid regular expression in {key}: {exception.Message}");
        }
    }

    private static HashSet<string> ExtractPlaceholders(string template)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(template, "\\{(?<name>[A-Za-z][A-Za-z0-9_]*)\\}"))
            names.Add(match.Groups["name"].Value);
        return names;
    }
}

public sealed class PluginTagProviderRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IPluginTagProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IPluginTagProvider> Providers
    {
        get
        {
            lock (_gate)
                return _providers.Values.OrderBy(provider => provider.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Register(IPluginTagProvider provider, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_gate)
        {
            if (!replace && _providers.ContainsKey(provider.Manifest.ProviderId))
                throw new PluginTagCatalogException("Provider is already registered: " + provider.Manifest.ProviderId);
            _providers[provider.Manifest.ProviderId] = provider;
        }
    }

    public bool Unregister(string providerId)
    {
        lock (_gate)
            return _providers.Remove(providerId);
    }

    public PluginTagCatalog CreateCatalog()
    {
        lock (_gate)
        {
            PluginTagCatalogBuilder builder = new();
            foreach (IPluginTagProvider provider in _providers.Values)
                builder.AddProvider(provider);
            return builder.Build();
        }
    }
}
