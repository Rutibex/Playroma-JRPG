using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed record Phase9CProjectCatalog(
    Phase9BProjectCatalog Shared,
    IReadOnlyList<DatabaseChoice> Items,
    IReadOnlyList<DatabaseChoice> Weapons,
    IReadOnlyList<DatabaseChoice> Armors,
    IReadOnlyDictionary<int, int> SkillMpCosts)
{
    public static Phase9CProjectCatalog Empty { get; } = new(Phase9BProjectCatalog.Empty, [], [], [], new Dictionary<int, int>());

    public string ResolveDrop(int kind, int dataId) => kind switch
    {
        1 => Resolve(Items, dataId, "Item"),
        2 => Resolve(Weapons, dataId, "Weapon"),
        3 => Resolve(Armors, dataId, "Armor"),
        _ => "None"
    };

    public IReadOnlyList<DatabaseChoice> ChoicesForDropKind(int kind) => kind switch
    {
        1 => Items,
        2 => Weapons,
        3 => Armors,
        _ => []
    };

    private static string Resolve(IReadOnlyList<DatabaseChoice> values, int id, string label)
    {
        DatabaseChoice? value = values.FirstOrDefault(item => item.Id == id);
        return value?.DisplayName ?? $"Unknown {label} #{id}";
    }
}

public static class Phase9CProjectCatalogLoader
{
    public static async ValueTask<Phase9CProjectCatalog> LoadAsync(
        MvProjectSession session,
        CancellationToken cancellationToken = default)
    {
        Phase9BProjectCatalog shared = await Phase9BProjectCatalogLoader.LoadAsync(session, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> items = await Phase9BProjectCatalogLoader.LoadChoicesAsync(session, MvDocumentKind.Items, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> weapons = await Phase9BProjectCatalogLoader.LoadChoicesAsync(session, MvDocumentKind.Weapons, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> armors = await Phase9BProjectCatalogLoader.LoadChoicesAsync(session, MvDocumentKind.Armors, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<int, int> skillMpCosts = await LoadSkillMpCostsAsync(session, cancellationToken).ConfigureAwait(false);
        return new Phase9CProjectCatalog(shared, items, weapons, armors, skillMpCosts);
    }

    private static async ValueTask<IReadOnlyDictionary<int, int>> LoadSkillMpCostsAsync(
        MvProjectSession session,
        CancellationToken cancellationToken)
    {
        MvDatabaseFileSummary? summary = session.Workspace.Snapshot.Database.Find(MvDocumentKind.Skills);
        if (summary is null) return new Dictionary<int, int>();
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(summary.RelativePath, cancellationToken).ConfigureAwait(false);
        if (lease.Document is not MutableJsonProjectDocument document || document.Syntax.Root is not JsonArraySyntax array)
        {
            return new Dictionary<int, int>();
        }
        Dictionary<int, int> result = [];
        for (int id = 1; id < array.Elements.Count; id++)
        {
            if (array.Elements[id] is not JsonObjectSyntax) continue;
            result[id] = document.GetInt32(JsonPath.Root.Append(id).Append("mpCost")) ?? 0;
        }
        return result;
    }
}
