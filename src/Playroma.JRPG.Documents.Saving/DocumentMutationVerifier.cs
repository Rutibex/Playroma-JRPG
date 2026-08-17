using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Documents.Saving;

public sealed record MutationVerificationResult(
    bool IsValid,
    IReadOnlyList<JsonSemanticDifference> Differences,
    IReadOnlyList<JsonSemanticDifference> UnauthorizedDifferences,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public interface IDocumentMutationVerifier
{
    MutationVerificationResult Verify(
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> generated,
        IReadOnlyList<DocumentChange> authorizedChanges,
        string relativePath);
}

public sealed class DocumentMutationVerifier : IDocumentMutationVerifier
{
    public MutationVerificationResult Verify(
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> generated,
        IReadOnlyList<DocumentChange> authorizedChanges,
        string relativePath)
    {
        IReadOnlyList<JsonSemanticDifference> actualDifferences;
        try
        {
            actualDifferences = JsonSemanticDiffer.Compare(original, generated);
        }
        catch (JsonException exception)
        {
            ProjectDiagnostic diagnostic = ProjectDiagnostic.Error(
                DiagnosticCodes.GeneratedJsonInvalid,
                "Generated content failed JSON validation.",
                new ProjectLocation(relativePath),
                exception.Message);
            return new MutationVerificationResult(false, [], [], [diagnostic]);
        }

        JsonSemanticDifference[] unauthorized;
        try
        {
            byte[] expected = BuildExpectedOutput(original, authorizedChanges);
            unauthorized = JsonSemanticDiffer.Compare(expected, generated).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            // Duplicate-property documents and malformed typed paths cannot always be
            // represented by JsonNode. Fall back to conservative path authorization;
            // the lossless writer still preserves all untouched source spans.
            JsonPath[] allowed = authorizedChanges.Select(change => change.Kind switch
            {
                DocumentChangeKind.ReplaceSubtree => change.Path,
                _ => change.Path
            }).ToArray();
            unauthorized = actualDifferences.Where(difference =>
                !allowed.Any(path => path.IsSameOrAncestorOf(difference.Path)))
                .ToArray();
        }

        List<ProjectDiagnostic> diagnostics = [];
        foreach (JsonSemanticDifference difference in unauthorized)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.UnauthorizedGeneratedDifference,
                $"Generated output contains an unauthorized {difference.Kind} difference at {difference.Path}.",
                new ProjectLocation(relativePath, difference.Path.ToString())));
        }

        return new MutationVerificationResult(
            unauthorized.Length == 0,
            actualDifferences,
            unauthorized,
            diagnostics);
    }

    private static byte[] BuildExpectedOutput(
        ReadOnlySpan<byte> original,
        IReadOnlyList<DocumentChange> authorizedChanges)
    {
        JsonNode? root = JsonNode.Parse(Utf8JsonInput.ToBomlessArray(original));
        foreach (DocumentChange change in authorizedChanges)
        {
            root = ApplyChange(root, change);
        }

        string json = root?.ToJsonString() ?? "null";
        return Encoding.UTF8.GetBytes(json);
    }

    private static JsonNode? ApplyChange(JsonNode? root, DocumentChange change)
    {
        IReadOnlyList<string> segments = change.Path.Segments();
        if (segments.Count == 0)
        {
            return change.Kind is DocumentChangeKind.RemoveProperty or DocumentChangeKind.RemoveArrayElement
                ? null
                : ParseSnapshot(change.NewValue);
        }

        JsonNode parent = ResolveParent(root, segments);
        string final = segments[^1];
        switch (change.Kind)
        {
            case DocumentChangeKind.SetValue:
            case DocumentChangeKind.ReplaceSubtree:
            case DocumentChangeKind.AddProperty:
                SetChild(parent, final, ParseSnapshot(change.NewValue));
                break;
            case DocumentChangeKind.RemoveProperty:
                if (parent is not JsonObject objectParent)
                {
                    throw new InvalidOperationException("A property removal did not target an object.");
                }
                objectParent.Remove(final);
                break;
            case DocumentChangeKind.InsertArrayElement:
                if (parent is not JsonArray insertArray || !TryIndex(final, out int insertIndex))
                {
                    throw new InvalidOperationException("An array insertion did not target an array index.");
                }
                insertArray.Insert(insertIndex, ParseSnapshot(change.NewValue));
                break;
            case DocumentChangeKind.RemoveArrayElement:
                if (parent is not JsonArray removeArray || !TryIndex(final, out int removeIndex))
                {
                    throw new InvalidOperationException("An array removal did not target an array index.");
                }
                removeArray.RemoveAt(removeIndex);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }

        return root;
    }

    private static JsonNode ResolveParent(JsonNode? root, IReadOnlyList<string> segments)
    {
        JsonNode current = root ?? throw new InvalidOperationException("The JSON root is null.");
        for (int index = 0; index < segments.Count - 1; index++)
        {
            string segment = segments[index];
            current = current switch
            {
                JsonObject objectNode => objectNode[segment]
                    ?? throw new InvalidOperationException("A JSON object path was not found: " + segment),
                JsonArray arrayNode when TryIndex(segment, out int arrayIndex) => arrayNode[arrayIndex]
                    ?? throw new InvalidOperationException("A JSON array path was null: " + segment),
                _ => throw new InvalidOperationException("The authorized mutation path could not be resolved.")
            };
        }

        return current;
    }

    private static void SetChild(JsonNode parent, string segment, JsonNode? value)
    {
        if (parent is JsonObject objectNode)
        {
            objectNode[segment] = value;
            return;
        }

        if (parent is JsonArray arrayNode && TryIndex(segment, out int index))
        {
            arrayNode[index] = value;
            return;
        }

        throw new InvalidOperationException("The authorized mutation target was neither an object property nor an array element.");
    }

    private static JsonNode? ParseSnapshot(JsonValueSnapshot? snapshot)
    {
        if (snapshot?.RawJson is null) return null;
        return JsonNode.Parse(snapshot.RawJson);
    }

    private static bool TryIndex(string value, out int index) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
}
