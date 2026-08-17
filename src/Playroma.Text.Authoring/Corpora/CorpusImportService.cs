using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.Text.Core.Corpora;

namespace Playroma.Text.Authoring.Corpora;

public sealed record CorpusImportOptions(
    string? Id = null,
    string? Name = null,
    string? Author = null,
    string? License = null,
    string? SourceUri = null,
    string? Notes = null,
    int? CsvColumnIndex = null,
    string? JsonPropertyName = null,
    CorpusNormalizationOptions? Normalization = null);

public sealed class CorpusImportService
{
    public async Task<TextCorpus> ImportFileAsync(string path, CorpusImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new CorpusImportOptions();
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var text = extension switch
        {
            ".csv" => ExtractCsv(raw, options.CsvColumnIndex),
            ".json" => ExtractJson(raw, options.JsonPropertyName),
            _ => raw
        };
        var id = options.Id ?? SafeId(Path.GetFileNameWithoutExtension(path));
        var name = options.Name ?? Path.GetFileNameWithoutExtension(path);
        var source = new CorpusSourceMetadata(name, Path.GetFileName(path), options.Author, options.License, options.SourceUri, options.Notes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), DateTimeOffset.UtcNow);
        return new TextCorpus(id, 1, name, text, [source], options.Normalization ?? new CorpusNormalizationOptions(),
            Tags: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "imported", extension.TrimStart('.') }, CreatedAtUtc: DateTimeOffset.UtcNow, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public TextCorpus FromPastedText(string text, string name = "Pasted Corpus", CorpusImportOptions? options = null)
    {
        options ??= new CorpusImportOptions();
        var id = options.Id ?? SafeId(name);
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return new TextCorpus(id, 1, options.Name ?? name, text ?? string.Empty,
            [new CorpusSourceMetadata(options.Name ?? name, Author: options.Author, License: options.License, SourceUri: options.SourceUri, Notes: options.Notes,
                Sha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ImportedAtUtc: DateTimeOffset.UtcNow)],
            options.Normalization ?? new CorpusNormalizationOptions(), Tags: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pasted" },
            CreatedAtUtc: DateTimeOffset.UtcNow, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public static string ExtractCsv(string csv, int? columnIndex = null)
    {
        var lines = new List<string>();
        foreach (var row in ParseCsv(csv))
        {
            if (row.Count == 0) continue;
            if (columnIndex is { } index)
            {
                if (index >= 0 && index < row.Count && !string.IsNullOrWhiteSpace(row[index])) lines.Add(row[index]);
            }
            else foreach (var cell in row) if (!string.IsNullOrWhiteSpace(cell)) lines.Add(cell);
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string ExtractJson(string json, string? propertyName = null)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var result = new List<string>();
        Collect(doc.RootElement, propertyName, result);
        return string.Join(Environment.NewLine, result);
    }

    private static void Collect(JsonElement element, string? propertyName, List<string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && (string.IsNullOrWhiteSpace(propertyName) || p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
                        result.Add(p.Value.GetString() ?? string.Empty);
                    else Collect(p.Value, propertyName, result);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, propertyName, result);
                break;
            case JsonValueKind.String when string.IsNullOrWhiteSpace(propertyName):
                result.Add(element.GetString() ?? string.Empty);
                break;
        }
    }

    private static IEnumerable<IReadOnlyList<string>> ParseCsv(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(field.ToString()); field.Clear(); yield return row.ToArray(); row.Clear(); }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row.ToArray(); }
    }

    private static string SafeId(string value)
    {
        var id = string.Concat((value ?? "corpus").ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
        while (id.Contains("--", StringComparison.Ordinal)) id = id.Replace("--", "-", StringComparison.Ordinal);
        return id.Trim('-') is { Length: > 0 } result ? result : "corpus";
    }
}
