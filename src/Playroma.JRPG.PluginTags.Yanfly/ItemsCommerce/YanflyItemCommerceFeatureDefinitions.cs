using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public static class YanflyItemCommerceFeatureDefinitions
{
    public const string DesignerEditorId = "yep.item-commerce-designer";

    private static readonly IReadOnlyDictionary<string, ItemCommerceArea> PluginAreas =
        new Dictionary<string, ItemCommerceArea>(StringComparer.OrdinalIgnoreCase)
        {
            ["YEP_ItemCore"] = ItemCommerceArea.CoreItems,
            ["YEP_EquipCore"] = ItemCommerceArea.EquipmentSlots,
            ["YEP_ItemSynthesis"] = ItemCommerceArea.Synthesis,
            ["YEP_ShopMenuCore"] = ItemCommerceArea.ShopPrices,
            ["YEP_WeaponAnimation"] = ItemCommerceArea.WeaponVisuals,
            ["YEP_WeaponUnleash"] = ItemCommerceArea.WeaponUnleash,
            ["YEP_X_ArmorScaling"] = ItemCommerceArea.ArmorScaling,
            ["YEP_X_AttachAugments"] = ItemCommerceArea.Augments,
            ["YEP_X_ChangeBattleEquip"] = ItemCommerceArea.BattleEquipment,
            ["YEP_X_CondShopPrices"] = ItemCommerceArea.ShopPrices,
            ["YEP_X_EquipRequirements"] = ItemCommerceArea.EquipRequirements,
            ["YEP_X_HideShowShopItems"] = ItemCommerceArea.ShopVisibility,
            ["YEP_X_ItemCategories"] = ItemCommerceArea.CategoriesAndPictures,
            ["YEP_X_ItemDisassemble"] = ItemCommerceArea.Disassembly,
            ["YEP_X_ItemDiscard"] = ItemCommerceArea.RenameAndDiscard,
            ["YEP_X_ItemDurability"] = ItemCommerceArea.Durability,
            ["YEP_X_ItemPictureImg"] = ItemCommerceArea.CategoriesAndPictures,
            ["YEP_X_ItemRename"] = ItemCommerceArea.RenameAndDiscard,
            ["YEP_X_ItemRequirements"] = ItemCommerceArea.ItemRequirements,
            ["YEP_X_ItemUpgradeSlots"] = ItemCommerceArea.UpgradeSlots,
            ["YEP_X_MoreCurrencies"] = ItemCommerceArea.Currencies
        };

    public static IReadOnlySet<string> CoveredPluginIds { get; } =
        PluginAreas.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<PluginFeatureDefinition> ApplyCorrections(IEnumerable<PluginFeatureDefinition> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (PluginFeatureDefinition feature in features)
        {
            if (feature.Surface != PluginAuthoringSurface.DatabaseNote || !PluginAreas.ContainsKey(feature.PluginId))
            {
                yield return feature;
                continue;
            }

            ItemCommerceArea area = Classify(feature) ?? PluginAreas[feature.PluginId];
            yield return feature with
            {
                SpecializedEditorId = DesignerEditorId,
                SearchTerms = feature.EffectiveSearchTerms
                    .Concat(["items equipment shops currencies", "item commerce", FriendlyArea(area), "item designer"])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }

    public static ItemCommerceArea? Classify(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!PluginAreas.TryGetValue(feature.PluginId, out ItemCommerceArea fallback)) return null;
        string text = string.Join(" ", feature.DisplayName, feature.Summary, feature.Syntax.CanonicalTemplate,
            string.Join(" ", feature.EffectiveSearchTerms)).ToLowerInvariant();

        if (feature.Complexity == PluginFeatureComplexity.AdvancedCode || text.Contains(" eval") || text.Contains("custom "))
            return ItemCommerceArea.Advanced;
        if (text.Contains("durability") || text.Contains("repair") || text.Contains("break sound") || text.Contains("unbreakable"))
            return ItemCommerceArea.Durability;
        if (text.Contains("augment")) return ItemCommerceArea.Augments;
        if (text.Contains("upgrade") || text.Contains("slot variance")) return ItemCommerceArea.UpgradeSlots;
        if (text.Contains("synthesis") || text.Contains("recipe") || text.Contains("ingredient")) return ItemCommerceArea.Synthesis;
        if (text.Contains("disassembl")) return ItemCommerceArea.Disassembly;
        if (text.Contains("equip requirement")) return ItemCommerceArea.EquipRequirements;
        if (text.Contains("enable requirement") || text.Contains("item requirement")) return ItemCommerceArea.ItemRequirements;
        if (text.Contains("rename") || text.Contains("discard")) return ItemCommerceArea.RenameAndDiscard;
        if (text.Contains("menu category") || text.Contains("picture") || text.Contains("image") && feature.PluginId.Equals("YEP_X_ItemPictureImg", StringComparison.OrdinalIgnoreCase))
            return ItemCommerceArea.CategoriesAndPictures;
        if (text.Contains("equip slot") || text.Contains("custom parameter")) return ItemCommerceArea.EquipmentSlots;
        if (text.Contains("battle equip")) return ItemCommerceArea.BattleEquipment;
        if (text.Contains("shop hide") || text.Contains("shop show")) return ItemCommerceArea.ShopVisibility;
        if (text.Contains("currency") || text.Contains("proxy buy") || text.Contains("variable") && text.Contains("buy price") || text.Contains("sell price") && feature.PluginId.Equals("YEP_X_MoreCurrencies", StringComparison.OrdinalIgnoreCase))
            return ItemCommerceArea.Currencies;
        if (text.Contains("price") || text.Contains("can sell") || text.Contains("cannot sell")) return ItemCommerceArea.ShopPrices;
        if (text.Contains("armor penetration") || text.Contains("armor reduction") || text.Contains("armor scaling") || text.Contains("base armor") || text.Contains("armor rate"))
            return ItemCommerceArea.ArmorScaling;
        if (text.Contains("weapon image") || text.Contains("weapon motion") || text.Contains("weapon animation") || text.Contains("weapon hue"))
            return ItemCommerceArea.WeaponVisuals;
        if (text.Contains("unleash") || text.Contains("replace attack") || text.Contains("replace guard") || text.Contains("attack text") || text.Contains("guard text") || text.Contains("command text"))
            return ItemCommerceArea.WeaponUnleash;
        if (text.Contains("independent") || text.Contains("random variance") || text.Contains("variance stock"))
            return ItemCommerceArea.IndependentItems;
        return fallback;
    }

    public static void RegisterSpecializedEditor(PluginSpecializedEditorRegistry registry, PluginTagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlySet<PluginFeatureKey> features = catalog.Features
            .Where(feature => feature.SpecializedEditorId?.Equals(DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true)
            .Select(feature => feature.Key).ToHashSet();
        registry.Register(new(DesignerEditorId, "Items, Equipment, Shops & Currencies",
            new HashSet<PluginAuthoringSurface> { PluginAuthoringSurface.DatabaseNote }, features, true,
            "Unified Item Core, equipment, durability, augments, upgrades, crafting, shops, currencies, armor and weapon authoring."), replace: true);
    }

    public static IReadOnlyList<ItemCommercePreset> CreatePresets(PluginTagCatalog catalog, PluginDocumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        List<ItemCommercePreset> result = [];
        AddPreset(result, catalog, kind, "unbreakable-equipment", "Unbreakable Equipment",
            "Prevent a weapon or armor from losing durability.", [("YEP_X_ItemDurability", "<Unbreakable>", [])]);
        AddPreset(result, catalog, kind, "durable-equipment", "100 Durability",
            "Give equipment a starting durability of 100.", [("YEP_X_ItemDurability", "<Durability:", ["100"])]);
        AddPreset(result, catalog, kind, "upgradeable-equipment", "Three Upgrade Slots",
            "Give an item three upgrade slots.", [("YEP_X_ItemUpgradeSlots", "<Upgrade Slots:", ["3"])]);
        AddPreset(result, catalog, kind, "rename-and-discard", "Player-Customizable Item",
            "Allow the player to rename and discard this item.", [("YEP_X_ItemRename", "<Can Rename>", []), ("YEP_X_ItemDiscard", "<Can Discard>", [])]);
        AddPreset(result, catalog, kind, "rare-category", "Rare Item Category",
            "Place an item in a custom Rare menu category.", [("YEP_X_ItemCategories", "<Menu Category: {value1}>", ["Rare"])]);
        AddPreset(result, catalog, kind, "unsellable-item", "Cannot Sell",
            "Prevent the shop from buying this item.", [("YEP_ShopMenuCore", "<Cannot Sell>", [])]);
        AddPreset(result, catalog, kind, "base-armor", "Armor Scaling Base",
            "Use the database defense value as base armor for scaling.", [("YEP_X_ArmorScaling", "<Base Armor>", [])]);
        AddPreset(result, catalog, kind, "swinging-weapon", "Swinging Weapon Motion",
            "Display the weapon using the swing sideview motion.", [("YEP_WeaponAnimation", "<Weapon Motion: swing>", [])]);
        return result;
    }

    private static void AddPreset(
        List<ItemCommercePreset> result,
        PluginTagCatalog catalog,
        PluginDocumentKind kind,
        string id,
        string name,
        string description,
        IReadOnlyList<(string PluginId, string SyntaxContains, string[] Values)> selectors)
    {
        List<ItemCommercePresetStep> steps = [];
        foreach ((string pluginId, string syntaxContains, string[] values) in selectors)
        {
            PluginFeatureDefinition? feature = catalog.Features.FirstOrDefault(candidate =>
                candidate.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Surface == PluginAuthoringSurface.DatabaseNote &&
                candidate.Syntax.CanonicalTemplate.Contains(syntaxContains, StringComparison.OrdinalIgnoreCase) &&
                (candidate.DocumentKinds.Count == 0 || candidate.DocumentKinds.Contains(PluginDocumentKind.Unknown) || candidate.DocumentKinds.Contains(kind)));
            if (feature is null) return;
            List<KeyValuePair<string, string>> arguments = [];
            for (int index = 0; index < feature.Arguments.Count; index++)
                arguments.Add(new(feature.Arguments[index].Name,
                    index < values.Length ? values[index] : feature.Arguments[index].DefaultValue ?? string.Empty));
            steps.Add(new(feature.Key, PluginFeatureValues.FromStrings(arguments)));
        }
        result.Add(new(id, name, description, new HashSet<PluginDocumentKind> { kind }, steps,
            [name, description, "Yanfly", "items equipment shops currencies"]));
    }

    public static string FriendlyArea(ItemCommerceArea area) => area switch
    {
        ItemCommerceArea.CoreItems => "Item Core",
        ItemCommerceArea.IndependentItems => "Independent Items",
        ItemCommerceArea.UpgradeSlots => "Upgrade Slots",
        ItemCommerceArea.ItemRequirements => "Item Requirements",
        ItemCommerceArea.EquipRequirements => "Equip Requirements",
        ItemCommerceArea.RenameAndDiscard => "Rename & Discard",
        ItemCommerceArea.CategoriesAndPictures => "Categories & Pictures",
        ItemCommerceArea.EquipmentSlots => "Equipment Slots",
        ItemCommerceArea.BattleEquipment => "Battle Equipment",
        ItemCommerceArea.ShopPrices => "Shop Prices",
        ItemCommerceArea.ShopVisibility => "Shop Visibility",
        ItemCommerceArea.ArmorScaling => "Armor Scaling",
        ItemCommerceArea.WeaponVisuals => "Weapon Visuals",
        ItemCommerceArea.WeaponUnleash => "Weapon Unleash",
        _ => area.ToString()
    };
}
