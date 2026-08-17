using Playroma.JRPG.Application;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class DatabaseCategoryRegistry
{
    private static readonly (MvDocumentKind Kind, string Key, string Title)[] StandardOrder =
    [
        (MvDocumentKind.Actors, "Actors", "Actors"),
        (MvDocumentKind.Classes, "Classes", "Classes"),
        (MvDocumentKind.Skills, "Skills", "Skills"),
        (MvDocumentKind.Items, "Items", "Items"),
        (MvDocumentKind.Weapons, "Weapons", "Weapons"),
        (MvDocumentKind.Armors, "Armors", "Armors"),
        (MvDocumentKind.Enemies, "Enemies", "Enemies"),
        (MvDocumentKind.Troops, "Troops", "Troops"),
        (MvDocumentKind.States, "States", "States"),
        (MvDocumentKind.Animations, "Animations", "Animations"),
        (MvDocumentKind.Tilesets, "Tilesets", "Tilesets"),
        (MvDocumentKind.CommonEvents, "CommonEvents", "Common Events")
    ];

    private readonly Dictionary<string, DatabaseCategoryDescriptor> _byKey;

    private DatabaseCategoryRegistry(IReadOnlyList<DatabaseCategoryDescriptor> categories)
    {
        Categories = categories;
        _byKey = categories.ToDictionary(category => category.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DatabaseCategoryDescriptor> Categories { get; }

    public static DatabaseCategoryRegistry Create(MvProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<DatabaseCategoryDescriptor> categories = [];
        foreach ((MvDocumentKind kind, string key, string title) in StandardOrder)
        {
            MvDatabaseFileSummary? summary = session.Workspace.Snapshot.Database.Find(kind);
            if (summary is null) continue;
            categories.Add(new DatabaseCategoryDescriptor(
                key,
                title,
                kind,
                summary.RelativePath,
                kind == MvDocumentKind.CommonEvents ? DatabaseSection.CommonEvents : DatabaseSection.Records));
        }

        MvDatabaseFileSummary? system = session.Workspace.Snapshot.Database.Find(MvDocumentKind.System);
        if (system is not null)
        {
            categories.Add(new DatabaseCategoryDescriptor(
                "System",
                "System",
                MvDocumentKind.System,
                system.RelativePath,
                DatabaseSection.System,
                PaneKey: "SystemData"));
            categories.Add(new DatabaseCategoryDescriptor(
                "Types",
                "Types",
                MvDocumentKind.System,
                system.RelativePath,
                DatabaseSection.Types,
                PaneKey: "SystemData"));
            categories.Add(new DatabaseCategoryDescriptor(
                "Terms",
                "Terms",
                MvDocumentKind.System,
                system.RelativePath,
                DatabaseSection.Terms,
                PaneKey: "SystemData"));
        }
        return new DatabaseCategoryRegistry(categories);
    }

    public DatabaseCategoryDescriptor? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_byKey.TryGetValue(key, out DatabaseCategoryDescriptor? exact)) return exact;
        return Categories.FirstOrDefault(category =>
            string.Equals(category.DocumentKind.ToString(), key, StringComparison.OrdinalIgnoreCase));
    }

    public DatabaseCategoryDescriptor? Find(MvDocumentKind kind, DatabaseSection section = DatabaseSection.Records) =>
        Categories.FirstOrDefault(category => category.DocumentKind == kind && category.Section == section)
        ?? Categories.FirstOrDefault(category => category.DocumentKind == kind);
}
