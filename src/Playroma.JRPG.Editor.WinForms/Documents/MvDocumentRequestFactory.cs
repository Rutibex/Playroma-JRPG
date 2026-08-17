using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Documents;

public static class MvDocumentRequestFactory
{
    public static DocumentRequest CreateDatabaseRootRequest() => new(
        EditorDocumentKind.Database,
        "database",
        DisplayTitle: "Database");

    public static DocumentRequest CreateDatabaseRequest(MvDatabaseFileSummary file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Kind switch
        {
            MvDocumentKind.System => new DocumentRequest(
                EditorDocumentKind.SystemData,
                "database:System",
                file.RelativePath,
                file.Kind,
                DisplayTitle: "System",
                EventSurface: "System"),
            MvDocumentKind.CommonEvents => new DocumentRequest(
                EditorDocumentKind.Database,
                "database:CommonEvents",
                file.RelativePath,
                file.Kind,
                DisplayTitle: "Common Events",
                EventSurface: "CommonEvents"),
            MvDocumentKind.MapInfos => new DocumentRequest(
                EditorDocumentKind.MapHierarchy,
                "maps:hierarchy",
                file.RelativePath,
                file.Kind,
                DisplayTitle: "Map Hierarchy"),
            _ => new DocumentRequest(
                EditorDocumentKind.Database,
                $"database:{file.Kind}",
                file.RelativePath,
                file.Kind,
                DisplayTitle: file.Kind.ToString())
        };
    }

    public static DocumentRequest CreateDatabaseRecordRequest(
        MvDatabaseFileSummary file,
        int recordId)
    {
        DocumentRequest request = CreateDatabaseRequest(file);
        return request with { RecordId = recordId };
    }

    public static DocumentRequest CreateSystemSectionRequest(
        MvDatabaseFileSummary systemFile,
        string section)
    {
        ArgumentNullException.ThrowIfNull(systemFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        return new DocumentRequest(
            EditorDocumentKind.SystemData,
            "database:" + section,
            systemFile.RelativePath,
            MvDocumentKind.System,
            DisplayTitle: section,
            EventSurface: section);
    }
}
