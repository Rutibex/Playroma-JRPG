using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Playroma.JRPG.Documents.Json;

public sealed class LosslessJsonDocument
{
    private readonly byte[] _originalBytes;
    private readonly List<JsonTextEdit> _edits = [];

    internal LosslessJsonDocument(
        byte[] originalBytes,
        JsonSyntaxNode root,
        JsonFormattingProfile formatting)
    {
        _originalBytes = originalBytes;
        Root = root;
        Formatting = formatting;
    }

    public ReadOnlyMemory<byte> OriginalBytes => _originalBytes;

    public JsonSyntaxNode Root { get; }

    public JsonFormattingProfile Formatting { get; }

    public IReadOnlyList<JsonTextEdit> Edits => _edits;

    public bool IsModified => _edits.Count > 0;

    public JsonSyntaxNode? Resolve(JsonPath path)
    {
        JsonSyntaxNode current = Root;
        foreach (string segment in path.Segments())
        {
            if (current is JsonObjectSyntax objectNode)
            {
                JsonPropertySyntax? property = objectNode.FindLastProperty(segment);
                if (property is null) return null;
                current = property.Value;
            }
            else if (current is JsonArraySyntax arrayNode
                     && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                     && index >= 0
                     && index < arrayNode.Elements.Count)
            {
                current = arrayNode.Elements[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    public string? GetString(JsonPath path) => Resolve(path) is JsonStringSyntax value ? value.Value : null;

    public int? GetInt32(JsonPath path)
    {
        if (Resolve(path) is not JsonNumberSyntax number) return null;
        return int.TryParse(number.RawText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    public double? GetDouble(JsonPath path)
    {
        if (Resolve(path) is not JsonNumberSyntax number) return null;
        return double.TryParse(number.RawText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    public bool? GetBoolean(JsonPath path) => Resolve(path)?.Kind switch
    {
        JsonSyntaxKind.True => true,
        JsonSyntaxKind.False => false,
        _ => null
    };

    public void SetString(JsonPath path, string value) => ReplaceValue(path, JsonValueEncoder.EncodeString(value));

    public void SetInt32(JsonPath path, int value) => ReplaceValue(path, JsonValueEncoder.EncodeInt32(value));

    public void SetInt64(JsonPath path, long value) => ReplaceValue(path, JsonValueEncoder.EncodeInt64(value));

    public void SetDouble(JsonPath path, double value) => ReplaceValue(path, JsonValueEncoder.EncodeDouble(value));

    public void SetBoolean(JsonPath path, bool value) => ReplaceValue(path, JsonValueEncoder.EncodeBoolean(value));

    public void SetNull(JsonPath path) => ReplaceValue(path, JsonValueEncoder.EncodeNull());

    public void SetRawJson(JsonPath path, ReadOnlySpan<byte> rawJson) =>
        ReplaceValue(path, JsonValueEncoder.ValidateRawJson(rawJson));

    public void AddProperty(JsonPath objectPath, string name, ReadOnlySpan<byte> rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        byte[] validated = JsonValueEncoder.ValidateRawJson(rawJson);
        if (Resolve(objectPath) is not JsonObjectSyntax objectNode)
        {
            throw new InvalidOperationException($"JSON path '{objectPath}' is not an object.");
        }

        if (objectNode.FindLastProperty(name) is not null)
        {
            throw new InvalidOperationException($"Object '{objectPath}' already contains property '{name}'.");
        }

        int depth = objectPath.Segments().Count;
        string colon = Formatting.SpaceAfterColon ? ": " : ":";
        string propertyText = Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(name)) + colon + Encoding.UTF8.GetString(validated);
        int insertionPosition;
        string insertionText;
        if (Formatting.Indentation == JsonIndentationStyle.Minified)
        {
            insertionPosition = objectNode.CloseBraceOffset;
            insertionText = objectNode.Properties.Count == 0 ? propertyText : "," + propertyText;
        }
        else
        {
            string childIndent = Formatting.Indent(depth + 1);
            if (objectNode.Properties.Count == 0)
            {
                insertionPosition = objectNode.CloseBraceOffset;
                insertionText = Formatting.NewLine + childIndent + propertyText + Formatting.NewLine + Formatting.Indent(depth);
            }
            else
            {
                JsonPropertySyntax last = objectNode.Properties[^1];
                insertionPosition = FindTrailingTriviaStart(last.Value.Span.End, objectNode.CloseBraceOffset);
                insertionText = "," + Formatting.NewLine + childIndent + propertyText;
            }
        }

        AddEdit(new JsonTextEdit(
            new TextSpan(insertionPosition, 0),
            Encoding.UTF8.GetBytes(insertionText),
            objectPath.Append(name),
            JsonEditKind.InsertProperty));
    }

    public void RemoveProperty(JsonPath objectPath, string name)
    {
        if (Resolve(objectPath) is not JsonObjectSyntax objectNode)
        {
            throw new InvalidOperationException($"JSON path '{objectPath}' is not an object.");
        }

        int index = -1;
        for (int current = objectNode.Properties.Count - 1; current >= 0; current--)
        {
            if (string.Equals(objectNode.Properties[current].Name, name, StringComparison.Ordinal))
            {
                index = current;
                break;
            }
        }

        if (index < 0) return;
        TextSpan span;
        if (objectNode.Properties.Count == 1)
        {
            span = TextSpan.FromBounds(objectNode.OpenBraceOffset + 1, objectNode.CloseBraceOffset);
        }
        else if (index < objectNode.Properties.Count - 1)
        {
            span = TextSpan.FromBounds(
                objectNode.Properties[index].NameToken.Span.Start,
                objectNode.Properties[index + 1].NameToken.Span.Start);
        }
        else
        {
            JsonPropertySyntax previous = objectNode.Properties[index - 1];
            span = TextSpan.FromBounds(previous.Value.Span.End, objectNode.Properties[index].Value.Span.End);
        }

        AddEdit(new JsonTextEdit(span, [], objectPath.Append(name), JsonEditKind.RemoveProperty));
    }

    public void InsertArrayElement(JsonPath arrayPath, int index, ReadOnlySpan<byte> rawJson)
    {
        byte[] validated = JsonValueEncoder.ValidateRawJson(rawJson);
        if (Resolve(arrayPath) is not JsonArraySyntax arrayNode)
        {
            throw new InvalidOperationException($"JSON path '{arrayPath}' is not an array.");
        }

        if (index < 0 || index > arrayNode.Elements.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index < arrayNode.Elements.Count)
        {
            int position = arrayNode.Elements[index].Span.Start;
            string separator = Formatting.Indentation == JsonIndentationStyle.Minified
                ? Encoding.UTF8.GetString(validated) + ","
                : Encoding.UTF8.GetString(validated) + "," + Formatting.NewLine + Formatting.Indent(arrayPath.Segments().Count + 1);
            AddEdit(new JsonTextEdit(new TextSpan(position, 0), Encoding.UTF8.GetBytes(separator), arrayPath.Append(index), JsonEditKind.InsertArrayElement));
            return;
        }

        int depth = arrayPath.Segments().Count;
        string valueText = Encoding.UTF8.GetString(validated);
        int insertionPosition;
        string insertionText;
        if (Formatting.Indentation == JsonIndentationStyle.Minified)
        {
            insertionPosition = arrayNode.CloseBracketOffset;
            insertionText = arrayNode.Elements.Count == 0 ? valueText : "," + valueText;
        }
        else if (arrayNode.Elements.Count == 0)
        {
            insertionPosition = arrayNode.CloseBracketOffset;
            insertionText = Formatting.NewLine + Formatting.Indent(depth + 1) + valueText
                + Formatting.NewLine + Formatting.Indent(depth);
        }
        else
        {
            JsonSyntaxNode last = arrayNode.Elements[^1];
            insertionPosition = FindTrailingTriviaStart(last.Span.End, arrayNode.CloseBracketOffset);
            insertionText = "," + Formatting.NewLine + Formatting.Indent(depth + 1) + valueText;
        }

        AddEdit(new JsonTextEdit(
            new TextSpan(insertionPosition, 0),
            Encoding.UTF8.GetBytes(insertionText),
            arrayPath.Append(index),
            JsonEditKind.InsertArrayElement));
    }

    public void AppendArrayElements(JsonPath arrayPath, IReadOnlyList<byte[]> rawJsonValues)
    {
        ArgumentNullException.ThrowIfNull(rawJsonValues);
        if (rawJsonValues.Count == 0) return;
        if (Resolve(arrayPath) is not JsonArraySyntax arrayNode)
        {
            throw new InvalidOperationException($"JSON path '{arrayPath}' is not an array.");
        }

        string[] values = rawJsonValues.Select(value =>
            Encoding.UTF8.GetString(JsonValueEncoder.ValidateRawJson(value))).ToArray();
        int depth = arrayPath.Segments().Count;
        int insertionPosition;
        string insertionText;
        if (Formatting.Indentation == JsonIndentationStyle.Minified)
        {
            insertionPosition = arrayNode.CloseBracketOffset;
            insertionText = (arrayNode.Elements.Count == 0 ? string.Empty : ",") + string.Join(",", values);
        }
        else
        {
            string separator = "," + Formatting.NewLine + Formatting.Indent(depth + 1);
            string joined = string.Join(separator, values);
            if (arrayNode.Elements.Count == 0)
            {
                insertionPosition = arrayNode.CloseBracketOffset;
                insertionText = Formatting.NewLine + Formatting.Indent(depth + 1) + joined
                    + Formatting.NewLine + Formatting.Indent(depth);
            }
            else
            {
                JsonSyntaxNode last = arrayNode.Elements[^1];
                insertionPosition = FindTrailingTriviaStart(last.Span.End, arrayNode.CloseBracketOffset);
                insertionText = separator + joined;
            }
        }

        AddEdit(new JsonTextEdit(
            new TextSpan(insertionPosition, 0),
            Encoding.UTF8.GetBytes(insertionText),
            arrayPath.Append(arrayNode.Elements.Count),
            JsonEditKind.InsertArrayElement));
    }

    public void RemoveArrayElement(JsonPath arrayPath, int index)
    {
        if (Resolve(arrayPath) is not JsonArraySyntax arrayNode)
        {
            throw new InvalidOperationException($"JSON path '{arrayPath}' is not an array.");
        }

        if (index < 0 || index >= arrayNode.Elements.Count) throw new ArgumentOutOfRangeException(nameof(index));
        TextSpan span;
        if (arrayNode.Elements.Count == 1)
        {
            span = TextSpan.FromBounds(arrayNode.OpenBracketOffset + 1, arrayNode.CloseBracketOffset);
        }
        else if (index < arrayNode.Elements.Count - 1)
        {
            span = TextSpan.FromBounds(arrayNode.Elements[index].Span.Start, arrayNode.Elements[index + 1].Span.Start);
        }
        else
        {
            span = TextSpan.FromBounds(arrayNode.Elements[index - 1].Span.End, arrayNode.Elements[index].Span.End);
        }

        AddEdit(new JsonTextEdit(span, [], arrayPath.Append(index), JsonEditKind.RemoveArrayElement));
    }

    public byte[] GenerateBytes()
    {
        if (_edits.Count == 0) return _originalBytes.ToArray();
        JsonTextEdit[] ordered = _edits.OrderBy(edit => edit.OriginalSpan.Start).ThenBy(edit => edit.OriginalSpan.Length).ToArray();
        ValidateNoOverlap(ordered);
        using MemoryStream stream = new(_originalBytes.Length + ordered.Sum(edit => edit.Replacement.Length - edit.OriginalSpan.Length));
        int cursor = 0;
        foreach (JsonTextEdit edit in ordered)
        {
            stream.Write(_originalBytes, cursor, edit.OriginalSpan.Start - cursor);
            stream.Write(edit.Replacement);
            cursor = edit.OriginalSpan.End;
        }

        stream.Write(_originalBytes, cursor, _originalBytes.Length - cursor);
        return stream.ToArray();
    }

    public void DiscardChanges() => _edits.Clear();

    private void ReplaceValue(JsonPath path, byte[] replacement)
    {
        JsonSyntaxNode node = Resolve(path) ?? throw new KeyNotFoundException($"JSON path '{path}' was not found.");
        AddEdit(new JsonTextEdit(node.Span, replacement, path, JsonEditKind.ReplaceValue));
    }

    private void AddEdit(JsonTextEdit edit)
    {
        for (int index = _edits.Count - 1; index >= 0; index--)
        {
            JsonTextEdit existing = _edits[index];
            if (existing.TargetPath == edit.TargetPath && existing.OriginalSpan == edit.OriginalSpan)
            {
                _edits.RemoveAt(index);
                continue;
            }

            if (Overlaps(existing.OriginalSpan, edit.OriginalSpan))
            {
                throw new InvalidOperationException(
                    $"JSON edit at '{edit.TargetPath}' overlaps an existing edit at '{existing.TargetPath}'. Generate and re-open the document before applying the structural edit.");
            }
        }

        _edits.Add(edit);
    }

    private int FindTrailingTriviaStart(int start, int end)
    {
        int cursor = end;
        while (cursor > start && _originalBytes[cursor - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') cursor--;
        return cursor;
    }

    private static bool Overlaps(TextSpan left, TextSpan right)
    {
        if (left.Length == 0 && right.Length == 0) return left.Start == right.Start;
        if (left.Length == 0) return left.Start > right.Start && left.Start < right.End;
        if (right.Length == 0) return right.Start > left.Start && right.Start < left.End;
        return left.Overlaps(right);
    }

    private static void ValidateNoOverlap(IReadOnlyList<JsonTextEdit> edits)
    {
        for (int index = 1; index < edits.Count; index++)
        {
            if (Overlaps(edits[index - 1].OriginalSpan, edits[index].OriginalSpan))
            {
                throw new InvalidOperationException("The generated JSON edit set contains overlapping source spans.");
            }
        }
    }
}
