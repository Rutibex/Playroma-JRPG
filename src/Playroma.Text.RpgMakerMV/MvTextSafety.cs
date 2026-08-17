namespace Playroma.Text.RpgMakerMV;

public static class MvTextSafety
{
    public static bool LooksLikePluginTag(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains('<') && text.Contains('>');

    public static string ApplyMode(string current, string generated, TextImportMode mode) => mode switch
    {
        TextImportMode.Replace or TextImportMode.CreateRecord => generated,
        TextImportMode.Append => current + generated,
        TextImportMode.Prepend => generated + current,
        TextImportMode.FillIfBlank => string.IsNullOrWhiteSpace(current) ? generated : current,
        // Cursor/selection modes need live editor selection state and are therefore resolved by
        // host-specific editors. Falling back to replacement here is intentionally explicit.
        TextImportMode.InsertAtCursor or TextImportMode.ReplaceSelection => generated,
        _ => generated
    };

    public static TextImportPlan BuildSingleFieldPlan(TextTargetDescriptor target, string generatedText, TextImportMode mode)
    {
        var issues = new List<TextImportValidationIssue>();
        if (target.MayContainPluginTags && mode == TextImportMode.Replace && LooksLikePluginTag(target.CurrentValue))
            issues.Add(new TextImportValidationIssue("PTXTMV0001", "Replacing this Note field could remove plugin tags. Use append, prepend, selection replacement, or explicitly exclude the field.", true, target.SemanticField));

        string next = ApplyMode(target.CurrentValue, generatedText, mode);
        return new TextImportPlan($"Import generated text into {target.DisplayName}",
            new[] { new TextImportChange(target.DataFile, target.RecordId, target.JsonPath, target.CurrentValue, next, target.SemanticField, target.DisplayName) }, issues);
    }
}
