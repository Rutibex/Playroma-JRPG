using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags;

public enum PluginTagDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record PluginTagDiagnostic(
    string Code,
    PluginTagDiagnosticSeverity Severity,
    string Message,
    PluginTextSpan Span,
    PluginFeatureKey? Feature = null,
    string? ArgumentName = null);

public sealed record PluginTagValidationContext(
    PluginTagContext TagContext,
    IReadOnlyDictionary<string, InstalledPluginStatus> InstalledPlugins,
    bool PluginInventoryAvailable = true);

public interface IPluginTagValidator
{
    IEnumerable<PluginTagDiagnostic> Validate(PluginTextDocument document, PluginTagValidationContext context);
}

public sealed class PluginTagValidator
{
    private static readonly Regex NumberPattern = new(
        "-?[0-9]+(?:\\.[0-9]+)?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private readonly IReadOnlyList<IPluginTagValidator> _additionalValidators;

    public PluginTagValidator(IEnumerable<IPluginTagValidator>? additionalValidators = null)
    {
        _additionalValidators = additionalValidators?.ToArray() ?? [];
    }

    public IReadOnlyList<PluginTagDiagnostic> Validate(PluginTextDocument document, PluginTagValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        List<PluginTagDiagnostic> diagnostics = [];
        ValidateMalformed(document, diagnostics);
        ValidateOccurrences(document, context, diagnostics);
        foreach (IPluginTagValidator validator in _additionalValidators)
            diagnostics.AddRange(validator.Validate(document, context));
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Span.Start)
            .ThenByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateMalformed(PluginTextDocument document, ICollection<PluginTagDiagnostic> diagnostics)
    {
        foreach (MalformedTagNode malformed in document.Nodes.OfType<MalformedTagNode>())
        {
            diagnostics.Add(new PluginTagDiagnostic(
                "PT1001",
                PluginTagDiagnosticSeverity.Error,
                malformed.Message,
                malformed.Span,
                malformed.ExpectedDefinition?.Key));
        }
    }

    private static void ValidateOccurrences(
        PluginTextDocument document,
        PluginTagValidationContext context,
        ICollection<PluginTagDiagnostic> diagnostics)
    {
        List<(PluginTextNode Node, PluginFeatureDefinition Definition, PluginFeatureValues Values)> occurrences = [];
        foreach (PluginTextNode node in document.Nodes)
        {
            switch (node)
            {
                case RecognizedTagNode tag:
                    occurrences.Add((tag, tag.Definition, tag.Values));
                    break;
                case RecognizedBlockNode block:
                    occurrences.Add((block, block.Definition, block.Values));
                    break;
            }
        }

        foreach ((PluginTextNode node, PluginFeatureDefinition definition, PluginFeatureValues values) in occurrences)
        {
            if (definition.Surface != context.TagContext.Surface)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1101", PluginTagDiagnosticSeverity.Error,
                    $"{definition.DisplayName} belongs to {definition.Surface}, not {context.TagContext.Surface}.", node.Span, definition.Key));
            }

            if (definition.DocumentKinds.Count > 0 &&
                !definition.DocumentKinds.Contains(PluginDocumentKind.Unknown) &&
                !definition.DocumentKinds.Contains(context.TagContext.DocumentKind))
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1102", PluginTagDiagnosticSeverity.Error,
                    $"{definition.DisplayName} is not valid for {context.TagContext.DocumentKind}.", node.Span, definition.Key));
            }

            ValidateArguments(node, definition, values, diagnostics);
            if (context.PluginInventoryAvailable)
                ValidatePlugins(node, definition, context.InstalledPlugins, diagnostics);
        }

        foreach (IGrouping<PluginFeatureKey, (PluginTextNode Node, PluginFeatureDefinition Definition, PluginFeatureValues Values)> group in occurrences.GroupBy(item => item.Definition.Key))
        {
            PluginFeatureDefinition definition = group.First().Definition;
            if (definition.Multiplicity == PluginFeatureMultiplicity.Singleton && group.Count() > 1)
            {
                foreach (var duplicate in group.Skip(1))
                {
                    PluginTextNode node = duplicate.Node;
                    diagnostics.Add(new PluginTagDiagnostic("PT1201", PluginTagDiagnosticSeverity.Error,
                        $"{definition.DisplayName} may appear only once.", node.Span, definition.Key));
                }
            }
        }

        Dictionary<string, List<(PluginTextNode Node, PluginFeatureDefinition Definition)>> conflicts = new(StringComparer.OrdinalIgnoreCase);
        foreach ((PluginTextNode node, PluginFeatureDefinition definition, _) in occurrences)
        {
            foreach (string conflictGroup in definition.EffectiveConflictGroups)
            {
                if (!conflicts.TryGetValue(conflictGroup, out List<(PluginTextNode, PluginFeatureDefinition)>? list))
                    conflicts[conflictGroup] = list = [];
                list.Add((node, definition));
            }
        }

        foreach ((string groupName, List<(PluginTextNode Node, PluginFeatureDefinition Definition)> members) in conflicts)
        {
            PluginFeatureKey[] distinct = members.Select(member => member.Definition.Key).Distinct().ToArray();
            if (distinct.Length <= 1) continue;
            foreach ((PluginTextNode node, PluginFeatureDefinition definition) in members)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1202", PluginTagDiagnosticSeverity.Warning,
                    $"{definition.DisplayName} conflicts with another feature in group '{groupName}'.", node.Span, definition.Key));
            }
        }
    }

    private static void ValidateArguments(
        PluginTextNode node,
        PluginFeatureDefinition definition,
        PluginFeatureValues values,
        ICollection<PluginTagDiagnostic> diagnostics)
    {
        foreach (PluginArgumentDefinition argument in definition.Arguments)
        {
            bool exists = values.TryGetValue(argument.Name, out PluginValue value);
            if (!exists || string.IsNullOrWhiteSpace(value.Text))
            {
                if (argument.IsRequired)
                {
                    diagnostics.Add(new PluginTagDiagnostic("PT1301", PluginTagDiagnosticSeverity.Error,
                        $"{definition.DisplayName} requires {argument.DisplayName}.", node.Span, definition.Key, argument.Name));
                }
                continue;
            }

            if (argument.Choices is { Count: > 0 } && !argument.Choices.Contains(value.Text, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1302", PluginTagDiagnosticSeverity.Error,
                    $"'{value.Text}' is not a valid value for {argument.DisplayName}.", node.Span, definition.Key, argument.Name));
            }

            if (argument.Kind == PluginArgumentKind.Boolean && !IsBoolean(value.Text))
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1303", PluginTagDiagnosticSeverity.Error,
                    $"{argument.DisplayName} must be true or false.", node.Span, definition.Key, argument.Name));
                continue;
            }

            if (!TryGetNumericValues(argument.Kind, value.Text, out IReadOnlyList<decimal> numericValues))
            {
                if (IsNumericKind(argument.Kind))
                {
                    diagnostics.Add(new PluginTagDiagnostic("PT1303", PluginTagDiagnosticSeverity.Error,
                        $"{argument.DisplayName} has an invalid numeric or ID format.", node.Span, definition.Key, argument.Name));
                }
                continue;
            }

            if ((argument.Minimum is not null && numericValues.Any(numeric => numeric < argument.Minimum.Value)) ||
                (argument.Maximum is not null && numericValues.Any(numeric => numeric > argument.Maximum.Value)))
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1304", PluginTagDiagnosticSeverity.Error,
                    $"{argument.DisplayName} is outside its allowed range.", node.Span, definition.Key, argument.Name));
            }
        }
    }

    private static void ValidatePlugins(
        PluginTextNode node,
        PluginFeatureDefinition definition,
        IReadOnlyDictionary<string, InstalledPluginStatus> installed,
        ICollection<PluginTagDiagnostic> diagnostics)
    {
        foreach (string requiredPlugin in definition.EffectiveRequiredPlugins)
        {
            InstalledPluginStatus? status = FindStatus(installed, requiredPlugin);
            if (status is null || !status.IsPresent)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1401", PluginTagDiagnosticSeverity.Error,
                    $"Required plugin is missing: {requiredPlugin}.", node.Span, definition.Key));
            }
            else if (!status.IsEnabled)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1402", PluginTagDiagnosticSeverity.Warning,
                    $"Required plugin is installed but disabled: {requiredPlugin}.", node.Span, definition.Key));
            }
        }

        foreach (string recommendedPlugin in definition.EffectiveRecommendedPlugins)
        {
            InstalledPluginStatus? status = FindStatus(installed, recommendedPlugin);
            if (status is null || !status.IsPresent || !status.IsEnabled)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1403", PluginTagDiagnosticSeverity.Information,
                    $"Recommended companion plugin is not enabled: {recommendedPlugin}.", node.Span, definition.Key));
            }
        }

        ValidateCompatibility(node, definition, installed, diagnostics);
    }

    private static void ValidateCompatibility(
        PluginTextNode node,
        PluginFeatureDefinition definition,
        IReadOnlyDictionary<string, InstalledPluginStatus> installed,
        ICollection<PluginTagDiagnostic> diagnostics)
    {
        PluginVersionRange? compatibility = definition.Compatibility;
        if (compatibility is null) return;
        InstalledPluginStatus? owner = FindStatus(installed, definition.PluginId);
        if (owner is null || !owner.IsPresent) return;

        if (!string.IsNullOrWhiteSpace(owner.Version))
        {
            if (!string.IsNullOrWhiteSpace(compatibility.MinimumInclusive) &&
                CompareVersions(owner.Version, compatibility.MinimumInclusive) < 0)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1410", PluginTagDiagnosticSeverity.Warning,
                    $"{definition.DisplayName} was verified for {definition.PluginId} {compatibility.MinimumInclusive} or newer; installed version is {owner.Version}.",
                    node.Span, definition.Key));
            }

            if (!string.IsNullOrWhiteSpace(compatibility.MaximumInclusive) &&
                CompareVersions(owner.Version, compatibility.MaximumInclusive) > 0)
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1411", PluginTagDiagnosticSeverity.Warning,
                    $"{definition.DisplayName} was verified through {definition.PluginId} {compatibility.MaximumInclusive}; installed version is {owner.Version}.",
                    node.Span, definition.Key));
            }

            if (compatibility.VerifiedVersions is { Count: > 0 } &&
                !compatibility.VerifiedVersions.Contains(owner.Version, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new PluginTagDiagnostic("PT1412", PluginTagDiagnosticSeverity.Information,
                    $"Installed version {owner.Version} is not in the explicitly verified version list for {definition.DisplayName}.",
                    node.Span, definition.Key));
            }
        }

        if (!string.IsNullOrWhiteSpace(owner.SourceFingerprint) &&
            compatibility.SourceFingerprints is { Count: > 0 } &&
            !compatibility.SourceFingerprints.Contains(owner.SourceFingerprint, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(new PluginTagDiagnostic("PT1413", PluginTagDiagnosticSeverity.Warning,
                $"The installed source fingerprint for {definition.PluginId} has not been verified for {definition.DisplayName}.",
                node.Span, definition.Key));
        }
    }

    private static bool IsBoolean(string value) =>
        bool.TryParse(value.Trim(), out _) || value.Trim() is "0" or "1";

    private static bool IsNumericKind(PluginArgumentKind kind) => kind is
        PluginArgumentKind.Integer or
        PluginArgumentKind.Decimal or
        PluginArgumentKind.Percentage or
        PluginArgumentKind.DatabaseId or
        PluginArgumentKind.DatabaseIdList or
        PluginArgumentKind.DatabaseIdRange or
        PluginArgumentKind.SwitchId or
        PluginArgumentKind.VariableId or
        PluginArgumentKind.CommonEventId or
        PluginArgumentKind.AnimationId or
        PluginArgumentKind.StateId or
        PluginArgumentKind.SkillId or
        PluginArgumentKind.ItemId or
        PluginArgumentKind.WeaponId or
        PluginArgumentKind.ArmorId or
        PluginArgumentKind.ClassId or
        PluginArgumentKind.ActorId or
        PluginArgumentKind.EnemyId or
        PluginArgumentKind.RegionId or
        PluginArgumentKind.IconId;

    private static bool TryGetNumericValues(PluginArgumentKind kind, string text, out IReadOnlyList<decimal> values)
    {
        if (!IsNumericKind(kind))
        {
            values = [];
            return true;
        }

        string normalized = text.Trim().TrimEnd('%');
        if (kind is PluginArgumentKind.Decimal or PluginArgumentKind.Percentage)
        {
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
            {
                values = [number];
                return true;
            }
            values = [];
            return false;
        }

        MatchCollection matches = NumberPattern.Matches(normalized);
        int minimumCount = kind == PluginArgumentKind.DatabaseIdRange ? 2 : 1;
        if (matches.Count < minimumCount)
        {
            values = [];
            return false;
        }

        if (kind is not PluginArgumentKind.DatabaseIdList and not PluginArgumentKind.DatabaseIdRange && matches.Count != 1)
        {
            values = [];
            return false;
        }

        List<decimal> parsed = [];
        foreach (Match match in matches)
        {
            if (!decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
            {
                values = [];
                return false;
            }
            parsed.Add(number);
        }
        values = parsed;
        return true;
    }

    private static InstalledPluginStatus? FindStatus(IReadOnlyDictionary<string, InstalledPluginStatus> statuses, string pluginId)
    {
        if (statuses.TryGetValue(pluginId, out InstalledPluginStatus? direct)) return direct;
        return statuses.Values.FirstOrDefault(status => string.Equals(status.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private static int CompareVersions(string left, string right)
    {
        int[] leftParts = ParseVersionParts(left);
        int[] rightParts = ParseVersionParts(right);
        int length = Math.Max(leftParts.Length, rightParts.Length);
        for (int index = 0; index < length; index++)
        {
            int leftPart = index < leftParts.Length ? leftParts[index] : 0;
            int rightPart = index < rightParts.Length ? rightParts[index] : 0;
            int comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int[] ParseVersionParts(string value) => Regex.Matches(value, "[0-9]+")
        .Cast<Match>()
        .Select(match => int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int part) ? part : 0)
        .ToArray();
}
