using System.Globalization;
using System.Text.RegularExpressions;

namespace Playroma.Text.Generators.Tracery;

public delegate string TraceryModifier(string input);

public sealed class TraceryModifierRegistry
{
    private readonly Dictionary<string, TraceryModifier> _modifiers = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> Names => _modifiers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(string name, TraceryModifier modifier)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Modifier name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(modifier);
        _modifiers[name] = modifier;
    }

    public bool TryApply(string name, string text, out string output)
    {
        if (_modifiers.TryGetValue(name, out var modifier))
        {
            output = modifier(text);
            return true;
        }
        output = text;
        return false;
    }

    public static TraceryModifierRegistry CreateDefault()
    {
        var r = new TraceryModifierRegistry();
        // Classic galaxykate/tracery universal modifiers.
        r.Register("capitalizeAll", s => Regex.Replace(s, @"(?:^|\s)\S", m => m.Value.ToUpperInvariant()));
        r.Register("capitalize", Capitalize);
        r.Register("inQuotes", s => "\"" + s + "\"");
        r.Register("comma", Comma);
        r.Register("beeSpeak", s => { var i = s.IndexOf('s'); return i < 0 ? s : s[..i] + "zzz" + s[(i + 1)..]; });
        r.Register("a", Article);
        r.Register("s", ClassicPlural);
        r.Register("ed", ClassicPast);

        // Playroma conveniences. These do not alter Classic grammar semantics unless used.
        r.Register("upper", s => s.ToUpperInvariant());
        r.Register("lower", s => s.ToLowerInvariant());
        r.Register("trim", s => s.Trim());
        r.Register("title", s => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant()));
        r.Register("sentence", s => Capitalize(s.Trim()));
        r.Register("possessive", s => s.EndsWith('s') ? s + "'" : s + "'s");
        r.Register("plural", SensiblePlural);
        return r;
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static bool IsConsonant(char c) => !"aeiou".Contains(char.ToLowerInvariant(c));
    private static string Article(string s) => string.IsNullOrEmpty(s) ? s : (IsConsonant(s[0]) ? "a " : "an ") + s;
    private static string Comma(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return ",.?!".Contains(s[^1]) ? s : s + ",";
    }

    private static string ClassicPlural(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var last = char.ToLowerInvariant(s[^1]);
        return last switch
        {
            'y' when s.Length > 1 && IsConsonant(s[^2]) => s[..^1] + "ies",
            'y' => s + "s",
            'x' => s[..^1] + "xen", // Matches classic tracery.js behavior.
            'z' => s[..^1] + "zes",
            'h' => s[..^1] + "hes",
            _ => s + "s"
        };
    }

    private static string ClassicPast(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var space = s.IndexOf(' ');
        var word = space > 0 ? s[..space] : s;
        var rest = space > 0 ? s[space..] : string.Empty;
        if (word.EndsWith('y') && word.Length > 1 && IsConsonant(word[^2])) return word[..^1] + "ied" + rest;
        if (word.EndsWith('e')) return word + "d" + rest;
        return word + "ed" + rest;
    }

    private static string SensiblePlural(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        if (Regex.IsMatch(s, "[^aeiou]y$", RegexOptions.IgnoreCase)) return s[..^1] + "ies";
        if (Regex.IsMatch(s, "(s|x|z|ch|sh)$", RegexOptions.IgnoreCase)) return s + "es";
        return s + "s";
    }
}
