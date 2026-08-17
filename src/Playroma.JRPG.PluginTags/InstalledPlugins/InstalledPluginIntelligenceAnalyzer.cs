namespace Playroma.JRPG.PluginTags;

public sealed class InstalledPluginIntelligenceAnalyzer
{
    public InstalledPluginAnalysis Analyze(
        IEnumerable<InstalledPluginDescriptor> installedPlugins,
        PluginIntelligenceProfile profile,
        IEnumerable<string>? scriptPluginIds = null)
    {
        ArgumentNullException.ThrowIfNull(installedPlugins);
        ArgumentNullException.ThrowIfNull(profile);

        Dictionary<string, KnownPluginProfile> known = BuildKnownIndex(profile.Plugins);
        InstalledPluginDescriptor[] installed = installedPlugins
            .Select(Normalize)
            .Select(plugin => known.TryGetValue(plugin.PluginId, out KnownPluginProfile? recognized)
                ? plugin with { PluginId = recognized.PluginId }
                : plugin)
            .OrderBy(plugin => plugin.LoadOrder)
            .ToArray();
        Dictionary<string, List<InstalledPluginDescriptor>> groups = installed
            .GroupBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InstalledPluginDescriptor> first = groups
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<InstalledPluginIssue>> perPlugin = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<InstalledPluginCompatibilityState>> states = new(StringComparer.OrdinalIgnoreCase);
        List<InstalledPluginIssue> issues = [];

        void Add(string pluginId, InstalledPluginCompatibilityState state, InstalledPluginIssue issue)
        {
            if (!states.TryGetValue(pluginId, out HashSet<InstalledPluginCompatibilityState>? set))
            {
                set = [];
                states[pluginId] = set;
            }
            set.Add(state);
            if (!perPlugin.TryGetValue(pluginId, out List<InstalledPluginIssue>? list))
            {
                list = [];
                perPlugin[pluginId] = list;
            }
            list.Add(issue);
            issues.Add(issue);
        }

        foreach ((string pluginId, List<InstalledPluginDescriptor> duplicates) in groups)
        {
            if (duplicates.Count <= 1) continue;
            foreach (InstalledPluginDescriptor duplicate in duplicates)
            {
                Add(pluginId, InstalledPluginCompatibilityState.DuplicateManifestEntry,
                    new InstalledPluginIssue("PI1001", InstalledPluginIssueSeverity.Error,
                        $"Plugin '{pluginId}' appears {duplicates.Count} times in plugins.js.", pluginId, LoadOrder: duplicate.LoadOrder));
            }
        }

        foreach (InstalledPluginDescriptor plugin in installed)
        {
            if (!known.TryGetValue(plugin.PluginId, out KnownPluginProfile? pluginProfile))
            {
                Add(plugin.PluginId, InstalledPluginCompatibilityState.UnknownPlugin,
                    new InstalledPluginIssue("PI1002", InstalledPluginIssueSeverity.Information,
                        $"Plugin '{plugin.PluginId}' is not in the {profile.ProviderId} intelligence catalog.", plugin.PluginId, LoadOrder: plugin.LoadOrder));
                continue;
            }

            if (!plugin.ScriptExists)
            {
                Add(plugin.PluginId, InstalledPluginCompatibilityState.MissingScript,
                    new InstalledPluginIssue("PI1003", InstalledPluginIssueSeverity.Error,
                        $"Plugin '{plugin.PluginId}' is listed in plugins.js but its script file is missing.", plugin.PluginId, LoadOrder: plugin.LoadOrder));
            }
            if (!plugin.IsEnabled)
            {
                Add(plugin.PluginId, InstalledPluginCompatibilityState.InstalledDisabled,
                    new InstalledPluginIssue("PI1004", InstalledPluginIssueSeverity.Information,
                        $"Plugin '{plugin.PluginId}' is installed but disabled.", plugin.PluginId, LoadOrder: plugin.LoadOrder));
            }

            VerifyVersionAndFingerprint(plugin, pluginProfile, Add);
        }

        foreach (PluginDependencyRule dependency in profile.Dependencies)
        {
            if (!first.TryGetValue(dependency.PluginId, out InstalledPluginDescriptor? dependent) || !dependent.IsEnabled) continue;
            if (!first.TryGetValue(dependency.RequiredPluginId, out InstalledPluginDescriptor? required))
            {
                InstalledPluginIssueSeverity severity = dependency.Strength == PluginDependencyStrength.Required
                    ? InstalledPluginIssueSeverity.Error
                    : InstalledPluginIssueSeverity.Warning;
                InstalledPluginCompatibilityState state = dependency.Strength == PluginDependencyStrength.Required
                    ? InstalledPluginCompatibilityState.MissingDependency
                    : InstalledPluginCompatibilityState.UnknownVersion;
                Add(dependent.PluginId, state,
                    new InstalledPluginIssue("PI1101", severity,
                        $"'{dependent.PluginId}' requires '{dependency.RequiredPluginId}', which is not in plugins.js.",
                        dependent.PluginId, dependency.RequiredPluginId, dependent.LoadOrder));
                continue;
            }
            if (!required.IsEnabled)
            {
                InstalledPluginIssueSeverity severity = dependency.Strength == PluginDependencyStrength.Required
                    ? InstalledPluginIssueSeverity.Error
                    : InstalledPluginIssueSeverity.Warning;
                Add(dependent.PluginId, InstalledPluginCompatibilityState.DisabledDependency,
                    new InstalledPluginIssue("PI1102", severity,
                        $"'{dependent.PluginId}' depends on '{required.PluginId}', but the dependency is disabled.",
                        dependent.PluginId, required.PluginId, dependent.LoadOrder));
            }
            if (required.LoadOrder > dependent.LoadOrder)
            {
                Add(dependent.PluginId, InstalledPluginCompatibilityState.LoadOrderViolation,
                    new InstalledPluginIssue("PI1201", InstalledPluginIssueSeverity.Error,
                        $"'{required.PluginId}' must be loaded above '{dependent.PluginId}'.",
                        dependent.PluginId, required.PluginId, dependent.LoadOrder));
            }
        }

        foreach (PluginLoadOrderRule rule in profile.LoadOrderRules)
        {
            if (!first.TryGetValue(rule.BeforePluginId, out InstalledPluginDescriptor? before) ||
                !first.TryGetValue(rule.AfterPluginId, out InstalledPluginDescriptor? after) ||
                !before.IsEnabled || !after.IsEnabled || before.LoadOrder < after.LoadOrder) continue;
            Add(after.PluginId, InstalledPluginCompatibilityState.LoadOrderViolation,
                new InstalledPluginIssue("PI1202",
                    rule.IsRequired ? InstalledPluginIssueSeverity.Error : InstalledPluginIssueSeverity.Warning,
                    rule.Reason, after.PluginId, before.PluginId, after.LoadOrder));
        }

        foreach (PluginConflictRule conflict in profile.Conflicts)
        {
            InstalledPluginDescriptor[] active = conflict.PluginIds
                .Select(id => first.TryGetValue(id, out InstalledPluginDescriptor? descriptor) ? descriptor : null)
                .Where(descriptor => descriptor is not null && (!conflict.EnabledOnly || descriptor.IsEnabled))
                .Cast<InstalledPluginDescriptor>()
                .ToArray();
            if (active.Length <= 1) continue;
            foreach (InstalledPluginDescriptor plugin in active)
            {
                Add(plugin.PluginId, InstalledPluginCompatibilityState.Conflict,
                    new InstalledPluginIssue("PI1301", InstalledPluginIssueSeverity.Error,
                        conflict.Message, plugin.PluginId,
                        string.Join(", ", active.Where(other => other != plugin).Select(other => other.PluginId)),
                        plugin.LoadOrder));
            }
        }

        IEnumerable<IGrouping<string, InstalledPluginDescriptor>> aliasCollisions = installed
            .SelectMany(plugin => plugin.EffectiveImportedAliases.Select(alias => (Alias: NormalizeId(alias), Plugin: plugin)))
            .Where(item => item.Alias.Length > 0)
            .GroupBy(item => item.Alias, item => item.Plugin, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(plugin => plugin.PluginId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        foreach (IGrouping<string, InstalledPluginDescriptor> collision in aliasCollisions)
        {
            string owners = string.Join(", ", collision.Select(plugin => plugin.PluginId).Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (InstalledPluginDescriptor plugin in collision)
            {
                Add(plugin.PluginId, InstalledPluginCompatibilityState.Conflict,
                    new InstalledPluginIssue("PI1302", InstalledPluginIssueSeverity.Warning,
                        $"Imported identifier '{collision.Key}' is declared by multiple plugins: {owners}.",
                        plugin.PluginId, collision.Key, plugin.LoadOrder));
            }
        }

        string[] unregistered = (scriptPluginIds ?? [])
            .Select(NormalizeId)
            .Where(id => id.Length > 0 && !first.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string id in unregistered)
        {
            issues.Add(new InstalledPluginIssue("PI1401", InstalledPluginIssueSeverity.Warning,
                $"Script '{id}.js' exists in js/plugins but is not registered in plugins.js.", id));
        }

        InstalledPluginAnalysisEntry[] entries = installed.Select(plugin =>
        {
            known.TryGetValue(plugin.PluginId, out KnownPluginProfile? pluginProfile);
            IReadOnlyList<InstalledPluginCompatibilityState> pluginStates = states.TryGetValue(plugin.PluginId, out HashSet<InstalledPluginCompatibilityState>? set)
                ? set.OrderByDescending(InstalledPluginStateOrdering.Rank).ToArray()
                : [InstalledPluginCompatibilityState.Available];
            IReadOnlyList<InstalledPluginIssue> pluginIssues = perPlugin.TryGetValue(plugin.PluginId, out List<InstalledPluginIssue>? list)
                ? list.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray()
                : [];
            return new InstalledPluginAnalysisEntry(plugin, pluginProfile, pluginStates, pluginIssues);
        }).ToArray();

        Dictionary<string, InstalledPluginStatus> statuses = entries
            .GroupBy(entry => entry.Installed.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                InstalledPluginAnalysisEntry entry = group.First();
                return new InstalledPluginStatus(entry.Installed.PluginId, true, entry.Installed.IsEnabled,
                    entry.Installed.Version, entry.Installed.LoadOrder, entry.Installed.SourceFingerprint);
            }, StringComparer.OrdinalIgnoreCase);

        return new InstalledPluginAnalysis(
            profile.ProviderId,
            entries,
            issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.LoadOrder ?? int.MaxValue).ThenBy(issue => issue.Code, StringComparer.Ordinal).ToArray(),
            BuildSuggestedMoves(installed, profile),
            unregistered,
            statuses);
    }

    public IReadOnlyList<PluginFeatureAvailability> EvaluateFeatures(
        IEnumerable<PluginFeatureDefinition> features,
        InstalledPluginAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(analysis);
        Dictionary<string, InstalledPluginAnalysisEntry> entries = analysis.Entries
            .GroupBy(entry => entry.Installed.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return features.Select(feature => EvaluateFeature(feature, entries)).ToArray();
    }

    private static PluginFeatureAvailability EvaluateFeature(
        PluginFeatureDefinition feature,
        IReadOnlyDictionary<string, InstalledPluginAnalysisEntry> entries)
    {
        List<string> requirements = [feature.PluginId];
        requirements.AddRange(feature.EffectiveRequiredPlugins.Where(id => !requirements.Contains(id, StringComparer.OrdinalIgnoreCase)));
        PluginFeatureAvailability? advisory = null;
        foreach (string requiredId in requirements)
        {
            if (!entries.TryGetValue(requiredId, out InstalledPluginAnalysisEntry? entry))
            {
                PluginFeatureAvailabilityState state = string.Equals(requiredId, feature.PluginId, StringComparison.OrdinalIgnoreCase)
                    ? PluginFeatureAvailabilityState.OwnerPluginMissing
                    : PluginFeatureAvailabilityState.RequiredPluginMissing;
                return new(feature.Key, state, $"Required plugin '{requiredId}' is not installed.", [requiredId]);
            }
            if (!entry.Installed.IsEnabled)
            {
                PluginFeatureAvailabilityState state = string.Equals(requiredId, feature.PluginId, StringComparison.OrdinalIgnoreCase)
                    ? PluginFeatureAvailabilityState.OwnerPluginDisabled
                    : PluginFeatureAvailabilityState.RequiredPluginDisabled;
                return new(feature.Key, state, $"Required plugin '{requiredId}' is disabled.", [requiredId]);
            }
            if (entry.States.Contains(InstalledPluginCompatibilityState.Conflict))
                return new(feature.Key, PluginFeatureAvailabilityState.PluginConflict, $"Plugin '{requiredId}' conflicts with another installed plugin.", [requiredId]);
            if (entry.States.Contains(InstalledPluginCompatibilityState.MissingDependency) || entry.States.Contains(InstalledPluginCompatibilityState.DisabledDependency))
                return new(feature.Key, PluginFeatureAvailabilityState.RequiredPluginMissing, $"Plugin '{requiredId}' has an unsatisfied dependency.", [requiredId]);
            if (entry.States.Contains(InstalledPluginCompatibilityState.LoadOrderViolation))
                return new(feature.Key, PluginFeatureAvailabilityState.LoadOrderInvalid, $"Plugin '{requiredId}' is in an invalid load-order position.", [requiredId]);
            if (entry.States.Contains(InstalledPluginCompatibilityState.UnverifiedSource))
                advisory ??= new(feature.Key, PluginFeatureAvailabilityState.SourceUnverified, $"Plugin '{requiredId}' is installed, but its source fingerprint is not verified.", [requiredId]);
            else if (entry.States.Contains(InstalledPluginCompatibilityState.UnknownVersion))
                advisory ??= new(feature.Key, PluginFeatureAvailabilityState.VersionUnverified, $"Plugin '{requiredId}' is installed, but its version is not verified.", [requiredId]);
        }
        return advisory ?? new(feature.Key, PluginFeatureAvailabilityState.Available, "All required plugins are installed, enabled, and ordered correctly.", []);
    }

    private static void VerifyVersionAndFingerprint(
        InstalledPluginDescriptor installed,
        KnownPluginProfile profile,
        Action<string, InstalledPluginCompatibilityState, InstalledPluginIssue> add)
    {
        if (profile.EffectiveKnownVersions.Count == 0) return;
        if (string.IsNullOrWhiteSpace(installed.Version))
        {
            add(installed.PluginId, InstalledPluginCompatibilityState.UnknownVersion,
                new InstalledPluginIssue("PI1501", InstalledPluginIssueSeverity.Warning,
                    $"The installed version of '{installed.PluginId}' could not be detected.", installed.PluginId, LoadOrder: installed.LoadOrder));
        }
        else if (!profile.EffectiveKnownVersions.Any(version => string.Equals(version.Version, installed.Version, StringComparison.OrdinalIgnoreCase)))
        {
            add(installed.PluginId, InstalledPluginCompatibilityState.UnknownVersion,
                new InstalledPluginIssue("PI1502", InstalledPluginIssueSeverity.Warning,
                    $"Version {installed.Version} of '{installed.PluginId}' is not in the verified catalog.", installed.PluginId, LoadOrder: installed.LoadOrder));
        }

        if (!string.IsNullOrWhiteSpace(installed.SourceFingerprint))
        {
            bool knownFingerprint = profile.EffectiveKnownVersions
                .SelectMany(version => version.SourceFingerprints)
                .Any(hash => string.Equals(hash, installed.SourceFingerprint, StringComparison.OrdinalIgnoreCase));
            if (!knownFingerprint)
            {
                add(installed.PluginId, InstalledPluginCompatibilityState.UnverifiedSource,
                    new InstalledPluginIssue("PI1503", InstalledPluginIssueSeverity.Warning,
                        $"The installed source for '{installed.PluginId}' does not match a verified fingerprint.", installed.PluginId, LoadOrder: installed.LoadOrder));
            }
        }
    }

    private static IReadOnlyList<PluginLoadOrderMove> BuildSuggestedMoves(
        IReadOnlyList<InstalledPluginDescriptor> installed,
        PluginIntelligenceProfile profile)
    {
        Dictionary<string, InstalledPluginDescriptor> map = installed
            .GroupBy(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<PluginLoadOrderMove> moves = [];
        foreach (PluginLoadOrderRule rule in profile.LoadOrderRules)
        {
            if (!map.TryGetValue(rule.BeforePluginId, out InstalledPluginDescriptor? before) ||
                !map.TryGetValue(rule.AfterPluginId, out InstalledPluginDescriptor? after) ||
                before.LoadOrder < after.LoadOrder) continue;
            moves.Add(new PluginLoadOrderMove(before.PluginId, before.LoadOrder, Math.Max(1, after.LoadOrder - 1), rule.Reason));
        }
        foreach (PluginDependencyRule rule in profile.Dependencies.Where(rule => rule.Strength == PluginDependencyStrength.Required))
        {
            if (!map.TryGetValue(rule.RequiredPluginId, out InstalledPluginDescriptor? required) ||
                !map.TryGetValue(rule.PluginId, out InstalledPluginDescriptor? dependent) ||
                required.LoadOrder < dependent.LoadOrder) continue;
            moves.Add(new PluginLoadOrderMove(required.PluginId, required.LoadOrder, Math.Max(1, dependent.LoadOrder - 1),
                $"{required.PluginId} must load above {dependent.PluginId}."));
        }
        return moves.GroupBy(move => move.PluginId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(move => move.SuggestedLoadOrder).First())
            .OrderBy(move => move.SuggestedLoadOrder)
            .ToArray();
    }

    private static Dictionary<string, KnownPluginProfile> BuildKnownIndex(IEnumerable<KnownPluginProfile> profiles)
    {
        Dictionary<string, KnownPluginProfile> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KnownPluginProfile profile in profiles)
        {
            result[NormalizeId(profile.PluginId)] = profile;
            foreach (string alias in profile.EffectiveAliases) result.TryAdd(NormalizeId(alias), profile);
        }
        return result;
    }

    private static InstalledPluginDescriptor Normalize(InstalledPluginDescriptor descriptor) => descriptor with
    {
        PluginId = NormalizeId(descriptor.PluginId),
        SourceFingerprint = descriptor.SourceFingerprint?.Trim().ToLowerInvariant()
    };

    public static string NormalizeId(string pluginId)
    {
        string result = (pluginId ?? string.Empty).Trim();
        if (result.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) result = result[..^3];
        return result;
    }
}
