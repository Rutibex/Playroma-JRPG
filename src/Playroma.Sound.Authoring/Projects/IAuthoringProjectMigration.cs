using System.Text.Json.Nodes;

namespace Playroma.Sound.Authoring.Projects;

public interface IAuthoringProjectMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonObject Migrate(JsonObject document);
}
