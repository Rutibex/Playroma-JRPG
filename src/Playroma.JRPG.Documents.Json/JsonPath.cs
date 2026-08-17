using System.Globalization;

namespace Playroma.JRPG.Documents.Json;

public readonly record struct JsonPath
{
    public JsonPath(string pointer)
    {
        Pointer = Normalize(pointer);
    }

    public string Pointer { get; }

    public static JsonPath Root => new("/");

    public static implicit operator JsonPath(string pointer) => new(pointer);

    public override string ToString() => Pointer;

    public JsonPath Append(string property) => new(Combine(Pointer, property));

    public JsonPath Append(int index) => new(Combine(Pointer, index));

    public JsonPath Parent
    {
        get
        {
            if (Pointer == "/") return Root;
            int separator = Pointer.LastIndexOf('/');
            return separator <= 0 ? Root : new JsonPath(Pointer[..separator]);
        }
    }

    public bool IsSameOrAncestorOf(JsonPath other)
    {
        if (Pointer == "/") return true;
        return string.Equals(Pointer, other.Pointer, StringComparison.Ordinal)
            || other.Pointer.StartsWith(Pointer + "/", StringComparison.Ordinal);
    }

    public IReadOnlyList<string> Segments()
    {
        if (Pointer == "/") return [];
        return Pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Unescape)
            .ToArray();
    }

    public static string Combine(string path, string property)
    {
        string normalized = Normalize(path);
        string escaped = Escape(property);
        return normalized == "/" ? "/" + escaped : normalized + "/" + escaped;
    }

    public static string Combine(string path, int index) =>
        Combine(path, index.ToString(CultureInfo.InvariantCulture));

    public static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    public static string Unescape(string segment) => segment.Replace("~1", "/", StringComparison.Ordinal)
        .Replace("~0", "~", StringComparison.Ordinal);

    private static string Normalize(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        if (pointer.Length == 0 || pointer == "/") return "/";
        return pointer.StartsWith("/", StringComparison.Ordinal) ? pointer.TrimEnd('/') : "/" + pointer.TrimEnd('/');
    }
}
