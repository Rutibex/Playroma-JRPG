using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring.Release;

public enum TextProjectDoctorSeverity { Info, Warning, Error }
public sealed record TextProjectDoctorIssue(TextProjectDoctorSeverity Severity, string Area, string Message, string? Path = null);
public sealed record TextProjectDoctorReport(IReadOnlyList<TextProjectDoctorIssue> Issues)
{
    public bool IsReleaseReady => Issues.All(x => x.Severity != TextProjectDoctorSeverity.Error);
    public int ErrorCount => Issues.Count(x => x.Severity == TextProjectDoctorSeverity.Error);
    public int WarningCount => Issues.Count(x => x.Severity == TextProjectDoctorSeverity.Warning);
}

public sealed class TextProjectDoctor
{
    public async Task<TextProjectDoctorReport> AuditAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var layout = new TextProjectLayout(projectRoot);
        layout.EnsureCreated();
        var issues = new List<TextProjectDoctorIssue>();
        var allFiles = layout.SourceDirectories().Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var recipeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                using var _ = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (path.EndsWith(".playtext.json", StringComparison.OrdinalIgnoreCase))
                {
                    var recipe = JsonSerializer.Deserialize<TextRecipe>(text, RecipeJson.Options) ?? throw new InvalidDataException("Recipe deserialized to null.");
                    foreach (var diagnostic in TextRecipeValidator.Validate(recipe))
                        issues.Add(new TextProjectDoctorIssue(diagnostic.Severity == GenerationDiagnosticSeverity.Error ? TextProjectDoctorSeverity.Error : TextProjectDoctorSeverity.Warning, "Recipe", diagnostic.Message, path));
                    if (recipeIds.TryGetValue(recipe.Id, out var other)) issues.Add(new TextProjectDoctorIssue(TextProjectDoctorSeverity.Error, "Recipe", $"Duplicate project recipe id '{recipe.Id}' also exists at {other}.", path));
                    else recipeIds[recipe.Id] = path;
                }
            }
            catch (Exception ex) { issues.Add(new TextProjectDoctorIssue(TextProjectDoctorSeverity.Error, "JSON", ex.Message, path)); }
        }

        var aiSettings = Path.Combine(layout.Studio, "ai-providers.json");
        if (File.Exists(aiSettings))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(aiSettings, cancellationToken).ConfigureAwait(false));
                if (doc.RootElement.TryGetProperty("providers", out var providers) && providers.ValueKind == JsonValueKind.Array)
                    foreach (var provider in providers.EnumerateArray().Where(x => x.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True))
                    {
                        var id = provider.TryGetProperty("id", out var idNode) ? idNode.GetString() : "unknown";
                        var exe = provider.TryGetProperty("executablePath", out var exeNode) ? exeNode.GetString() : null;
                        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) issues.Add(new TextProjectDoctorIssue(TextProjectDoctorSeverity.Error, "Optional AI", $"Enabled provider '{id}' executable is missing.", aiSettings));
                    }
            }
            catch (Exception ex) { issues.Add(new TextProjectDoctorIssue(TextProjectDoctorSeverity.Error, "Optional AI", ex.Message, aiSettings)); }
        }

        if (issues.Count == 0) issues.Add(new TextProjectDoctorIssue(TextProjectDoctorSeverity.Info, "Release", "No Text Studio project issues detected."));
        return new TextProjectDoctorReport(issues);
    }
}
