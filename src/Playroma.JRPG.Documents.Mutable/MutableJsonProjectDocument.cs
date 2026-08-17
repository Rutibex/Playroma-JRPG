using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Documents.Mutable;

public class MutableJsonProjectDocument : IMutableProjectDocument, ITransactionalDocumentSnapshot
{
    private LosslessJsonDocument? _document;
    private byte[] _originalBytes;
    private readonly List<DocumentChange> _changes = [];
    private readonly List<ProjectDiagnostic> _diagnostics;
    private bool _disposed;

    protected MutableJsonProjectDocument(
        string relativePath,
        MvDocumentKind kind,
        byte[] originalBytes,
        ContentFingerprint originalFingerprint,
        LosslessJsonDocument? document,
        IReadOnlyList<ProjectDiagnostic> diagnostics)
    {
        RelativePath = relativePath;
        Kind = kind;
        _originalBytes = originalBytes;
        OriginalFingerprint = originalFingerprint;
        _document = document;
        _diagnostics = diagnostics.ToList();
        State = document is null ? MutableDocumentState.InvalidReadOnly : MutableDocumentState.Clean;
    }

    public string RelativePath { get; }

    public MvDocumentKind Kind { get; }

    public MutableDocumentState State { get; private set; }

    public ContentFingerprint OriginalFingerprint { get; private set; }

    public ContentFingerprint? CurrentDiskFingerprint { get; internal set; }

    public IReadOnlyList<DocumentChange> Changes => _changes;

    public IReadOnlyList<ProjectDiagnostic> Diagnostics => _diagnostics;

    public LosslessJsonDocument Syntax
    {
        get
        {
            ThrowIfDisposed();
            if (_document is null)
            {
                throw new InvalidOperationException("The invalid document has no mutable JSON syntax tree.");
            }

            EnsureCurrentSyntax();
            return _document
                ?? throw new InvalidOperationException("The mutable JSON syntax tree became unavailable.");
        }
    }

    public static MutableJsonProjectDocument Create(
        string relativePath,
        MvDocumentKind kind,
        byte[] bytes,
        ContentFingerprint fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(bytes);
        LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(bytes, relativePath);
        return new MutableJsonProjectDocument(relativePath, kind, bytes, fingerprint, parsed.Document, parsed.Diagnostics);
    }

    public string? GetString(JsonPath path) => Syntax.GetString(path);

    public int? GetInt32(JsonPath path) => Syntax.GetInt32(path);

    public double? GetDouble(JsonPath path) => Syntax.GetDouble(path);

    public bool? GetBoolean(JsonPath path) => Syntax.GetBoolean(path);

    public void SetString(JsonPath path, string value)
    {
        byte[] encoded = JsonValueEncoder.EncodeString(value);
        string? oldRaw = GetRaw(path);
        string newRaw = Encoding.UTF8.GetString(encoded);
        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) return;
        Syntax.SetRawJson(path, encoded);
        RecordChange(path, DocumentChangeKind.SetValue, oldRaw, newRaw, $"Set string at {path}");
    }

    public void SetInt32(JsonPath path, int value)
    {
        string newRaw = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string? oldRaw = GetRaw(path);
        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) return;
        Syntax.SetInt32(path, value);
        RecordChange(path, DocumentChangeKind.SetValue, oldRaw, newRaw, $"Set integer at {path}");
    }

    public void SetInt32ArrayValues(
        JsonPath arrayPath,
        IReadOnlyList<IndexedInt32Value> values,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return;
        LosslessJsonDocument syntax = Syntax;
        JsonArraySyntax array = syntax.Resolve(arrayPath) as JsonArraySyntax
            ?? throw new InvalidOperationException($"JSON path '{arrayPath}' is not an array.");
        IndexedInt32Value[] normalized = values
            .GroupBy(value => value.Index)
            .Select(group => group.Last())
            .OrderBy(value => value.Index)
            .ToArray();
        foreach (IndexedInt32Value value in normalized)
        {
            if (value.Index < 0 || value.Index >= array.Elements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(values), $"Array index {value.Index} is outside '{arrayPath}'.");
            }
            if (array.Elements[value.Index] is not JsonNumberSyntax number
                || !int.TryParse(number.RawText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int current))
            {
                throw new InvalidDataException($"Array element {arrayPath.Append(value.Index)} is not an integer.");
            }
            if (value.ExpectedBefore.HasValue && current != value.ExpectedBefore.Value)
            {
                throw new InvalidOperationException($"Array element {arrayPath.Append(value.Index)} changed unexpectedly.");
            }
        }

        bool changed = false;
        foreach (IndexedInt32Value value in normalized)
        {
            int? current = syntax.GetInt32(arrayPath.Append(value.Index));
            if (current == value.Value) continue;
            syntax.SetInt32(arrayPath.Append(value.Index), value.Value);
            changed = true;
        }
        if (!changed) return;
        RecordChange(
            arrayPath,
            DocumentChangeKind.ReplaceSubtree,
            null,
            null,
            description ?? $"Set {normalized.Length} integer array values at {arrayPath}");
    }

    public void SetDouble(JsonPath path, double value)
    {
        byte[] encoded = JsonValueEncoder.EncodeDouble(value);
        string newRaw = Encoding.UTF8.GetString(encoded);
        string? oldRaw = GetRaw(path);
        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) return;
        Syntax.SetDouble(path, value);
        RecordChange(path, DocumentChangeKind.SetValue, oldRaw, newRaw, $"Set number at {path}");
    }

    public void SetBoolean(JsonPath path, bool value)
    {
        string newRaw = value ? "true" : "false";
        string? oldRaw = GetRaw(path);
        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) return;
        Syntax.SetBoolean(path, value);
        RecordChange(path, DocumentChangeKind.SetValue, oldRaw, newRaw, $"Set Boolean at {path}");
    }

    public void SetNull(JsonPath path)
    {
        string? oldRaw = GetRaw(path);
        if (string.Equals(oldRaw, "null", StringComparison.Ordinal)) return;
        Syntax.SetNull(path);
        RecordChange(path, DocumentChangeKind.SetValue, oldRaw, "null", $"Set null at {path}");
    }

    public void SetRawJson(JsonPath path, ReadOnlySpan<byte> rawJson)
    {
        string? oldRaw = GetRaw(path);
        byte[] validated = JsonValueEncoder.ValidateRawJson(rawJson);
        string newRaw = Encoding.UTF8.GetString(validated);
        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)) return;
        Syntax.SetRawJson(path, validated);
        RecordChange(path, DocumentChangeKind.ReplaceSubtree, oldRaw, newRaw, $"Replace JSON value at {path}");
    }

    public void AddProperty(JsonPath objectPath, string name, ReadOnlySpan<byte> rawJson)
    {
        byte[] validated = JsonValueEncoder.ValidateRawJson(rawJson);
        Syntax.AddProperty(objectPath, name, validated);
        JsonPath path = objectPath.Append(name);
        RecordChange(path, DocumentChangeKind.AddProperty, null, Encoding.UTF8.GetString(validated), $"Add property {path}");
    }

    public void RemoveProperty(JsonPath objectPath, string name)
    {
        JsonPath path = objectPath.Append(name);
        string? oldRaw = GetRaw(path);
        if (oldRaw is null) return;
        Syntax.RemoveProperty(objectPath, name);
        RecordChange(path, DocumentChangeKind.RemoveProperty, oldRaw, null, $"Remove property {path}");
    }

    public void InsertArrayElement(JsonPath arrayPath, int index, ReadOnlySpan<byte> rawJson)
    {
        byte[] validated = JsonValueEncoder.ValidateRawJson(rawJson);
        Syntax.InsertArrayElement(arrayPath, index, validated);
        JsonPath path = arrayPath.Append(index);
        RecordChange(path, DocumentChangeKind.InsertArrayElement, null, Encoding.UTF8.GetString(validated), $"Insert array element at {path}");
    }

    public void AppendArrayElements(JsonPath arrayPath, IReadOnlyList<byte[]> rawJsonValues)
    {
        ArgumentNullException.ThrowIfNull(rawJsonValues);
        if (rawJsonValues.Count == 0) return;
        int startIndex = (Syntax.Resolve(arrayPath) as JsonArraySyntax)?.Elements.Count
            ?? throw new InvalidOperationException($"JSON path '{arrayPath}' is not an array.");
        Syntax.AppendArrayElements(arrayPath, rawJsonValues);
        for (int offset = 0; offset < rawJsonValues.Count; offset++)
        {
            string raw = Encoding.UTF8.GetString(JsonValueEncoder.ValidateRawJson(rawJsonValues[offset]));
            RecordChange(
                arrayPath.Append(startIndex + offset),
                DocumentChangeKind.InsertArrayElement,
                null,
                raw,
                $"Append array element at {arrayPath.Append(startIndex + offset)}");
        }
    }

    public void RemoveArrayElement(JsonPath arrayPath, int index)
    {
        JsonPath path = arrayPath.Append(index);
        string? oldRaw = GetRaw(path);
        Syntax.RemoveArrayElement(arrayPath, index);
        RecordChange(path, DocumentChangeKind.RemoveArrayElement, oldRaw, null, $"Remove array element at {path}");
    }

    public bool ContainsPath(JsonPath path)
    {
        ThrowIfDisposed();
        return Syntax.Resolve(path) is not null;
    }

    public string? GetRawJson(JsonPath path)
    {
        ThrowIfDisposed();
        return GetRaw(path);
    }

    public byte[] CaptureGeneratedBytes()
    {
        ThrowIfDisposed();
        return _document?.GenerateBytes() ?? _originalBytes.ToArray();
    }

    public void RestoreGeneratedBytes(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        byte[] restored = bytes.ToArray();
        LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(restored, RelativePath);
        _document = parsed.Document
            ?? throw new InvalidDataException("The supplied JSON state could not be restored.");
        _diagnostics.Clear();
        _diagnostics.AddRange(parsed.Diagnostics);
        _changes.Clear();

        if (_originalBytes.AsSpan().SequenceEqual(restored))
        {
            State = MutableDocumentState.Clean;
            return;
        }

        IReadOnlyList<JsonSemanticDifference> differences = JsonSemanticDiffer.Compare(_originalBytes, restored);
        foreach (JsonSemanticDifference difference in differences)
        {
            _changes.Add(new DocumentChange(
                Guid.NewGuid(),
                difference.Path,
                DocumentChangeKind.ReplaceSubtree,
                null,
                null,
                "Restore transaction state at " + difference.Path));
        }

        if (_changes.Count == 0)
        {
            _changes.Add(new DocumentChange(
                Guid.NewGuid(),
                JsonPath.Root,
                DocumentChangeKind.ReplaceSubtree,
                null,
                null,
                "Restore transaction state"));
        }

        State = MutableDocumentState.Modified;
    }

    public ReadOnlyMemory<byte> GetOriginalBytes()
    {
        ThrowIfDisposed();
        return _originalBytes;
    }

    public ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ReadOnlyMemory<byte>>(_document?.GenerateBytes() ?? _originalBytes.ToArray());
    }

    public string GenerateSha256()
    {
        byte[] generated = _document?.GenerateBytes() ?? _originalBytes;
        return Convert.ToHexString(SHA256.HashData(generated)).ToLowerInvariant();
    }

    public void DiscardChanges()
    {
        ThrowIfDisposed();
        if (_document is not null)
        {
            LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(_originalBytes, RelativePath);
            _document = parsed.Document
                ?? throw new InvalidDataException("The original JSON baseline could not be reopened.");
            _diagnostics.Clear();
            _diagnostics.AddRange(parsed.Diagnostics);
        }

        _changes.Clear();
        State = _document is null ? MutableDocumentState.InvalidReadOnly : MutableDocumentState.Clean;
    }

    public void MarkSaving()
    {
        ThrowIfDisposed();
        State = MutableDocumentState.Saving;
    }

    public void MarkSaved(ReadOnlyMemory<byte> savedBytes, ContentFingerprint fingerprint)
    {
        ThrowIfDisposed();
        byte[] baseline = savedBytes.ToArray();
        LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(baseline, RelativePath);
        if (parsed.Document is null)
        {
            _diagnostics.Clear();
            _diagnostics.AddRange(parsed.Diagnostics);
            State = MutableDocumentState.SaveFailed;
            throw new InvalidDataException("Saved JSON could not be reopened as a mutable lossless document.");
        }

        _originalBytes = baseline;
        _document = parsed.Document;
        _changes.Clear();
        _diagnostics.Clear();
        _diagnostics.AddRange(parsed.Diagnostics);
        OriginalFingerprint = fingerprint;
        CurrentDiskFingerprint = fingerprint;
        State = MutableDocumentState.Clean;
    }

    public void MarkSaveFailed() => State = MutableDocumentState.SaveFailed;

    public void MarkExternallyModified(ContentFingerprint current)
    {
        CurrentDiskFingerprint = current;
        State = MutableDocumentState.ExternallyModified;
    }

    public void Dispose()
    {
        _disposed = true;
        State = MutableDocumentState.Disposed;
    }

    private string? GetRaw(JsonPath path)
    {
        LosslessJsonDocument syntax = Syntax;
        JsonSyntaxNode? node = syntax.Resolve(path);
        return node is null ? null : Encoding.UTF8.GetString(node.GetSource(syntax.OriginalBytes).Span);
    }

    private void RecordChange(
        JsonPath path,
        DocumentChangeKind kind,
        string? oldRaw,
        string? newRaw,
        string description)
    {
        bool structuralArrayChange = kind is DocumentChangeKind.InsertArrayElement or DocumentChangeKind.RemoveArrayElement;
        if (!structuralArrayChange)
        {
            for (int index = _changes.Count - 1; index >= 0; index--)
            {
                DocumentChange previous = _changes[index];
                if (previous.Path != path || previous.Kind is DocumentChangeKind.InsertArrayElement or DocumentChangeKind.RemoveArrayElement)
                {
                    continue;
                }

                _changes.RemoveAt(index);
                if (previous.Kind == DocumentChangeKind.AddProperty)
                {
                    if (kind == DocumentChangeKind.RemoveProperty)
                    {
                        RestoreCleanStateWhenNoChanges();
                        return;
                    }

                    kind = DocumentChangeKind.AddProperty;
                    oldRaw = null;
                }
                else if (previous.Kind == DocumentChangeKind.RemoveProperty && kind == DocumentChangeKind.AddProperty)
                {
                    kind = DocumentChangeKind.ReplaceSubtree;
                    oldRaw = previous.OldValue?.RawJson;
                }
                else
                {
                    oldRaw = previous.OldValue?.RawJson ?? oldRaw;
                }

                break;
            }
        }

        if (string.Equals(oldRaw, newRaw, StringComparison.Ordinal)
            && kind is DocumentChangeKind.SetValue or DocumentChangeKind.ReplaceSubtree)
        {
            RestoreCleanStateWhenNoChanges();
            return;
        }

        _changes.Add(new DocumentChange(
            Guid.NewGuid(),
            path,
            kind,
            oldRaw is null ? null : new JsonValueSnapshot(oldRaw),
            newRaw is null ? null : new JsonValueSnapshot(newRaw),
            description));
        State = MutableDocumentState.Modified;
    }

    private void EnsureCurrentSyntax()
    {
        if (_document is null || !_document.IsModified) return;
        byte[] currentBytes = _document.GenerateBytes();
        LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(currentBytes, RelativePath);
        _document = parsed.Document
            ?? throw new InvalidDataException("Generated JSON could not be reopened for continued editing.");
        _diagnostics.Clear();
        _diagnostics.AddRange(parsed.Diagnostics);
    }

    private void RestoreCleanStateWhenNoChanges()
    {
        if (_changes.Count != 0)
        {
            State = MutableDocumentState.Modified;
            return;
        }

        LosslessJsonParseResult parsed = new LosslessJsonParser().Parse(_originalBytes, RelativePath);
        _document = parsed.Document
            ?? throw new InvalidDataException("The original JSON baseline could not be reopened.");
        _diagnostics.Clear();
        _diagnostics.AddRange(parsed.Diagnostics);
        State = MutableDocumentState.Clean;
    }



    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
