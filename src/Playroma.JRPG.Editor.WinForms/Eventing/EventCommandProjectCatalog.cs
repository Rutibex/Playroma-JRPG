using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed record NamedProjectValue(int Id, string Name)
{
    public override string ToString() => $"{Id:D4}: {Name}";
}

public interface IEventCommandProjectCatalog
{
    IReadOnlyList<NamedProjectValue> GetValues(string target);
    string Resolve(string target, int id);
    IReadOnlyList<NamedProjectValue> GetMapEvents();
}

/// <summary>
/// Project-aware selector catalogue backed by the shared mutable documents. It exposes record,
/// switch, variable, map, common-event, and current-map event names without reading stale disk data.
/// </summary>
public sealed class EventCommandProjectCatalog : IEventCommandProjectCatalog
{
    private readonly MvProjectSession _session;
    private readonly EventListAddress _address;
    private readonly Dictionary<string, List<NamedProjectValue>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NamedProjectValue> _mapEvents = [];

    public EventCommandProjectCatalog(MvProjectSession session, EventListAddress address)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        _values.Clear();
        _mapEvents.Clear();
        foreach ((string target, MvDocumentKind kind) in DatabaseTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadNamedArrayAsync(target, kind, cancellationToken).ConfigureAwait(true);
        }
        await LoadSystemNamesAsync(cancellationToken).ConfigureAwait(true);
        LoadMaps();
        if (_address.Surface == EventSurfaceKind.MapEvent)
        {
            await LoadCurrentMapEventsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public IReadOnlyList<NamedProjectValue> GetValues(string target) =>
        _values.TryGetValue(target, out List<NamedProjectValue>? values) ? values.ToArray() : [];

    public string Resolve(string target, int id) =>
        GetValues(target).FirstOrDefault(value => value.Id == id)?.Name ?? string.Empty;

    public IReadOnlyList<NamedProjectValue> GetMapEvents() => _mapEvents.ToArray();

    private async ValueTask LoadNamedArrayAsync(
        string target,
        MvDocumentKind kind,
        CancellationToken cancellationToken)
    {
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(kind);
        if (summary is null) return;
        try
        {
            await using IProjectDocumentLease lease = await _session.Workspace
                .AcquireDocumentAsync(summary.RelativePath, cancellationToken)
                .ConfigureAwait(true);
            ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true);
            using JsonDocument json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return;
            List<NamedProjectValue> values = [];
            for (int index = 0; index < json.RootElement.GetArrayLength(); index++)
            {
                JsonElement record = json.RootElement[index];
                if (record.ValueKind != JsonValueKind.Object) continue;
                string name = record.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                values.Add(new NamedProjectValue(index, string.IsNullOrWhiteSpace(name) ? $"{target} {index}" : name));
            }
            _values[target] = values;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _values[target] = [];
        }
    }

    private async ValueTask LoadSystemNamesAsync(CancellationToken cancellationToken)
    {
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.System);
        if (summary is null) return;
        try
        {
            await using IProjectDocumentLease lease = await _session.Workspace
                .AcquireDocumentAsync(summary.RelativePath, cancellationToken)
                .ConfigureAwait(true);
            ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true);
            using JsonDocument json = JsonDocument.Parse(bytes);
            LoadStringArray(json.RootElement, "switches", "Switch");
            LoadStringArray(json.RootElement, "variables", "Variable");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _values["Switch"] = [];
            _values["Variable"] = [];
        }
    }

    private void LoadMaps()
    {
        _values["Map"] = _session.Workspace.Snapshot.Maps
            .OrderBy(map => map.Order)
            .ThenBy(map => map.Id)
            .Select(map => new NamedProjectValue(map.Id, string.IsNullOrWhiteSpace(map.Name) ? $"Map {map.Id:D3}" : map.Name))
            .ToList();
    }

    private async ValueTask LoadCurrentMapEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using IProjectDocumentLease lease = await _session.Workspace
                .AcquireDocumentAsync(_address.NormalizedRelativePath, cancellationToken)
                .ConfigureAwait(true);
            ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true);
            using JsonDocument json = JsonDocument.Parse(bytes);
            if (!json.RootElement.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return;
            for (int index = 0; index < events.GetArrayLength(); index++)
            {
                JsonElement item = events[index];
                if (item.ValueKind != JsonValueKind.Object) continue;
                int id = item.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt32(out int parsed) ? parsed : index;
                string name = item.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                _mapEvents.Add(new NamedProjectValue(id, string.IsNullOrWhiteSpace(name) ? $"Event {id:D3}" : name));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _mapEvents.Clear();
        }
    }

    private void LoadStringArray(JsonElement root, string property, string target)
    {
        if (!root.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            _values[target] = [];
            return;
        }
        List<NamedProjectValue> values = [];
        for (int index = 1; index < array.GetArrayLength(); index++)
        {
            JsonElement item = array[index];
            string name = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty;
            values.Add(new NamedProjectValue(index, string.IsNullOrWhiteSpace(name) ? $"{target} {index:D4}" : name));
        }
        _values[target] = values;
    }

    private static readonly (string Target, MvDocumentKind Kind)[] DatabaseTargets =
    [
        ("Actor", MvDocumentKind.Actors),
        ("Class", MvDocumentKind.Classes),
        ("Skill", MvDocumentKind.Skills),
        ("Item", MvDocumentKind.Items),
        ("Weapon", MvDocumentKind.Weapons),
        ("Armor", MvDocumentKind.Armors),
        ("Enemy", MvDocumentKind.Enemies),
        ("Troop", MvDocumentKind.Troops),
        ("State", MvDocumentKind.States),
        ("Animation", MvDocumentKind.Animations),
        ("Tileset", MvDocumentKind.Tilesets),
        ("CommonEvent", MvDocumentKind.CommonEvents)
    ];
}
