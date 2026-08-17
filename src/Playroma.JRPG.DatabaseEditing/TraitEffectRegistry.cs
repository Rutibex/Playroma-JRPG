using System.Globalization;
using System.Text.Json;

namespace Playroma.JRPG.DatabaseEditing;

public interface IStructuredCodeDefinition
{
    int Code { get; }
    string DisplayName { get; }
    string FormatSummary(int dataId, double value1, double value2 = 0);
}

public sealed record StructuredCodeDefinition(
    int Code,
    string DisplayName,
    Func<int, double, double, string> Formatter) : IStructuredCodeDefinition
{
    public string FormatSummary(int dataId, double value1, double value2 = 0) => Formatter(dataId, value1, value2);
}

public sealed class StructuredCodeRegistry
{
    private readonly Dictionary<int, IStructuredCodeDefinition> _definitions = [];

    public IReadOnlyCollection<IStructuredCodeDefinition> Definitions => _definitions.Values;

    public void Register(IStructuredCodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Code, definition)) throw new InvalidOperationException($"Code {definition.Code} is already registered.");
    }

    public IStructuredCodeDefinition? Find(int code) => _definitions.TryGetValue(code, out IStructuredCodeDefinition? definition) ? definition : null;

    public string FormatRaw(string rawJson, string value2Property = "value2")
    {
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement root = document.RootElement;
        int code = root.TryGetProperty("code", out JsonElement codeElement) && codeElement.TryGetInt32(out int parsedCode) ? parsedCode : -1;
        int dataId = root.TryGetProperty("dataId", out JsonElement dataElement) && dataElement.TryGetInt32(out int parsedData) ? parsedData : 0;
        double value1 = root.TryGetProperty("value", out JsonElement valueElement) && valueElement.TryGetDouble(out double parsedValue)
            ? parsedValue
            : root.TryGetProperty("value1", out JsonElement value1Element) && value1Element.TryGetDouble(out parsedValue) ? parsedValue : 0;
        double value2 = root.TryGetProperty(value2Property, out JsonElement value2Element) && value2Element.TryGetDouble(out double parsedValue2) ? parsedValue2 : 0;
        IStructuredCodeDefinition? definition = Find(code);
        return definition is null
            ? $"Unknown Code {code} — Data {dataId}; Values {value1.ToString(CultureInfo.InvariantCulture)}, {value2.ToString(CultureInfo.InvariantCulture)}"
            : definition.FormatSummary(dataId, value1, value2);
    }
}

public static class MvTraitDefinitions
{
    public static StructuredCodeRegistry Create()
    {
        StructuredCodeRegistry registry = new();
        void Add(int code, string name, Func<int, double, double, string>? formatter = null) => registry.Register(new StructuredCodeDefinition(code, name, formatter ?? ((id, v1, _) => $"{name}: {id} = {Percent(v1)}")));
        Add(11, "Element Rate");
        Add(12, "Debuff Rate");
        Add(13, "State Rate");
        Add(14, "State Resist", (id, _, _) => $"State Resist: {id}");
        Add(21, "Parameter", (id, value, _) => $"Parameter {id}: {Percent(value)}");
        Add(22, "Ex-Parameter", (id, value, _) => $"Ex-Parameter {id}: {SignedPercent(value)}");
        Add(23, "Sp-Parameter", (id, value, _) => $"Sp-Parameter {id}: {Percent(value)}");
        Add(31, "Attack Element", (id, _, _) => $"Attack Element: {id}");
        Add(32, "Attack State", (id, value, _) => $"Attack State {id}: {Percent(value)}");
        Add(33, "Attack Speed", (_, value, _) => $"Attack Speed: {value:+0;-0;0}");
        Add(34, "Attack Times", (_, value, _) => $"Attack Times: +{value:0.##}");
        Add(41, "Add Skill Type", (id, _, _) => $"Add Skill Type: {id}");
        Add(42, "Seal Skill Type", (id, _, _) => $"Seal Skill Type: {id}");
        Add(43, "Add Skill", (id, _, _) => $"Add Skill: {id}");
        Add(44, "Seal Skill", (id, _, _) => $"Seal Skill: {id}");
        Add(51, "Equip Weapon", (id, _, _) => $"Equip Weapon Type: {id}");
        Add(52, "Equip Armor", (id, _, _) => $"Equip Armor Type: {id}");
        Add(53, "Lock Equipment", (id, _, _) => $"Lock Equipment Slot: {id}");
        Add(54, "Seal Equipment", (id, _, _) => $"Seal Equipment Slot: {id}");
        Add(55, "Slot Type", (id, _, _) => id == 1 ? "Dual Wield" : "Normal Slots");
        Add(61, "Action Times", (_, value, _) => $"Action Times +: {Percent(value)}");
        Add(62, "Special Flag", (id, _, _) => $"Special Flag: {id}");
        Add(63, "Collapse Effect", (id, _, _) => $"Collapse Effect: {id}");
        Add(64, "Party Ability", (id, _, _) => $"Party Ability: {id}");
        return registry;
    }

    private static string Percent(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string SignedPercent(double value) => (value * 100).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";
}

public static class MvEffectDefinitions
{
    public static StructuredCodeRegistry Create()
    {
        StructuredCodeRegistry registry = new();
        void Add(int code, string name, Func<int, double, double, string> formatter) => registry.Register(new StructuredCodeDefinition(code, name, formatter));
        Add(11, "Recover HP", (_, v1, v2) => $"Recover HP: {Percent(v1)} + {v2:0.##}");
        Add(12, "Recover MP", (_, v1, v2) => $"Recover MP: {Percent(v1)} + {v2:0.##}");
        Add(13, "Gain TP", (_, v1, _) => $"Gain TP: {v1:0.##}");
        Add(21, "Add State", (id, v1, _) => $"Add State {id}: {Percent(v1)}");
        Add(22, "Remove State", (id, v1, _) => $"Remove State {id}: {Percent(v1)}");
        Add(31, "Add Buff", (id, v1, _) => $"Add Buff {id}: {v1:0} turns");
        Add(32, "Add Debuff", (id, v1, _) => $"Add Debuff {id}: {v1:0} turns");
        Add(33, "Remove Buff", (id, _, _) => $"Remove Buff: {id}");
        Add(34, "Remove Debuff", (id, _, _) => $"Remove Debuff: {id}");
        Add(41, "Special Effect", (id, _, _) => $"Special Effect: {id}");
        Add(42, "Grow", (id, v1, _) => $"Grow Parameter {id}: {v1:+0;-0;0}");
        Add(43, "Learn Skill", (id, _, _) => $"Learn Skill: {id}");
        Add(44, "Common Event", (id, _, _) => $"Common Event: {id}");
        return registry;
    }

    private static string Percent(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
