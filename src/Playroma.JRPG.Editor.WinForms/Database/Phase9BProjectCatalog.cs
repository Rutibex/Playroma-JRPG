using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed record Phase9BProjectCatalog(
    MvSystemTypeCatalog SystemTypes,
    IReadOnlyList<DatabaseChoice> Actors,
    IReadOnlyList<DatabaseChoice> Enemies,
    IReadOnlyList<DatabaseChoice> States,
    IReadOnlyList<DatabaseChoice> Skills,
    IReadOnlyList<DatabaseChoice> CommonEvents,
    IReadOnlyList<DatabaseChoice> Animations)
{
    public static Phase9BProjectCatalog Empty { get; } = new(MvSystemTypeCatalog.Empty, [], [], [], [], [], []);

    public string Resolve(ReferenceTargetKind kind, int id) => kind switch
    {
        ReferenceTargetKind.Actor => Resolve(Actors, id, "Actor"),
        ReferenceTargetKind.Enemy => Resolve(Enemies, id, "Enemy"),
        ReferenceTargetKind.State => Resolve(States, id, "State"),
        ReferenceTargetKind.Skill => Resolve(Skills, id, "Skill"),
        ReferenceTargetKind.CommonEvent => Resolve(CommonEvents, id, "Common Event"),
        ReferenceTargetKind.Animation => Resolve(Animations, id, "Animation"),
        _ => SystemTypes.Resolve(kind, id)
    };

    private static string Resolve(IReadOnlyList<DatabaseChoice> choices, int id, string kind)
    {
        DatabaseChoice? choice = choices.FirstOrDefault(item => item.Id == id);
        return choice is null ? $"Unknown {kind} #{id}" : choice.DisplayName;
    }
}

public static class Phase9BProjectCatalogLoader
{
    public static async ValueTask<Phase9BProjectCatalog> LoadAsync(
        MvProjectSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        MvSystemTypeCatalog system = await LoadSystemAsync(session, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> actors = await LoadChoicesAsync(session, MvDocumentKind.Actors, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> enemies = await LoadChoicesAsync(session, MvDocumentKind.Enemies, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> states = await LoadChoicesAsync(session, MvDocumentKind.States, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> skills = await LoadChoicesAsync(session, MvDocumentKind.Skills, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> commonEvents = await LoadChoicesAsync(session, MvDocumentKind.CommonEvents, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DatabaseChoice> animations = await LoadChoicesAsync(session, MvDocumentKind.Animations, cancellationToken).ConfigureAwait(false);
        return new Phase9BProjectCatalog(system, actors, enemies, states, skills, commonEvents, animations);
    }

    public static async ValueTask<MvSystemTypeCatalog> LoadSystemAsync(
        MvProjectSession session,
        CancellationToken cancellationToken = default)
    {
        ProjectDocumentHandle? handle = session.Workspace.Documents.FirstOrDefault(item => item.Kind == MvDocumentKind.System);
        if (handle is null) return MvSystemTypeCatalog.Empty;
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
        return lease.Document is MutableJsonProjectDocument document ? MvSystemTypeCatalog.Read(document) : MvSystemTypeCatalog.Empty;
    }

    public static async ValueTask<IReadOnlyList<DatabaseChoice>> LoadChoicesAsync(
        MvProjectSession session,
        MvDocumentKind kind,
        CancellationToken cancellationToken = default)
    {
        MvDatabaseFileSummary? summary = session.Workspace.Snapshot.Database.Find(kind);
        if (summary is null) return [];
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(summary.RelativePath, cancellationToken).ConfigureAwait(false);
        if (lease.Document is not MutableJsonProjectDocument document || document.Syntax.Root is not JsonArraySyntax array) return [];
        List<DatabaseChoice> result = [];
        for (int id = 1; id < array.Elements.Count; id++)
        {
            if (array.Elements[id] is not JsonObjectSyntax) continue;
            string name = document.GetString(JsonPath.Root.Append(id).Append("name")) ?? $"{kind} {id}";
            result.Add(new DatabaseChoice(id, $"{id:D4} {name}"));
        }
        return result;
    }
}

public sealed class NamedIdPicker : UserControl
{
    private readonly ComboBox _combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private bool _loading;
    private string _unknownPrefix = "Unknown Value";

    public NamedIdPicker()
    {
        Height = 25;
        Controls.Add(_combo);
        _combo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading) ValueChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? ValueChanged;
    public bool AllowNone { get; set; } = true;

    public int SelectedId
    {
        get => (_combo.SelectedItem as NamedIdItem)?.Id ?? 0;
        set
        {
            _loading = true;
            try
            {
                int index = _combo.Items.Cast<NamedIdItem>().ToList().FindIndex(item => item.Id == value);
                if (index < 0)
                {
                    _combo.Items.Add(new NamedIdItem(value, value == 0 && AllowNone ? "None" : $"{_unknownPrefix} #{value}"));
                    index = _combo.Items.Count - 1;
                }
                _combo.SelectedIndex = index;
            }
            finally { _loading = false; }
        }
    }

    public void SetChoices(
        IEnumerable<NamedDatabaseValue> choices,
        string unknownPrefix,
        bool includeNone = true)
    {
        ArgumentNullException.ThrowIfNull(choices);
        int selected = SelectedId;
        _unknownPrefix = unknownPrefix;
        AllowNone = includeNone;
        _loading = true;
        try
        {
            _combo.Items.Clear();
            if (includeNone) _combo.Items.Add(new NamedIdItem(0, "None"));
            foreach (NamedDatabaseValue choice in choices.Where(item => item.Id > 0 || !includeNone).OrderBy(item => item.Id))
            {
                _combo.Items.Add(new NamedIdItem(choice.Id, string.IsNullOrWhiteSpace(choice.Name) ? $"{unknownPrefix} #{choice.Id}" : choice.Name));
            }
        }
        finally { _loading = false; }
        SelectedId = selected;
    }

    public void SetChoices(
        IEnumerable<DatabaseChoice> choices,
        string unknownPrefix,
        bool includeNone = true)
    {
        SetChoices(choices.Select(item => new NamedDatabaseValue(item.Id, item.DisplayName)), unknownPrefix, includeNone);
    }

    private sealed record NamedIdItem(int Id, string Name)
    {
        public override string ToString() => Id == 0 && string.Equals(Name, "None", StringComparison.Ordinal)
            ? Name
            : $"{Id:D4} {Name}";
    }
}
