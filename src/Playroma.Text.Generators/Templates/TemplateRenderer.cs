using System.Globalization;
using System.Text;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Transforms;

namespace Playroma.Text.Generators.Templates;

public enum MissingTemplateValueBehavior { Empty, KeepToken, Error }

public sealed record TemplateRenderOptions(
    double OptionalProbability = 0.5,
    MissingTemplateValueBehavior MissingValueBehavior = MissingTemplateValueBehavior.Error,
    int MaxPasses = 16);

public sealed record TemplateRenderResult(string Text, IReadOnlyList<string> MissingKeys, IReadOnlyList<string> UnknownTransforms);

public sealed class TemplateRenderer
{
    private readonly TextTransformRegistry _transforms;
    public TemplateRenderer(TextTransformRegistry? transforms = null) => _transforms = transforms ?? TextTransformRegistry.CreateDefault();

    public TemplateRenderResult Render(string template, IReadOnlyDictionary<string, object?> values, IDeterministicRandom random, TemplateRenderOptions? options = null)
    {
        options ??= new TemplateRenderOptions();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = template ?? string.Empty;

        for (var pass = 0; pass < options.MaxPasses; pass++)
        {
            var before = text;
            text = ExpandOptional(text, values, random, options, missing, unknown);
            text = ExpandTokens(text, values, random, options, missing, unknown);
            if (text == before || (!text.Contains("{{", StringComparison.Ordinal) && !text.Contains("[[", StringComparison.Ordinal))) break;
        }
        return new TemplateRenderResult(text, missing.ToArray(), unknown.ToArray());
    }

    private string ExpandOptional(string input, IReadOnlyDictionary<string, object?> values, IDeterministicRandom random, TemplateRenderOptions options, HashSet<string> missing, HashSet<string> unknown)
    {
        var output = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length;)
        {
            var start = input.IndexOf("[[", i, StringComparison.Ordinal);
            if (start < 0) { output.Append(input, i, input.Length - i); break; }
            output.Append(input, i, start - i);
            var end = input.IndexOf("]]", start + 2, StringComparison.Ordinal);
            if (end < 0) { output.Append(input, start, input.Length - start); break; }
            var body = input[(start + 2)..end];
            var probability = options.OptionalProbability;
            var colon = body.IndexOf(':');
            if (colon > 0 && TryProbability(body[..colon], out var parsed))
            {
                probability = parsed;
                body = body[(colon + 1)..];
            }
            if (random.NextBool(probability)) output.Append(body);
            i = end + 2;
        }
        return output.ToString();
    }

    private string ExpandTokens(string input, IReadOnlyDictionary<string, object?> values, IDeterministicRandom random, TemplateRenderOptions options, HashSet<string> missing, HashSet<string> unknown)
    {
        var output = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length;)
        {
            var start = input.IndexOf("{{", i, StringComparison.Ordinal);
            if (start < 0) { output.Append(input, i, input.Length - i); break; }
            output.Append(input, i, start - i);
            var end = input.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0) { output.Append(input, start, input.Length - start); break; }
            var raw = input[(start + 2)..end].Trim();
            output.Append(Evaluate(raw, values, random, options, missing, unknown));
            i = end + 2;
        }
        return output.ToString();
    }

    private string Evaluate(string raw, IReadOnlyDictionary<string, object?> values, IDeterministicRandom random, TemplateRenderOptions options, HashSet<string> missing, HashSet<string> unknown)
    {
        if (raw.StartsWith("choice:", StringComparison.OrdinalIgnoreCase))
        {
            var choices = raw[7..].Split("||", StringSplitOptions.None);
            return choices.Length == 0 ? string.Empty : choices[random.NextInt(choices.Length)];
        }

        if (raw.StartsWith("?", StringComparison.Ordinal))
        {
            var colon = raw.IndexOf(':');
            if (colon > 1)
            {
                var key = raw[1..colon].Trim();
                var branches = raw[(colon + 1)..].Split("||", 2, StringSplitOptions.None);
                var truthy = values.TryGetValue(key, out var v) && IsTruthy(v);
                return truthy ? branches[0] : branches.Length > 1 ? branches[1] : string.Empty;
            }
        }

        var pieces = raw.Split('|', StringSplitOptions.TrimEntries);
        var keyName = pieces[0];
        if (!TryResolve(values, keyName, out var value))
        {
            missing.Add(keyName);
            return options.MissingValueBehavior switch
            {
                MissingTemplateValueBehavior.Empty => string.Empty,
                MissingTemplateValueBehavior.KeepToken => "{{" + raw + "}}",
                _ => string.Empty
            };
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        for (var i = 1; i < pieces.Length; i++)
        {
            var invocation = TextTransformInvocation.Parse(pieces[i]);
            if (!_transforms.TryApply(invocation.Name, text, invocation.Arguments, out var transformed)) unknown.Add(invocation.Name);
            else text = transformed;
        }
        return text;
    }

    private static bool TryResolve(IReadOnlyDictionary<string, object?> values, string key, out object? value)
    {
        if (values.TryGetValue(key, out value)) return true;
        foreach (var kv in values)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; return true; }
        value = null;
        return false;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0",
        int i => i != 0,
        long l => l != 0,
        double d => Math.Abs(d) > double.Epsilon,
        _ => true
    };

    private static bool TryProbability(string text, out double value)
    {
        var t = text.Trim();
        if (t.EndsWith('%') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) { value = Math.Clamp(pct / 100.0, 0, 1); return true; }
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) { value = Math.Clamp(value, 0, 1); return true; }
        value = 0; return false;
    }
}
