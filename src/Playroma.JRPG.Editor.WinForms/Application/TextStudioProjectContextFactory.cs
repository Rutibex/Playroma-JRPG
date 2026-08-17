using Playroma.JRPG.Application;
using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.Editor.WinForms;

namespace Playroma.JRPG.Editor.WinForms.Application;

internal static class TextStudioProjectContextFactory
{
    public static TextStudioProjectContextSnapshot Create(MvProjectSession session, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        MvProjectSnapshot snapshot = session.Workspace.Snapshot;
        string projectTitle = string.IsNullOrWhiteSpace(snapshot.Identity.ProjectTitle)
            ? Path.GetFileName(snapshot.Identity.ProjectRoot)
            : snapshot.Identity.ProjectTitle!;

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["project.root"] = snapshot.Identity.ProjectRoot,
            ["project.dataRoot"] = snapshot.Identity.DataRoot,
            ["project.title"] = projectTitle,
            ["project.layout"] = snapshot.Layout.Kind.ToString(),
            ["project.editability"] = snapshot.Editability.ToString(),
            ["project.mapCount"] = snapshot.Maps.Count,
            ["project.maps"] = snapshot.Maps.OrderBy(x => x.Order).ThenBy(x => x.Id).Select(x => x.Name).ToArray(),
            ["project.mapIds"] = snapshot.Maps.OrderBy(x => x.Order).ThenBy(x => x.Id).Select(x => x.Id).ToArray(),
            ["project.enabledPluginCount"] = snapshot.Plugins.EnabledCount,
            ["project.enabledPlugins"] = snapshot.Plugins.Entries.Where(x => x.Enabled).OrderBy(x => x.LoadOrder).Select(x => x.Name).ToArray()
        };

        var summary = new List<TextStudioProjectContextItem>
        {
            new("Title", projectTitle, "Project"),
            new("Project root", snapshot.Identity.ProjectRoot, "Project"),
            new("Layout", snapshot.Layout.Kind.ToString(), "Project"),
            new("Editability", snapshot.Editability.ToString(), "Project"),
            new("Maps", snapshot.Maps.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Maps"),
            new("Enabled plugins", snapshot.Plugins.EnabledCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Plugins")
        };

        foreach (MvDatabaseFileSummary file in snapshot.Database.Files.OrderBy(x => x.Kind.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            string key = file.Kind.ToString();
            string camel = char.ToLowerInvariant(key[0]) + key[1..];
            values[$"project.database.{camel}.count"] = file.PopulatedRecordCount;
            values[$"project.database.{camel}.highestId"] = file.HighestRecordId;
            summary.Add(new TextStudioProjectContextItem(
                key,
                $"{file.PopulatedRecordCount:N0} records (highest ID {file.HighestRecordId:N0})",
                "Database"));
        }

        if (snapshot.Maps.Count > 0)
        {
            string mapPreview = string.Join(", ", snapshot.Maps.OrderBy(x => x.Order).ThenBy(x => x.Id).Take(8).Select(x => x.Name));
            if (snapshot.Maps.Count > 8) mapPreview += $", +{snapshot.Maps.Count - 8:N0} more";
            summary.Add(new TextStudioProjectContextItem("Map names", mapPreview, "Maps"));
        }

        var enabledPlugins = snapshot.Plugins.Entries.Where(x => x.Enabled).OrderBy(x => x.LoadOrder).Select(x => x.Name).ToArray();
        if (enabledPlugins.Length > 0)
        {
            string pluginPreview = string.Join(", ", enabledPlugins.Take(8));
            if (enabledPlugins.Length > 8) pluginPreview += $", +{enabledPlugins.Length - 8:N0} more";
            summary.Add(new TextStudioProjectContextItem("Enabled plugin names", pluginPreview, "Plugins"));
        }

        return new TextStudioProjectContextSnapshot(
            snapshot.Identity.ProjectRoot,
            projectTitle,
            revision,
            values,
            summary);
    }
}
