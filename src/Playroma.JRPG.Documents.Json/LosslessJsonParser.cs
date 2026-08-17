using System.Text;
using System.Text.Json;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Documents.Json;

public sealed record LosslessJsonParseResult(
    LosslessJsonDocument? Document,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public bool Success => Document is not null && Diagnostics.All(
        diagnostic => diagnostic.Severity is not DiagnosticSeverity.Error and not DiagnosticSeverity.Fatal);
}

public sealed class LosslessJsonParser
{
    public LosslessJsonParseResult Parse(ReadOnlyMemory<byte> source, string? relativePath = null)
    {
        try
        {
            Parser parser = new(source);
            JsonSyntaxNode root = parser.ParseDocument();
            JsonFormattingProfile formatting = JsonFormattingProfileDetector.Detect(source.Span);
            return new LosslessJsonParseResult(
                new LosslessJsonDocument(source.ToArray(), root, formatting),
                parser.Diagnostics);
        }
        catch (LosslessJsonParseException exception)
        {
            ProjectDiagnostic diagnostic = ProjectDiagnostic.Error(
                DiagnosticCodes.MutableInvalidJson,
                "The document is not valid JSON and can only be preserved read-only.",
                new ProjectLocation(relativePath, exception.JsonPath),
                $"Byte {exception.ByteOffset}: {exception.Message}");
            return new LosslessJsonParseResult(null, [diagnostic]);
        }
    }

    private sealed class Parser
    {
        private readonly ReadOnlyMemory<byte> _source;
        private int _position;
        private readonly List<ProjectDiagnostic> _diagnostics = [];

        public Parser(ReadOnlyMemory<byte> source)
        {
            _source = source;
            if (source.Length >= 3 && source.Span[0] == 0xEF && source.Span[1] == 0xBB && source.Span[2] == 0xBF)
            {
                _position = 3;
            }
        }

        public IReadOnlyList<ProjectDiagnostic> Diagnostics => _diagnostics;

        public JsonSyntaxNode ParseDocument()
        {
            SkipWhitespace();
            JsonSyntaxNode root = ParseValue("/");
            SkipWhitespace();
            if (_position != _source.Length)
            {
                throw Error("Unexpected content after the root JSON value.", "/");
            }

            return root;
        }

        private JsonSyntaxNode ParseValue(string path)
        {
            if (_position >= _source.Length) throw Error("Expected a JSON value.", path);
            return _source.Span[_position] switch
            {
                (byte)'{' => ParseObject(path),
                (byte)'[' => ParseArray(path),
                (byte)'\"' => ParseString(path),
                (byte)'-' or >= (byte)'0' and <= (byte)'9' => ParseNumber(path),
                (byte)'t' => ParseLiteral("true"u8, JsonSyntaxKind.True, path),
                (byte)'f' => ParseLiteral("false"u8, JsonSyntaxKind.False, path),
                (byte)'n' => ParseLiteral("null"u8, JsonSyntaxKind.Null, path),
                _ => throw Error("Expected a JSON value.", path)
            };
        }

        private JsonObjectSyntax ParseObject(string path)
        {
            int start = _position++;
            List<JsonPropertySyntax> properties = [];
            SkipWhitespace();
            if (TryConsume((byte)'}'))
            {
                return new JsonObjectSyntax(TextSpan.FromBounds(start, _position), start, _position - 1, properties);
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != (byte)'\"') throw Error("Expected a quoted object property name.", path);
                int propertyStart = _position;
                JsonStringSyntax name = ParseString(path);
                SkipWhitespace();
                Require((byte)':', "Expected ':' after an object property name.", path);
                SkipWhitespace();
                string propertyPath = JsonPath.Combine(path, name.Value);
                JsonSyntaxNode value = ParseValue(propertyPath);
                JsonPropertySyntax property = new(
                    TextSpan.FromBounds(propertyStart, value.Span.End),
                    name,
                    value);
                if (properties.Any(existing => string.Equals(existing.Name, property.Name, StringComparison.Ordinal)))
                {
                    _diagnostics.Add(ProjectDiagnostic.Warning(
                        DiagnosticCodes.DuplicateJsonProperty,
                        $"Duplicate JSON property '{property.Name}' was preserved.",
                        new ProjectLocation(JsonPath: propertyPath)));
                }

                properties.Add(property);
                SkipWhitespace();
                if (TryConsume((byte)'}'))
                {
                    return new JsonObjectSyntax(TextSpan.FromBounds(start, _position), start, _position - 1, properties);
                }

                Require((byte)',', "Expected ',' or '}' after an object property.", path);
                SkipWhitespace();
            }
        }

        private JsonArraySyntax ParseArray(string path)
        {
            int start = _position++;
            List<JsonSyntaxNode> elements = [];
            SkipWhitespace();
            if (TryConsume((byte)']'))
            {
                return new JsonArraySyntax(TextSpan.FromBounds(start, _position), start, _position - 1, elements);
            }

            while (true)
            {
                SkipWhitespace();
                JsonSyntaxNode element = ParseValue(JsonPath.Combine(path, elements.Count));
                elements.Add(element);
                SkipWhitespace();
                if (TryConsume((byte)']'))
                {
                    return new JsonArraySyntax(TextSpan.FromBounds(start, _position), start, _position - 1, elements);
                }

                Require((byte)',', "Expected ',' or ']' after an array element.", path);
                SkipWhitespace();
            }
        }

        private JsonStringSyntax ParseString(string path)
        {
            int start = _position;
            Require((byte)'\"', "Expected a JSON string.", path);
            bool escaped = false;
            while (_position < _source.Length)
            {
                byte current = _source.Span[_position++];
                if (!escaped && current == (byte)'\"')
                {
                    TextSpan span = TextSpan.FromBounds(start, _position);
                    try
                    {
                        string? value = JsonSerializer.Deserialize<string>(_source.Span.Slice(span.Start, span.Length));
                        return new JsonStringSyntax(span, value ?? string.Empty);
                    }
                    catch (JsonException exception)
                    {
                        throw Error("Invalid JSON string: " + exception.Message, path, start);
                    }
                }

                if (!escaped && current < 0x20)
                {
                    throw Error("Unescaped control character in JSON string.", path, _position - 1);
                }

                if (escaped)
                {
                    if (current == (byte)'u')
                    {
                        for (int index = 0; index < 4; index++)
                        {
                            if (_position >= _source.Length || !IsHex(_source.Span[_position]))
                            {
                                throw Error("Invalid Unicode escape in JSON string.", path);
                            }

                            _position++;
                        }
                    }
                    else if (current is not ((byte)'\"') and not ((byte)'\\') and not ((byte)'/')
                             and not ((byte)'b') and not ((byte)'f') and not ((byte)'n')
                             and not ((byte)'r') and not ((byte)'t'))
                    {
                        throw Error("Invalid escape sequence in JSON string.", path, _position - 1);
                    }

                    escaped = false;
                }
                else if (current == (byte)'\\')
                {
                    escaped = true;
                }
            }

            throw Error("Unterminated JSON string.", path, start);
        }

        private JsonNumberSyntax ParseNumber(string path)
        {
            int start = _position;
            TryConsume((byte)'-');
            if (_position >= _source.Length) throw Error("Incomplete JSON number.", path);
            if (TryConsume((byte)'0'))
            {
                if (_position < _source.Length && IsDigit(_source.Span[_position]))
                {
                    throw Error("JSON numbers may not contain a leading zero.", path);
                }
            }
            else
            {
                RequireDigit(path);
                while (_position < _source.Length && IsDigit(_source.Span[_position])) _position++;
            }

            if (TryConsume((byte)'.'))
            {
                RequireDigit(path);
                while (_position < _source.Length && IsDigit(_source.Span[_position])) _position++;
            }

            if (_position < _source.Length && _source.Span[_position] is (byte)'e' or (byte)'E')
            {
                _position++;
                if (_position < _source.Length && _source.Span[_position] is (byte)'+' or (byte)'-') _position++;
                RequireDigit(path);
                while (_position < _source.Length && IsDigit(_source.Span[_position])) _position++;
            }

            TextSpan span = TextSpan.FromBounds(start, _position);
            return new JsonNumberSyntax(span, Encoding.UTF8.GetString(_source.Span.Slice(span.Start, span.Length)));
        }

        private JsonLiteralSyntax ParseLiteral(ReadOnlySpan<byte> literal, JsonSyntaxKind kind, string path)
        {
            int start = _position;
            if (_source.Span.Slice(_position).StartsWith(literal))
            {
                _position += literal.Length;
                return new JsonLiteralSyntax(kind, TextSpan.FromBounds(start, _position));
            }

            throw Error($"Expected '{Encoding.UTF8.GetString(literal)}'.", path);
        }

        private byte Peek() => _position < _source.Length ? _source.Span[_position] : (byte)0;

        private bool TryConsume(byte value)
        {
            if (_position >= _source.Length || _source.Span[_position] != value) return false;
            _position++;
            return true;
        }

        private void Require(byte value, string message, string path)
        {
            if (!TryConsume(value)) throw Error(message, path);
        }

        private void RequireDigit(string path)
        {
            if (_position >= _source.Length || !IsDigit(_source.Span[_position]))
            {
                throw Error("Expected a digit in JSON number.", path);
            }

            _position++;
        }

        private void SkipWhitespace()
        {
            while (_position < _source.Length && _source.Span[_position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                _position++;
            }
        }

        private LosslessJsonParseException Error(string message, string path, int? offset = null) =>
            new(message, offset ?? _position, path);

        private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

        private static bool IsHex(byte value) => IsDigit(value)
            || value is >= (byte)'a' and <= (byte)'f'
            || value is >= (byte)'A' and <= (byte)'F';
    }
}

public sealed class LosslessJsonParseException : FormatException
{
    public LosslessJsonParseException(string message, int byteOffset, string jsonPath)
        : base(message)
    {
        ByteOffset = byteOffset;
        JsonPath = jsonPath;
    }

    public int ByteOffset { get; }

    public string JsonPath { get; }
}
