using System.IO.Compression;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyBundleAuditor
{
    private static readonly HashSet<string> KnownConflictingDuplicates = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_LevelUpGrowthEffects.js",
        "YEP_X_ItemUpgradeSlots.js"
    };

    private static readonly HashSet<string> SpecializedEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_BattleAICore",
        "YEP_X_ActorAutoBattleAI",
        "YEP_BattleEngineCore",
        "YEP_X_ActSeqPack1",
        "YEP_X_ActSeqPack2",
        "YEP_X_ActSeqPack3",
        "YEP_Z_ActionBeginEnd",
        "YEP_SkillCore",
        "YEP_X_SkillCooldowns",
        "YEP_X_LimitedSkillUses",
        "YEP_X_SkillCostItems",
        "YEP_SkillMasteryLevels",
        "YEP_SkillLearnSystem",
        "YEP_Z_SkillRewards",
        "YEP_AutoPassiveStates",
        "YEP_X_PassiveAuras",
        "YEP_Z_PassiveCases",
        "YEP_BuffsStatesCore",
        "YEP_X_ExtDoT",
        "YEP_X_StateCategories",
        "YEP_Z_StateProtection",
        "YEP_X_TickBasedRegen",
        "YEP_ItemCore",
        "YEP_X_AttachAugments",
        "YEP_X_ItemDurability",
        "YEP_X_ItemUpgradeSlots",
        "YEP_X_ItemDisassemble",
        "YEP_ItemSynthesis",
        "YEP_X_ItemRequirements",
        "YEP_X_EquipRequirements",
        "YEP_X_CondShopPrices",
        "YEP_X_MoreCurrencies",
        "YEP_EnemyLevels",
        "YEP_X_EnemyBaseParam",
        "YEP_X_MapEnemyLevels",
        "YEP_X_AnimatedSVEnemies",
        "KELYEP_DragonBones",
        "YEP_X_WeakEnemyPoses",
        "YEP_X_VisualHpGauge",
        "YEP_X_VisualStateFX",
        "YEP_RegionEvents",
        "YEP_RegionRestrictions",
        "YEP_RegionBattlebacks",
        "YEP_FootstepSounds",
        "YEP_SlipperyTiles",
        "YEP_X_VehicleRestrict",
        "YEP_MoveRouteCore",
        "YEP_MessageCore",
        "YEP_X_ExtMesPack1",
        "YEP_X_ExtMesPack2"
    };

    private static readonly HashSet<string> Phase6Plugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_BattleAICore", "YEP_X_ActorAutoBattleAI", "YEP_BattleEngineCore",
        "YEP_X_ActSeqPack1", "YEP_X_ActSeqPack2", "YEP_X_ActSeqPack3", "YEP_Z_ActionBeginEnd"
    };

    private static readonly HashSet<string> Phase7Plugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_AutoPassiveStates", "YEP_EnhancedTP", "YEP_EquipBattleSkills", "YEP_JobPoints",
        "YEP_StatAllocation", "YEP_X_EBSAllowedTypes", "YEP_X_EquipSkillTiers",
        "YEP_X_MoreTPModes1", "YEP_X_MoreTPModes2", "YEP_X_MoreTPModes3", "YEP_X_MoreTPModes4",
        "YEP_ClassChangeCore", "YEP_X_Subclass", "YEP_LevelUpGrowthEffects"
    };

    private static readonly HashSet<string> Phase9Plugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_BuffsStatesCore", "YEP_X_ExtDoT", "YEP_X_StateCategories", "YEP_X_TickBasedRegen",
        "YEP_Z_StateProtection", "YEP_X_PassiveAuras", "YEP_Z_PassiveCases", "YEP_EnemyLevels",
        "YEP_X_EnemyBaseParam", "YEP_X_MapEnemyLevels", "YEP_X_AnimatedSVEnemies",
        "KELYEP_DragonBones", "YEP_X_WeakEnemyPoses", "YEP_X_VisualHpGauge",
        "YEP_X_VisualStateFX", "YEP_ExtraEnemyDrops", "YEP_SwapEnemies"
    };

    private static readonly HashSet<string> Phase10UtilityPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEP_DashToggle", "YEP_DisableAutoShadowExt", "YEP_FloorDamage", "YEP_FootstepSounds",
        "YEP_RegionBattlebacks", "YEP_RegionEvents", "YEP_RegionRestrictions", "YEP_SaveEventLocations",
        "YEP_SlipperyTiles", "YEP_SmartJump", "YEP_X_VehicleRestrict", "YEP_BaseTroopEvents",
        "YEP_CallEvent", "YEP_MergeCommonEvents", "YEP_ButtonCommonEvents", "YEP_PictureCommonEvents"
    };

    private static readonly Dictionary<string, string> ManualDependencyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YEP_BattleSysATB"] = "YEP_X_BattleSysATB"
    };

    private readonly YanflyPluginSourceAnalyzer analyzer = new();

    public YanflyBundleAuditResult Audit(string inputPath, DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
            throw new FileNotFoundException("Yanfly bundle input was not found.", inputPath);

        List<YanflyBundleDiagnostic> diagnostics = [];
        List<YanflySourceAnalysis> analyses = [];
        int totalJavaScriptFiles = 0;
        int ignoredSeparatorFiles = 0;

        foreach ((string relativePath, byte[] bytes) in ReadJavaScriptFiles(inputPath))
        {
            totalJavaScriptFiles++;
            string fileName = Path.GetFileName(relativePath);
            if (IsSeparatorFile(fileName))
            {
                ignoredSeparatorFiles++;
                diagnostics.Add(new(
                    "YEP0001",
                    YanflyAuditSeverity.Info,
                    "Ignored collection separator JavaScript file.",
                    RelativePath: NormalizePath(relativePath)));
                continue;
            }

            if (!fileName.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase) &&
                !fileName.StartsWith("KELYEP_", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new(
                    "YEP0002",
                    YanflyAuditSeverity.Warning,
                    "JavaScript file did not match a supported YEP filename prefix and was ignored.",
                    RelativePath: NormalizePath(relativePath)));
                continue;
            }

            analyses.Add(analyzer.Analyze(relativePath, bytes));
        }

        if (analyses.Count == 0)
        {
            diagnostics.Add(new("YEP0003", YanflyAuditSeverity.Error, "No YEP plugin files were discovered."));
        }

        List<CanonicalPlugin> canonical = analyses
            .GroupBy(analysis => analysis.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => ResolveCanonical(group.ToList(), diagnostics))
            .OrderBy(item => item.Selected.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, string> aliases = BuildAliasIndex(canonical, diagnostics);
        List<YanflyPluginInventoryEntry> entries = [];
        foreach (CanonicalPlugin item in canonical)
        {
            YanflySourceAnalysis selected = item.Selected;
            List<YanflyDependencyEvidence> dependencies = ResolveDependencies(selected, aliases, diagnostics);
            IReadOnlyList<YanflyCoverageMode> coverageModes = ClassifyCoverage(selected);
            if (coverageModes.Count == 0)
            {
                diagnostics.Add(new(
                    "YEP0004",
                    YanflyAuditSeverity.Error,
                    "Plugin did not receive a Phase 0 coverage classification.",
                    selected.PluginId,
                    selected.RelativePath));
            }

            entries.Add(new YanflyPluginInventoryEntry(
                selected.PluginId,
                selected.FileName,
                selected.DisplayName,
                selected.SourceFamily,
                selected.Version,
                selected.Category,
                selected.RelativePath,
                selected.Sha256,
                item.DuplicateKind,
                item.ResolutionReason,
                item.All
                    .Select(ToOccurrence)
                    .OrderBy(occurrence => occurrence.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                selected.ImportedAliases,
                selected.AuthoringSurfaces,
                selected.DeclaredContexts,
                coverageModes,
                DeterminePlannedPhase(selected),
                selected.HelpAngleBracketExampleCount,
                selected.HasEnglishHelp,
                selected.HasPluginParameters,
                dependencies));
        }

        YanflyBundleSummary summary = BuildSummary(
            totalJavaScriptFiles,
            ignoredSeparatorFiles,
            analyses.Count,
            entries);
        YanflyBundleInventory inventory = new(
            YanflyPhase0Constitution.SchemaVersion,
            YanflyPhase0Constitution.ProviderId,
            YanflyPhase0Constitution.RuntimeFamily,
            Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            generatedAtUtc ?? DateTimeOffset.UtcNow,
            summary,
            entries);

        return new YanflyBundleAuditResult(
            inventory,
            diagnostics
                .OrderBy(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.PluginId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(diagnostic => diagnostic.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static IEnumerable<(string RelativePath, byte[] Bytes)> ReadJavaScriptFiles(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            string root = Path.GetFullPath(inputPath);
            foreach (string path in Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return (NormalizePath(Path.GetRelativePath(root, path)), File.ReadAllBytes(path));
            }

            yield break;
        }

        if (!Path.GetExtension(inputPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Yanfly bundle input must be a directory or ZIP archive.");

        using ZipArchive archive = ZipFile.OpenRead(inputPath);
        foreach (ZipArchiveEntry entry in archive.Entries
                     .Where(entry => entry.FullName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using Stream stream = entry.Open();
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            yield return (NormalizePath(entry.FullName), buffer.ToArray());
        }
    }

    private static CanonicalPlugin ResolveCanonical(
        List<YanflySourceAnalysis> candidates,
        ICollection<YanflyBundleDiagnostic> diagnostics)
    {
        List<YanflySourceAnalysis> sorted = candidates
            .OrderByDescending(candidate => candidate.Version, YanflyVersionComparer.Instance)
            .ThenBy(candidate => candidate.IsFreeCollectionCopy)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        YanflySourceAnalysis selected = sorted[0];
        int distinctHashes = candidates.Select(candidate => candidate.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        YanflyDuplicateKind duplicateKind = candidates.Count switch
        {
            1 => YanflyDuplicateKind.Unique,
            _ when distinctHashes == 1 => YanflyDuplicateKind.ExactDuplicate,
            _ => YanflyDuplicateKind.ConflictingDuplicate
        };

        YanflyResolutionReason reason;
        if (candidates.Count == 1)
        {
            reason = YanflyResolutionReason.SoleOccurrence;
        }
        else if (distinctHashes == 1)
        {
            reason = YanflyResolutionReason.ExactDuplicate;
        }
        else
        {
            bool hasOlderVersion = candidates.Any(candidate =>
                YanflyVersionComparer.Instance.Compare(selected.Version, candidate.Version) > 0);
            reason = hasOlderVersion
                ? YanflyResolutionReason.HighestVersion
                : selected.IsFreeCollectionCopy
                    ? YanflyResolutionReason.ManualOverride
                    : YanflyResolutionReason.PreferredCategorizedCopy;

            bool known = KnownConflictingDuplicates.Contains(selected.FileName);
            diagnostics.Add(new(
                known ? "YEP0005" : "YEP0006",
                known ? YanflyAuditSeverity.Info : YanflyAuditSeverity.Error,
                known
                    ? $"Resolved known differing duplicate using {reason}: {selected.RelativePath}."
                    : $"Unrecognized differing duplicate requires an explicit normalization rule: {selected.FileName}.",
                selected.PluginId,
                selected.RelativePath));
        }

        return new CanonicalPlugin(selected, candidates, duplicateKind, reason);
    }

    private static Dictionary<string, string> BuildAliasIndex(
        IEnumerable<CanonicalPlugin> plugins,
        ICollection<YanflyBundleDiagnostic> diagnostics)
    {
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (CanonicalPlugin plugin in plugins)
        {
            AddAlias(aliases, plugin.Selected.PluginId, plugin.Selected.PluginId, diagnostics);
            foreach (string importedAlias in plugin.Selected.ImportedAliases)
                AddAlias(aliases, importedAlias, plugin.Selected.PluginId, diagnostics);
        }

        foreach ((string alias, string pluginId) in ManualDependencyAliases)
            aliases[alias] = pluginId;
        return aliases;
    }

    private static void AddAlias(
        IDictionary<string, string> aliases,
        string alias,
        string pluginId,
        ICollection<YanflyBundleDiagnostic> diagnostics)
    {
        if (aliases.TryGetValue(alias, out string? existing) && !existing.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new(
                "YEP0007",
                YanflyAuditSeverity.Warning,
                $"Imported alias '{alias}' is claimed by both {existing} and {pluginId}.",
                pluginId));
            return;
        }

        aliases[alias] = pluginId;
    }

    private static List<YanflyDependencyEvidence> ResolveDependencies(
        YanflySourceAnalysis source,
        IReadOnlyDictionary<string, string> aliases,
        ICollection<YanflyBundleDiagnostic> diagnostics)
    {
        List<YanflyDependencyEvidence> result = [];
        foreach (YanflyUnresolvedDependency dependency in source.Dependencies)
        {
            bool resolved = aliases.TryGetValue(dependency.RequestedPluginId, out string? resolvedPluginId);
            if (resolved && resolvedPluginId!.Equals(source.PluginId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!resolved)
            {
                diagnostics.Add(new(
                    "YEP0008",
                    dependency.Kind == YanflyDependencyKind.Required
                        ? YanflyAuditSeverity.Warning
                        : YanflyAuditSeverity.Info,
                    $"Dependency token '{dependency.RequestedPluginId}' was not resolved to a supplied plugin filename or imported alias.",
                    source.PluginId,
                    source.RelativePath));
            }

            result.Add(new(
                dependency.RequestedPluginId,
                resolvedPluginId,
                dependency.Kind,
                dependency.Location,
                dependency.Evidence,
                resolved));
        }

        return result
            .OrderBy(dependency => dependency.Kind)
            .ThenBy(dependency => dependency.ResolvedPluginId ?? dependency.RequestedPluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<YanflyCoverageMode> ClassifyCoverage(YanflySourceAnalysis source)
    {
        HashSet<YanflyCoverageMode> modes = [];
        if (source.AuthoringSurfaces.Any(surface => surface is
                YanflyAuthoringSurface.DatabaseNote or
                YanflyAuthoringSurface.MapNote or
                YanflyAuthoringSurface.TilesetNote or
                YanflyAuthoringSurface.EventNote))
        {
            modes.Add(YanflyCoverageMode.TypedCatalog);
        }

        if (SpecializedEditors.Contains(source.PluginId) ||
            source.AuthoringSurfaces.Contains(YanflyAuthoringSurface.ActionSequence))
        {
            modes.Add(YanflyCoverageMode.SpecializedEditor);
        }

        if (source.AuthoringSurfaces.Any(surface => surface is
                YanflyAuthoringSurface.EventPageComment or
                YanflyAuthoringSurface.PluginCommand or
                YanflyAuthoringSurface.MoveRouteScript or
                YanflyAuthoringSurface.MessageTextCode or
                YanflyAuthoringSurface.SwitchNameMetadata or
                YanflyAuthoringSurface.VariableNameMetadata or
                YanflyAuthoringSurface.ScriptCall or
                YanflyAuthoringSurface.LunaticMode))
        {
            modes.Add(YanflyCoverageMode.ContextAssistant);
        }

        if (source.HasPluginParameters) modes.Add(YanflyCoverageMode.PluginConfiguration);
        if (modes.Count == 0) modes.Add(YanflyCoverageMode.RuntimeValidationOnly);
        return modes.OrderBy(mode => mode).ToList();
    }

    private static string DeterminePlannedPhase(YanflySourceAnalysis source)
    {
        if (Phase6Plugins.Contains(source.PluginId) || source.AuthoringSurfaces.Contains(YanflyAuthoringSurface.ActionSequence))
            return "Phase 6 — Battle AI and action sequences";
        if (source.Category.Equals("Skill & Status Menu Plugins", StringComparison.OrdinalIgnoreCase) || Phase7Plugins.Contains(source.PluginId))
            return "Phase 7 — Skills and progression";
        if (source.Category.Equals("Items & Equips Plugins", StringComparison.OrdinalIgnoreCase))
            return "Phase 8 — Items, equipment, shops, and currencies";
        if (Phase9Plugins.Contains(source.PluginId))
            return "Phase 9 — Enemies, states, auras, and formulas";
        if (source.Category.Equals("Event & Movement Plugins", StringComparison.OrdinalIgnoreCase) ||
            Phase10UtilityPlugins.Contains(source.PluginId) ||
            source.AuthoringSurfaces.Any(surface => surface is YanflyAuthoringSurface.MapNote or YanflyAuthoringSurface.TilesetNote or YanflyAuthoringSurface.EventNote or YanflyAuthoringSurface.EventPageComment))
        {
            return "Phase 10 — Maps, tilesets, events, comments, and commands";
        }

        if (source.Category.Equals("Core & Options Plugins", StringComparison.OrdinalIgnoreCase) ||
            source.AuthoringSurfaces.Any(surface => surface is YanflyAuthoringSurface.MessageTextCode or YanflyAuthoringSurface.SwitchNameMetadata or YanflyAuthoringSurface.VariableNameMetadata))
        {
            return "Phase 11 — Text codes and global authoring";
        }

        if (source.AuthoringSurfaces.Contains(YanflyAuthoringSurface.DatabaseNote))
            return "Phase 5 — Generic typed-tag coverage";
        return "Phase 3 — Installed-plugin intelligence";
    }

    private static YanflyBundleOccurrence ToOccurrence(YanflySourceAnalysis source) => new(
        source.RelativePath,
        source.FileName,
        source.Category,
        source.SizeBytes,
        source.Sha256,
        source.Version,
        source.IsFreeCollectionCopy);

    private static YanflyBundleSummary BuildSummary(
        int totalJavaScriptFiles,
        int ignoredSeparatorFiles,
        int pluginOccurrences,
        IReadOnlyList<YanflyPluginInventoryEntry> plugins)
    {
        int duplicateGroups = plugins.Count(plugin => plugin.Occurrences.Count > 1);
        int exactDuplicates = plugins.Count(plugin => plugin.DuplicateKind == YanflyDuplicateKind.ExactDuplicate);
        int conflicts = plugins.Count(plugin => plugin.DuplicateKind == YanflyDuplicateKind.ConflictingDuplicate);
        return new YanflyBundleSummary(
            totalJavaScriptFiles,
            ignoredSeparatorFiles,
            pluginOccurrences,
            plugins.Count,
            plugins.Count(plugin => plugin.SourceFamily == YanflySourceFamily.YanflyEnginePlugins),
            plugins.Count(plugin => plugin.SourceFamily != YanflySourceFamily.YanflyEnginePlugins),
            duplicateGroups,
            exactDuplicates,
            conflicts,
            plugins.GroupBy(plugin => plugin.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            Enum.GetValues<YanflyAuthoringSurface>()
                .ToDictionary(surface => surface, surface => plugins.Count(plugin => plugin.AuthoringSurfaces.Contains(surface))),
            Enum.GetValues<YanflyCoverageMode>()
                .ToDictionary(mode => mode, mode => plugins.Count(plugin => plugin.CoverageModes.Contains(mode))));
    }

    private static bool IsSeparatorFile(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return stem.Length > 0 && stem.All(character => character is '-' or '_' or ' ');
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed record CanonicalPlugin(
        YanflySourceAnalysis Selected,
        IReadOnlyList<YanflySourceAnalysis> All,
        YanflyDuplicateKind DuplicateKind,
        YanflyResolutionReason ResolutionReason);
}
