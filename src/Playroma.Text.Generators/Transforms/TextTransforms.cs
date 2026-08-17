using System.Globalization;
using System.Text;

namespace Playroma.Text.Generators.Transforms;

public delegate string TextTransform(string input, IReadOnlyList<string> arguments);

public sealed class TextTransformRegistry
{
    private readonly Dictionary<string, TextTransform> _transforms = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Names => _transforms.Keys.Order(StringComparer.OrdinalIgnoreCase);

    public TextTransformRegistry Register(string name, TextTransform transform)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Transform name is required.", nameof(name));
        _transforms[name] = transform ?? throw new ArgumentNullException(nameof(transform));
        return this;
    }

    public bool TryApply(string name, string input, IReadOnlyList<string> arguments, out string output)
    {
        if (_transforms.TryGetValue(name, out var transform))
        {
            output = transform(input, arguments);
            return true;
        }
        output = input;
        return false;
    }

    public static TextTransformRegistry CreateDefault()
    {
        var r = new TextTransformRegistry();
        r.Register("trim", (s, _) => s.Trim());
        r.Register("lower", (s, _) => s.ToLowerInvariant());
        r.Register("upper", (s, _) => s.ToUpperInvariant());
        r.Register("capitalize", (s, _) => Capitalize(s));
        r.Register("sentence", (s, _) => Capitalize(s.Trim()));
        r.Register("title", (s, _) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()));
        r.Register("a", (s, _) => Article(s));
        r.Register("an", (s, _) => Article(s));
        r.Register("plural", (s, _) => Pluralize(s));
        r.Register("possessive", (s, _) => s.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? s + "'" : s + "'s");
        r.Register("period", (s, _) => EnsureTerminal(s, '.'));
        r.Register("exclaim", (s, _) => EnsureTerminal(s, '!'));
        r.Register("question", (s, _) => EnsureTerminal(s, '?'));
        r.Register("prefix", (s, a) => (a.Count > 0 ? a[0] : string.Empty) + s);
        r.Register("suffix", (s, a) => s + (a.Count > 0 ? a[0] : string.Empty));
        r.Register("replace", (s, a) => a.Count >= 2 ? s.Replace(a[0], a[1], StringComparison.Ordinal) : s);
        r.Register("collapse-space", (s, _) => CollapseWhitespace(s));
        return r;
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static string Article(string s)
    {
        var trimmed = s.TrimStart();
        if (trimmed.Length == 0) return s;
        var c = char.ToLowerInvariant(trimmed[0]);
        return ("aeiou".Contains(c) ? "an " : "a ") + s;
    }

    private static string Pluralize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        if (s.EndsWith("ch", StringComparison.OrdinalIgnoreCase) || s.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith("s", StringComparison.OrdinalIgnoreCase) || s.EndsWith("x", StringComparison.OrdinalIgnoreCase) || s.EndsWith("z", StringComparison.OrdinalIgnoreCase)) return s + "es";
        if (s.Length > 1 && s.EndsWith("y", StringComparison.OrdinalIgnoreCase) && !"aeiou".Contains(char.ToLowerInvariant(s[^2]))) return s[..^1] + "ies";
        return s + "s";
    }

    private static string EnsureTerminal(string s, char punctuation)
    {
        var trimmed = s.TrimEnd();
        if (trimmed.Length == 0) return trimmed;
        return ".!?…".Contains(trimmed[^1]) ? trimmed : trimmed + punctuation;
    }

    private static string CollapseWhitespace(string s)
    {
        var b = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = b.Length > 0; continue; }
            if (pendingSpace) { b.Append(' '); pendingSpace = false; }
            b.Append(c);
        }
        return b.ToString();
    }
}

public readonly record struct TextTransformInvocation(string Name, IReadOnlyList<string> Arguments)
{
    public static TextTransformInvocation Parse(string expression)
    {
        var trimmed = expression.Trim();
        var open = trimmed.IndexOf('(');
        if (open < 0 || !trimmed.EndsWith(')')) return new TextTransformInvocation(trimmed, Array.Empty<string>());
        var name = trimmed[..open].Trim();
        var raw = trimmed[(open + 1)..^1];
        var args = raw.Length == 0 ? Array.Empty<string>() : raw.Split(',', StringSplitOptions.TrimEntries).Select(Unquote).ToArray();
        return new TextTransformInvocation(name, args);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            return value[1..^1];
        return value;
    }
}
