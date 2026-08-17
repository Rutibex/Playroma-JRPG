using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Validation;

public sealed class Phase10EventValidator : IProjectValidator
{
    private readonly EventListAnalyzer _analyzer = new(EventCommandRegistry.CreateBuiltIn());

    public string Name => "Event authoring";

    public async ValueTask<IReadOnlyList<ProjectValidationDiagnostic>> ValidateAsync(
        ProjectValidationContext context,
        CancellationToken cancellationToken = default)
    {
        List<ProjectValidationDiagnostic> diagnostics = [];
        foreach (ProjectDocumentHandle handle in context.Workspace.Documents.Where(item => item.Kind is MvDocumentKind.Map or MvDocumentKind.CommonEvents or MvDocumentKind.Troops))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMutableProjectDocument opened = await context.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
            if (opened is not MutableJsonProjectDocument document) continue;
            ReadOnlyMemory<byte> generated = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(Utf8JsonInput.ToBomlessArray(generated.Span));
            foreach (EventListAddress address in EnumerateAddresses(handle.Kind, handle.RelativePath, json.RootElement))
            {
                EventListSnapshot snapshot;
                try
                {
                    snapshot = EventListSnapshot.Capture(document, address);
                }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
                {
                    diagnostics.Add(new ProjectValidationDiagnostic(
                        ProjectDiagnostic.Error(
                            DiagnosticCodes.Phase10InvalidParameters,
                            exception.Message,
                            ToLocation(address)),
                        DocumentationKey: "phase10/event-structure"));
                    continue;
                }
                foreach (EventStructureDiagnostic diagnostic in _analyzer.Analyze(snapshot))
                {
                    diagnostics.Add(new ProjectValidationDiagnostic(
                        ToProjectDiagnostic(diagnostic, address),
                        DocumentationKey: "phase10/event-structure"));
                }
            }
        }
        return diagnostics;
    }

    private static IEnumerable<EventListAddress> EnumerateAddresses(
        MvDocumentKind kind,
        string relativePath,
        JsonElement root)
    {
        switch (kind)
        {
            case MvDocumentKind.Map:
            {
                int mapId = ParseMapId(relativePath) ?? 0;
                if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) yield break;
                for (int eventIndex = 1; eventIndex < events.GetArrayLength(); eventIndex++)
                {
                    JsonElement mapEvent = events[eventIndex];
                    if (mapEvent.ValueKind != JsonValueKind.Object || !mapEvent.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) continue;
                    int eventId = mapEvent.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int id) ? id : eventIndex;
                    for (int pageIndex = 0; pageIndex < pages.GetArrayLength(); pageIndex++)
                    {
                        yield return EventListAddress.ForMapEvent(relativePath, mapId, eventId, pageIndex);
                    }
                }
                break;
            }
            case MvDocumentKind.CommonEvents:
                if (root.ValueKind != JsonValueKind.Array) yield break;
                for (int index = 1; index < root.GetArrayLength(); index++)
                {
                    if (root[index].ValueKind == JsonValueKind.Object) yield return EventListAddress.ForCommonEvent(relativePath, index);
                }
                break;
            case MvDocumentKind.Troops:
                if (root.ValueKind != JsonValueKind.Array) yield break;
                for (int troopId = 1; troopId < root.GetArrayLength(); troopId++)
                {
                    JsonElement troop = root[troopId];
                    if (troop.ValueKind != JsonValueKind.Object || !troop.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) continue;
                    for (int pageIndex = 0; pageIndex < pages.GetArrayLength(); pageIndex++)
                    {
                        yield return EventListAddress.ForTroopEvent(relativePath, troopId, pageIndex);
                    }
                }
                break;
        }
    }

    private static ProjectDiagnostic ToProjectDiagnostic(
        EventStructureDiagnostic diagnostic,
        EventListAddress address)
    {
        ProjectLocation location = ToLocation(address, diagnostic.RawIndex);
        return diagnostic.Severity switch
        {
            EventDiagnosticSeverity.Error => ProjectDiagnostic.Error(diagnostic.Code, diagnostic.Message, location),
            EventDiagnosticSeverity.Warning => ProjectDiagnostic.Warning(diagnostic.Code, diagnostic.Message, location),
            _ => ProjectDiagnostic.Information(diagnostic.Code, diagnostic.Message, location)
        };
    }

    private static ProjectLocation ToLocation(EventListAddress address, int? commandIndex = null) => new(
        address.NormalizedRelativePath,
        commandIndex.HasValue ? address.ListPath.Append(commandIndex.Value).ToString() : address.ListPath.ToString(),
        address.MapId,
        address.RecordId,
        address.EventId,
        address.PageIndex,
        commandIndex);

    private static int? ParseMapId(string relativePath)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        return name.StartsWith("Map", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out int id)
            ? id
            : null;
    }
}
