using System.Text.RegularExpressions;

namespace Playroma.Sprite.Automation;

public static class AutomationVariables
{
    private static readonly Regex VariablePattern = new("\\$\\{(?<name>[A-Za-z0-9_.-]+)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Expand(string value, SpriteAutomationContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        return VariablePattern.Replace(value, match => Resolve(match.Groups["name"].Value, context));
    }

    public static string Resolve(string name, SpriteAutomationContext context)
    {
        if (context.Variables.TryGetValue(name, out string? value)) return value;
        return name.ToLowerInvariant() switch
        {
            "document" => Sanitize(context.Package.Document.Name),
            "width" => context.Package.Document.Canvas.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "height" => context.Package.Document.Canvas.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "frames" => context.Package.Document.Frames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "workspace" => context.WorkspaceRoot,
            "output" => context.OutputRoot,
            "date" => DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "time" => DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new KeyNotFoundException($"Automation variable '{name}' is not defined.")
        };
    }

    public static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "sprite" : result.Trim();
    }
}
