using System.Text;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public sealed class MvPluginManifestDocument : IMutableProjectDocument, ITransactionalDocumentSnapshot
{
    private byte[] _originalBytes;
    private byte[] _prefix;
    private byte[] _suffix;
    private MutableJsonProjectDocument _arrayDocument;
    private ContentFingerprint? _currentDiskFingerprint;

    private MvPluginManifestDocument(
        string relativePath,
        byte[] originalBytes,
        byte[] prefix,
        byte[] array,
        byte[] suffix,
        ContentFingerprint fingerprint)
    {
        RelativePath = relativePath;
        _originalBytes = originalBytes;
        _prefix = prefix;
        _suffix = suffix;
        string arrayHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(array)).ToLowerInvariant();
        ContentFingerprint arrayFingerprint = new(array.Length, fingerprint.LastWriteTimeUtc, arrayHash);
        _arrayDocument = MutableJsonProjectDocument.Create(relativePath + "#$plugins", MvDocumentKind.Unknown, array, arrayFingerprint);
        OriginalFingerprint = fingerprint;
    }

    public string RelativePath { get; }
    public MvDocumentKind Kind => MvDocumentKind.Unknown;
    public MutableDocumentState State => _arrayDocument.State;
    public ContentFingerprint OriginalFingerprint { get; private set; }
    public ContentFingerprint? CurrentDiskFingerprint => _currentDiskFingerprint;
    public IReadOnlyList<DocumentChange> Changes => _arrayDocument.Changes;
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => _arrayDocument.Diagnostics;
    public int Count => (_arrayDocument.Syntax.Root as JsonArraySyntax)?.Elements.Count ?? 0;

    public static MvPluginManifestDocument Parse(
        string relativePath,
        byte[] bytes,
        ContentFingerprint fingerprint)
    {
        (int start, int end) = FindPluginArray(bytes);
        byte[] prefix = bytes[..start];
        byte[] array = bytes[start..end];
        byte[] suffix = bytes[end..];
        return new MvPluginManifestDocument(relativePath, bytes, prefix, array, suffix, fingerprint);
    }

    public string GetName(int index) => _arrayDocument.GetString(EntryPath(index).Append("name")) ?? string.Empty;

    public string GetDescription(int index) =>
        _arrayDocument.GetString(EntryPath(index).Append("description")) ?? string.Empty;

    public bool GetStatus(int index) =>
        _arrayDocument.GetBoolean(EntryPath(index).Append("status")) ?? false;

    public void SetStatus(int index, bool value)
    {
        JsonPath target = EntryPath(index).Append("status");
        if (_arrayDocument.Syntax.Resolve(target) is null)
        {
            _arrayDocument.AddProperty(EntryPath(index), "status", JsonValueEncoder.EncodeBoolean(value));
        }
        else
        {
            _arrayDocument.SetBoolean(target, value);
        }
    }

    public string? GetParameter(int index, string name) =>
        _arrayDocument.GetString(ParametersPath(index).Append(name));

    public IReadOnlyList<string> GetParameterNames(int index)
    {
        if (_arrayDocument.Syntax.Resolve(ParametersPath(index)) is not JsonObjectSyntax parameters) return [];
        return parameters.Properties.Select(property => property.Name).ToArray();
    }

    public void SetParameter(int index, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        JsonPath parametersPath = ParametersPath(index);
        if (_arrayDocument.Syntax.Resolve(parametersPath) is null)
        {
            _arrayDocument.AddProperty(EntryPath(index), "parameters", "{}"u8);
        }

        JsonPath target = parametersPath.Append(name);
        if (_arrayDocument.Syntax.Resolve(target) is null)
        {
            _arrayDocument.AddProperty(parametersPath, name, JsonValueEncoder.EncodeString(value));
        }
        else
        {
            _arrayDocument.SetString(target, value);
        }
    }

    public void RemoveParameter(int index, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _arrayDocument.RemoveProperty(ParametersPath(index), name);
    }

    public string GetRawEntry(int index)
    {
        LosslessJsonDocument syntax = _arrayDocument.Syntax;
        JsonSyntaxNode node = syntax.Resolve(EntryPath(index))
            ?? throw new ArgumentOutOfRangeException(nameof(index));
        return Encoding.UTF8.GetString(node.GetSource(syntax.OriginalBytes).Span);
    }

    public void InsertEntryRaw(int index, ReadOnlySpan<byte> rawJson) =>
        _arrayDocument.InsertArrayElement(JsonPath.Root, index, rawJson);

    public void AppendEntryRaw(ReadOnlySpan<byte> rawJson) =>
        _arrayDocument.InsertArrayElement(JsonPath.Root, Count, rawJson);

    public void RemoveEntry(int index) =>
        _arrayDocument.RemoveArrayElement(JsonPath.Root, index);

    public ReadOnlyMemory<byte> GetOriginalBytes() => _originalBytes;

    public ReadOnlyMemory<byte> GetArrayOriginalBytes() => _arrayDocument.GetOriginalBytes();

    public byte[] CaptureGeneratedBytes() => _arrayDocument.CaptureGeneratedBytes();

    public void RestoreGeneratedBytes(ReadOnlySpan<byte> bytes) =>
        _arrayDocument.RestoreGeneratedBytes(bytes);

    public ValueTask<ReadOnlyMemory<byte>> GenerateArrayBytesAsync(CancellationToken cancellationToken) =>
        _arrayDocument.GenerateBytesAsync(cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> array = await _arrayDocument.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        byte[] result = new byte[_prefix.Length + array.Length + _suffix.Length];
        _prefix.CopyTo(result, 0);
        array.CopyTo(result.AsMemory(_prefix.Length));
        _suffix.CopyTo(result, _prefix.Length + array.Length);
        return result;
    }

    public void DiscardChanges() => _arrayDocument.DiscardChanges();

    public void MarkSaving() => _arrayDocument.MarkSaving();

    public void MarkSaveFailed() => _arrayDocument.MarkSaveFailed();

    public void MarkSaved(byte[] fullBytes, ContentFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fullBytes);
        (int start, int end) = FindPluginArray(fullBytes);
        byte[] prefix = fullBytes[..start];
        byte[] array = fullBytes[start..end];
        byte[] suffix = fullBytes[end..];
        string arrayHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(array)).ToLowerInvariant();
        MutableJsonProjectDocument replacement = MutableJsonProjectDocument.Create(
            RelativePath + "#$plugins",
            MvDocumentKind.Unknown,
            array,
            new ContentFingerprint(array.Length, fingerprint.LastWriteTimeUtc, arrayHash));
        _arrayDocument.Dispose();
        _arrayDocument = replacement;
        _originalBytes = fullBytes;
        _prefix = prefix;
        _suffix = suffix;
        OriginalFingerprint = fingerprint;
        _currentDiskFingerprint = fingerprint;
    }

    public void Dispose() => _arrayDocument.Dispose();


    private static JsonPath EntryPath(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return JsonPath.Root.Append(index);
    }

    private static JsonPath ParametersPath(int index) => EntryPath(index).Append("parameters");

    private static (int Start, int End) FindPluginArray(ReadOnlySpan<byte> bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        int assignment = text.IndexOf("$plugins", StringComparison.Ordinal);
        if (assignment < 0) throw new FormatException("plugins.js does not contain a $plugins assignment.");
        int startCharacter = text.IndexOf('[', assignment);
        if (startCharacter < 0) throw new FormatException("plugins.js does not contain a plugin array.");
        bool inString = false;
        bool escaped = false;
        int depth = 0;
        int endCharacter = -1;
        for (int index = startCharacter; index < text.Length; index++)
        {
            char current = text[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }

            if (current == '"') inString = true;
            else if (current == '[') depth++;
            else if (current == ']' && --depth == 0)
            {
                endCharacter = index + 1;
                break;
            }
        }

        if (endCharacter < 0) throw new FormatException("plugins.js contains an unterminated plugin array.");
        int startByte = Encoding.UTF8.GetByteCount(text.AsSpan(0, startCharacter));
        int endByte = Encoding.UTF8.GetByteCount(text.AsSpan(0, endCharacter));
        return (startByte, endByte);
    }
}
