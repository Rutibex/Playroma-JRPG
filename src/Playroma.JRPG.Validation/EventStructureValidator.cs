using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Validation;

public sealed class EventStructureValidator : IProjectValidator
{
    private static readonly HashSet<int> TerminationCodes = [115, 121, 123, 201, 214, 353, 354];

    public string Name => "Event structure";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(handle =>
                     handle.Kind is MvDocumentKind.Map or MvDocumentKind.CommonEvents or MvDocumentKind.Troops))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument document = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(bytes.Span));
            switch (handle.Kind)
            {
                case MvDocumentKind.Map:
                    ValidateMap(json.RootElement, handle.RelativePath, diagnostics);
                    break;
                case MvDocumentKind.CommonEvents:
                    ValidateCommonEvents(json.RootElement, handle.RelativePath, diagnostics);
                    break;
                case MvDocumentKind.Troops:
                    ValidateTroops(json.RootElement, handle.RelativePath, diagnostics);
                    break;
            }
        }
        return diagnostics;
    }

    private static void ValidateMap(
        JsonElement map,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        int? mapId = ParseMapId(relativePath);
        if (!map.TryGetPropertyValue("events", out JsonElement events)) return;
        foreach ((int eventIndex, JsonElement eventRecord) in events.EnumerateIndexedArray())
        {
            if (eventIndex == 0 || eventRecord.ValueKind != JsonValueKind.Object) continue;
            int eventId = eventRecord.GetOptionalInt32("id") ?? eventIndex;
            if (!eventRecord.TryGetPropertyValue("pages", out JsonElement pages)) continue;
            foreach ((int pageIndex, JsonElement page) in pages.EnumerateIndexedArray())
            {
                if (!page.TryGetPropertyValue("list", out JsonElement list)) continue;
                string path = $"/events/{eventIndex}/pages/{pageIndex}/list";
                ValidateCommandList(list, relativePath, path, mapId, eventId, pageIndex, diagnostics);
                int trigger = page.GetOptionalInt32("trigger") ?? 0;
                if (trigger == 3 && !HasApparentTermination(list))
                {
                    diagnostics.Add(new ProjectValidationDiagnostic(
                        ProjectDiagnostic.Warning(
                            DiagnosticCodes.Phase3AutorunTerminationRisk,
                            $"Autorun event {eventId}, page {pageIndex + 1}, has no apparent switch, self-switch, transfer, erase, exit, or scene termination command.",
                            new ProjectLocation(relativePath, path, mapId, EventId: eventId, PageIndex: pageIndex)),
                        DocumentationKey: "phase3/autorun-risk"));
                }
            }
        }
    }

    private static void ValidateCommonEvents(
        JsonElement root,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind != JsonValueKind.Object) continue;
            int id = record.GetOptionalInt32("id") ?? index;
            if (!record.TryGetPropertyValue("list", out JsonElement list)) continue;
            string path = $"/{index}/list";
            ValidateCommandList(list, relativePath, path, null, null, null, diagnostics, id);
            int trigger = record.GetOptionalInt32("trigger") ?? 0;
            if (trigger == 1 && !HasApparentTermination(list))
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase3AutorunTerminationRisk,
                        $"Autorun common event {id} has no apparent termination command.",
                        new ProjectLocation(relativePath, path, RecordId: id)),
                    DocumentationKey: "phase3/autorun-risk"));
            }
        }
    }

    private static void ValidateTroops(
        JsonElement root,
        string relativePath,
        ICollection<ProjectValidationDiagnostic> diagnostics)
    {
        foreach ((int index, JsonElement record) in root.EnumerateIndexedArray())
        {
            if (index == 0 || record.ValueKind != JsonValueKind.Object) continue;
            int id = record.GetOptionalInt32("id") ?? index;
            if (!record.TryGetPropertyValue("pages", out JsonElement pages)) continue;
            foreach ((int pageIndex, JsonElement page) in pages.EnumerateIndexedArray())
            {
                if (!page.TryGetPropertyValue("list", out JsonElement list)) continue;
                ValidateCommandList(list, relativePath, $"/{index}/pages/{pageIndex}/list", null, null, pageIndex, diagnostics, id);
            }
        }
    }

    private static void ValidateCommandList(
        JsonElement list,
        string relativePath,
        string path,
        int? mapId,
        int? eventId,
        int? pageIndex,
        ICollection<ProjectValidationDiagnostic> diagnostics,
        int? recordId = null)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new ProjectValidationDiagnostic(
                ProjectDiagnostic.Error(
                    DiagnosticCodes.Phase3InvalidEventIndentation,
                    "Event command list is not an array.",
                    new ProjectLocation(relativePath, path, mapId, recordId, eventId, pageIndex)),
                DocumentationKey: "phase3/event-structure"));
            return;
        }

        int previousIndent = 0;
        bool first = true;
        foreach ((int commandIndex, JsonElement command) in list.EnumerateIndexedArray())
        {
            int indent = command.GetOptionalInt32("indent") ?? 0;
            bool invalid = indent < 0 || indent > 100 || (first && indent != 0) || (!first && indent > previousIndent + 1);
            if (invalid)
            {
                diagnostics.Add(new ProjectValidationDiagnostic(
                    ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase3InvalidEventIndentation,
                        $"Event command {commandIndex} has invalid indentation {indent}; previous indentation was {previousIndent}.",
                        new ProjectLocation(relativePath, $"{path}/{commandIndex}/indent", mapId, recordId, eventId, pageIndex, commandIndex)),
                    DocumentationKey: "phase3/event-indentation"));
            }
            previousIndent = Math.Max(0, indent);
            first = false;
        }
    }

    private static bool HasApparentTermination(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array) return false;
        foreach (JsonElement command in list.EnumerateArray())
        {
            int code = command.GetOptionalInt32("code") ?? 0;
            if (TerminationCodes.Contains(code)) return true;
        }
        return false;
    }

    private static int? ParseMapId(string relativePath)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        return name.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int id)
            ? id
            : null;
    }
}
