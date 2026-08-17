using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyPluginIntelligenceProfileFactory
{
    public PluginIntelligenceProfile CreateEmbedded() => Create(YanflyInventorySerializer.LoadEmbeddedPhase0Inventory());

    public PluginIntelligenceProfile Create(YanflyBundleInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        KnownPluginProfile[] plugins = inventory.Plugins.Select(CreatePluginProfile).ToArray();
        PluginDependencyRule[] dependencies = inventory.Plugins
            .SelectMany(plugin => plugin.Dependencies
                .Where(dependency => dependency.IsResolved && !string.IsNullOrWhiteSpace(dependency.ResolvedPluginId))
                .Select(dependency => new PluginDependencyRule(
                    plugin.PluginId,
                    dependency.ResolvedPluginId!,
                    dependency.Kind == YanflyDependencyKind.Required
                        ? PluginDependencyStrength.Required
                        : PluginDependencyStrength.Recommended,
                    dependency.Evidence)))
            .DistinctBy(rule => (rule.PluginId.ToUpperInvariant(), rule.RequiredPluginId.ToUpperInvariant(), rule.Strength))
            .ToArray();

        List<PluginLoadOrderRule> order = dependencies
            .Where(rule => rule.Strength == PluginDependencyStrength.Required)
            .Select(rule => new PluginLoadOrderRule(
                rule.RequiredPluginId,
                rule.PluginId,
                $"{rule.RequiredPluginId} must load above {rule.PluginId} because the latter depends on it."))
            .ToList();
        order.AddRange(CreateCuratedLoadOrderRules());

        PluginConflictRule[] conflicts =
        [
            new("battle-system", ["YEP_X_BattleSysATB", "YEP_X_BattleSysCTB", "YEP_X_BattleSysSTB"],
                "Only one Yanfly alternate battle system (ATB, CTB, or STB) should be enabled at a time.")
        ];

        return new PluginIntelligenceProfile(
            inventory.ProviderId,
            plugins,
            dependencies,
            order.DistinctBy(rule => (rule.BeforePluginId.ToUpperInvariant(), rule.AfterPluginId.ToUpperInvariant())).ToArray(),
            conflicts);
    }

    private static KnownPluginProfile CreatePluginProfile(YanflyPluginInventoryEntry plugin)
    {
        IEnumerable<IGrouping<string, YanflyBundleOccurrence>> groups = plugin.Occurrences
            .GroupBy(occurrence => occurrence.Version ?? plugin.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        KnownPluginVersion[] versions = groups.Select(group => new KnownPluginVersion(
            string.IsNullOrWhiteSpace(group.Key) ? null : group.Key,
            group.Select(occurrence => occurrence.Sha256)
                .Append(plugin.CanonicalSha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray())).ToArray();
        if (versions.Length == 0)
            versions = [new KnownPluginVersion(plugin.Version, [plugin.CanonicalSha256])];

        string[] aliases = plugin.ImportedAliases
            .Append(plugin.FileName)
            .Append(Path.GetFileNameWithoutExtension(plugin.FileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new KnownPluginProfile(plugin.PluginId, plugin.DisplayName, YanflyProviderIdentity.ProviderId, aliases, versions);
    }

    private static IEnumerable<PluginLoadOrderRule> CreateCuratedLoadOrderRules()
    {
        yield return Rule("YEP_CoreEngine", "YEP_BattleEngineCore");
        yield return Rule("YEP_CoreEngine", "YEP_ItemCore");
        yield return Rule("YEP_CoreEngine", "YEP_SkillCore");
        yield return Rule("YEP_CoreEngine", "YEP_EquipCore");
        yield return Rule("YEP_CoreEngine", "YEP_SaveCore");
        yield return Rule("YEP_BattleEngineCore", "YEP_BuffsStatesCore");
        yield return Rule("YEP_BattleEngineCore", "YEP_X_ActSeqPack1");
        yield return Rule("YEP_BattleEngineCore", "YEP_X_ActSeqPack2");
        yield return Rule("YEP_BattleEngineCore", "YEP_X_ActSeqPack3");
        yield return Rule("YEP_ItemCore", "YEP_X_ItemUpgradeSlots");
        yield return Rule("YEP_ItemCore", "YEP_X_AttachAugments");
        yield return Rule("YEP_ItemCore", "YEP_X_ItemDurability");
        yield return Rule("YEP_SkillCore", "YEP_X_SkillCooldowns");
        yield return Rule("YEP_AutoPassiveStates", "YEP_X_PassiveAuras");
        yield return Rule("YEP_MessageCore", "YEP_X_ExtMesPack1");
        yield return Rule("YEP_MessageCore", "YEP_X_ExtMesPack2");
        yield return Rule("YEP_ClassChangeCore", "YEP_X_Subclass");
        yield return Rule("YEP_SaveCore", "YEP_X_NewGamePlus");

        static PluginLoadOrderRule Rule(string before, string after) => new(
            before, after, $"{before} should load above {after} according to the YEP extension/core relationship.");
    }
}
