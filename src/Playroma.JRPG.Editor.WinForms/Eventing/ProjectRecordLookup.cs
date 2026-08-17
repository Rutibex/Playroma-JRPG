using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>
/// In-memory database display-name lookup used by event command formatting. It is populated from
/// the shared mutable project documents, so it reflects unsaved Database edits when refreshed.
/// </summary>
public sealed class ProjectRecordLookup
{
    private readonly MvProjectSession _session;
    private readonly Dictionary<(MvDocumentKind Kind, int Id), string> _names = [];

    public ProjectRecordLookup(MvProjectSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<(MvDocumentKind Kind, int Id), string> next = [];
        foreach (MvDocumentKind kind in SupportedKinds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(kind);
            if (summary is null) continue;
            try
            {
                await using IProjectDocumentLease lease = await _session.Workspace
                    .AcquireDocumentAsync(summary.RelativePath, cancellationToken)
                    .ConfigureAwait(true);
                ReadOnlyMemory<byte> bytes = await lease.Document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true);
                using JsonDocument json = JsonDocument.Parse(bytes);
                if (json.RootElement.ValueKind != JsonValueKind.Array) continue;
                for (int id = 0; id < json.RootElement.GetArrayLength(); id++)
                {
                    JsonElement record = json.RootElement[id];
                    if (record.ValueKind != JsonValueKind.Object) continue;
                    if (!record.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String) continue;
                    next[(kind, id)] = name.GetString() ?? string.Empty;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A missing optional database file should not prevent the Event Editor from opening.
            }
        }
        _names.Clear();
        foreach (KeyValuePair<(MvDocumentKind Kind, int Id), string> pair in next)
        {
            _names[pair.Key] = pair.Value;
        }
    }

    public string Resolve(string target, int id)
    {
        MvDocumentKind? kind = target.ToLowerInvariant() switch
        {
            "actor" => MvDocumentKind.Actors,
            "class" => MvDocumentKind.Classes,
            "skill" => MvDocumentKind.Skills,
            "item" => MvDocumentKind.Items,
            "weapon" => MvDocumentKind.Weapons,
            "armor" => MvDocumentKind.Armors,
            "enemy" => MvDocumentKind.Enemies,
            "troop" => MvDocumentKind.Troops,
            "state" => MvDocumentKind.States,
            "animation" => MvDocumentKind.Animations,
            "tileset" => MvDocumentKind.Tilesets,
            _ => null
        };
        return kind.HasValue && _names.TryGetValue((kind.Value, id), out string? name) ? name : string.Empty;
    }

    private static readonly MvDocumentKind[] SupportedKinds =
    [
        MvDocumentKind.Actors,
        MvDocumentKind.Classes,
        MvDocumentKind.Skills,
        MvDocumentKind.Items,
        MvDocumentKind.Weapons,
        MvDocumentKind.Armors,
        MvDocumentKind.Enemies,
        MvDocumentKind.Troops,
        MvDocumentKind.States,
        MvDocumentKind.Animations,
        MvDocumentKind.Tilesets
    ];
}
