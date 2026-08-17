using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Selection;

namespace Playroma.Text.Generators.Stages;

/// <summary>
/// Expands recursively named weighted tables. References use @{tableName}; this syntax is
/// intentionally distinct from the Phase-3 Tracery-compatible #symbol# syntax.
/// </summary>
public sealed class NestedTableStage : ITextGenerationStage
{
    public const string StageKind = "phase1.nested-table";
    private static readonly Regex ReferencePattern = new("@\\{(?<name>[A-Za-z0-9_.-]+)\\}", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StageSettingsReader.TryGet(context.Stage, "tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Object)
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, "nested-table requires an object-valued 'tables' setting."));

        var tables = ReadTables(tablesElement);
        var origin = StageSettingsReader.GetString(context.Stage, "origin", "origin");
        var maxDepth = Math.Clamp(StageSettingsReader.GetInt(context.Stage, "maxDepth", 16), 1, 128);
        if (!tables.ContainsKey(origin)) return ValueTask.FromResult(StageSettingsReader.Invalid(context, $"nested-table origin '{origin}' does not exist."));

        try
        {
            var text = ExpandTable(origin, tables, context, 0, maxDepth, cancellationToken);
            context.Trace.Add(context.Stage.Id, "nested-table.complete", output: text, metadata: new Dictionary<string, string> { ["origin"] = origin });
            return ValueTask.FromResult(GenerationStageResult.Ok(StageUtilities.Write(context, text, "text", "nested-table")));
        }
        catch (InvalidOperationException ex)
        {
            return ValueTask.FromResult(StageSettingsReader.Invalid(context, ex.Message));
        }
    }

    private static string ExpandTable(string name, IReadOnlyDictionary<string, IReadOnlyList<WeightedText>> tables, GenerationStageContext context, int depth, int maxDepth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth >= maxDepth) throw new InvalidOperationException($"Nested table expansion exceeded maxDepth={maxDepth} while expanding '{name}'.");
        if (!tables.TryGetValue(name, out var entries) || entries.Count == 0) throw new InvalidOperationException($"Nested table '{name}' is missing or empty.");
        context.Budget.ConsumeCandidateAttempt();
        WeightedText selected;
        try { selected = WeightedSelector.Select(entries, x => x.Weight, context.Random); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException) { throw new InvalidOperationException($"Nested table '{name}' is invalid: {ex.Message}", ex); }
        context.Trace.Add(context.Stage.Id, "nested-table.pick", output: selected.Text, metadata: new Dictionary<string, string> { ["table"] = name, ["depth"] = depth.ToString() });

        return ReferencePattern.Replace(selected.Text, match =>
        {
            var child = match.Groups["name"].Value;
            return ExpandTable(child, tables, context, depth + 1, maxDepth, cancellationToken);
        });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<WeightedText>> ReadTables(JsonElement element)
    {
        var result = new Dictionary<string, IReadOnlyList<WeightedText>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in element.EnumerateObject())
        {
            if (table.Value.ValueKind != JsonValueKind.Array) continue;
            var entries = new List<WeightedText>();
            foreach (var item in table.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) { entries.Add(new WeightedText(item.GetString() ?? string.Empty)); continue; }
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) continue;
                var weight = item.TryGetProperty("weight", out var w) && w.TryGetDouble(out var parsed) ? parsed : 1.0;
                entries.Add(new WeightedText(text.GetString() ?? string.Empty, weight));
            }
            result[table.Name] = entries;
        }
        return result;
    }
}
