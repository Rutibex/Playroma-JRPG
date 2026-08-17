using System.Globalization;
using System.Text;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

internal static class JsonCommandUtilities
{
    public static byte[] Validate(string rawJson) =>
        JsonValueEncoder.ValidateRawJson(Encoding.UTF8.GetBytes(rawJson));

    public static void SetOrAdd(
        MutableJsonProjectDocument document,
        JsonPath path,
        ReadOnlySpan<byte> rawJson)
    {
        if (document.ContainsPath(path))
        {
            document.SetRawJson(path, rawJson);
            return;
        }

        JsonPath parentPath = path.Parent;
        JsonSyntaxNode? parent = document.Syntax.Resolve(parentPath);
        string[] segments = path.Segments().ToArray();
        if (segments.Length == 0)
        {
            document.SetRawJson(JsonPath.Root, rawJson);
            return;
        }

        string finalSegment = segments[^1];
        if (parent is JsonObjectSyntax)
        {
            document.AddProperty(parentPath, finalSegment, rawJson);
            return;
        }

        if (parent is JsonArraySyntax array
            && int.TryParse(finalSegment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index == array.Elements.Count)
        {
            document.InsertArrayElement(parentPath, index, rawJson);
            return;
        }

        throw new InvalidOperationException($"JSON path '{path}' does not exist and cannot be added to its parent.");
    }

    public static void RemovePath(MutableJsonProjectDocument document, JsonPath path)
    {
        if (!document.ContainsPath(path)) return;
        JsonPath parentPath = path.Parent;
        JsonSyntaxNode? parent = document.Syntax.Resolve(parentPath);
        string[] segments = path.Segments().ToArray();
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("The root JSON value cannot be removed.");
        }

        string finalSegment = segments[^1];
        if (parent is JsonObjectSyntax)
        {
            document.RemoveProperty(parentPath, finalSegment);
            return;
        }

        if (parent is JsonArraySyntax
            && int.TryParse(finalSegment, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            document.RemoveArrayElement(parentPath, index);
            return;
        }

        throw new InvalidOperationException($"JSON path '{path}' cannot be removed from its parent.");
    }
}
