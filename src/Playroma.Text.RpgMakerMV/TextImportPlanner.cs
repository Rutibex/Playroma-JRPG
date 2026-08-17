using Playroma.Text.Core.Generation;

namespace Playroma.Text.RpgMakerMV;

/// <summary>Pure planning/validation for multi-field RPG Maker MV text imports.</summary>
public static class TextImportPlanner
{
    public static TextImportPlan Build(
        TextRecordTargetDescriptor target,
        GeneratedTextBundle bundle,
        IReadOnlyList<TextImportFieldSelection>? selections = null,
        TextImportRecordCreation? creation = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bundle);
        var choices = selections?.Where(x => x.Include).ToArray();
        if (choices is null || choices.Length == 0)
        {
            TextTargetDescriptor? selected = target.SelectedField;
            if (selected is null)
                return new TextImportPlan("Import generated text", [], [new("PTXTMV0100", "The target contains no writable text fields.", true)], creation);
            choices = [new TextImportFieldSelection(selected.SemanticField, true, TextImportMode.Replace)];
        }

        var changes = new List<TextImportChange>();
        var issues = new List<TextImportValidationIssue>();
        foreach (TextImportFieldSelection choice in choices)
        {
            TextTargetDescriptor? field = target.Fields.FirstOrDefault(x => x.SemanticField.Equals(choice.SemanticField, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                issues.Add(new("PTXTMV0101", $"Target field '{choice.SemanticField}' is not available.", true, choice.SemanticField));
                continue;
            }
            if (!field.EffectiveSupportedModes.Contains(choice.Mode) && choice.Mode != TextImportMode.CreateRecord)
            {
                issues.Add(new("PTXTMV0102", $"{field.DisplayName} does not support {choice.Mode} imports.", true, field.SemanticField));
                continue;
            }

            string generated = ResolveGeneratedText(bundle, choice.GeneratedField, field, target.SelectedField);
            if (string.IsNullOrEmpty(generated) && !HasExplicitGeneratedField(bundle, choice.GeneratedField, field))
            {
                issues.Add(new("PTXTMV0103", $"The generated result has no value for {field.DisplayName}.", false, field.SemanticField));
                continue;
            }

            TextImportMode effectiveMode = choice.Mode == TextImportMode.CreateRecord ? TextImportMode.Replace : choice.Mode;
            if (field.MayContainPluginTags && effectiveMode == TextImportMode.Replace && MvTextSafety.LooksLikePluginTag(field.CurrentValue))
            {
                issues.Add(new("PTXTMV0001", $"Replacing {field.DisplayName} could remove plugin tags. Use append/prepend/fill, or explicitly deselect the Note field.", true, field.SemanticField));
                continue;
            }

            string next = MvTextSafety.ApplyMode(field.CurrentValue, generated, effectiveMode);
            if (field.SuggestedMaximumLength is int max && next.Length > max)
                issues.Add(new("PTXTMV0104", $"{field.DisplayName} is {next.Length:N0} characters; the suggested maximum is {max:N0}.", false, field.SemanticField));
            if (!field.SupportsMultiline && (next.Contains('\n') || next.Contains('\r')))
                issues.Add(new("PTXTMV0105", $"{field.DisplayName} is a single-line field but the generated text contains line breaks.", false, field.SemanticField));

            if (!string.Equals(field.CurrentValue, next, StringComparison.Ordinal) || creation is not null)
                changes.Add(new(field.DataFile, creation?.RecordId.ToString() ?? field.RecordId, field.JsonPath, creation is null ? field.CurrentValue : string.Empty, next, field.SemanticField, field.DisplayName));
        }

        if (changes.Count == 0 && !issues.Any(x => x.IsError))
            issues.Add(new("PTXTMV0106", "The selected generated fields would not change the target record.", false));

        string description = creation is null
            ? $"Import generated text into {target.DisplayName}"
            : $"Create {creation.DisplayName} from generated text";
        return new TextImportPlan(description, changes, issues, creation);
    }

    public static string ResolveGeneratedText(GeneratedTextBundle bundle, string? requestedField, TextTargetDescriptor target, TextTargetDescriptor? selectedTarget)
    {
        string[] names = BuildCandidateNames(requestedField, target).ToArray();
        foreach (string name in names)
        {
            GeneratedField? exact = bundle.Fields.FirstOrDefault(field =>
                field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.SemanticRole, name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.Text;
        }

        // The primary result belongs to the field that launched Text Studio. For other fields we
        // only import explicit generated fields; this prevents a spell name from filling description/messages.
        if (selectedTarget is not null && target.SemanticField.Equals(selectedTarget.SemanticField, StringComparison.OrdinalIgnoreCase))
            return bundle.PrimaryText;
        return string.Empty;
    }

    public static IReadOnlyList<string> DiscoverGeneratedFields(GeneratedTextBundle bundle) =>
        bundle.Fields.Select(x => x.Name)
            .Concat(bundle.Fields.Where(x => !string.IsNullOrWhiteSpace(x.SemanticRole)).Select(x => x.SemanticRole!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasExplicitGeneratedField(GeneratedTextBundle bundle, string? requestedField, TextTargetDescriptor target) =>
        BuildCandidateNames(requestedField, target).Any(name => bundle.Fields.Any(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || string.Equals(field.SemanticRole, name, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> BuildCandidateNames(string? requestedField, TextTargetDescriptor target)
    {
        if (!string.IsNullOrWhiteSpace(requestedField)) yield return requestedField;
        yield return target.SemanticField;
        if (!string.IsNullOrWhiteSpace(target.FieldKey)) yield return target.FieldKey;
        if (target.SemanticField.Equals("profile", StringComparison.OrdinalIgnoreCase)) yield return "description";
        if (target.SemanticField.Equals("description", StringComparison.OrdinalIgnoreCase)) yield return "profile";
        if (target.SemanticField.Equals("note", StringComparison.OrdinalIgnoreCase)) yield return "lore";
    }
}
