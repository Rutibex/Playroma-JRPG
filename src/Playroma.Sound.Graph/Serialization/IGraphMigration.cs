using System.Text.Json.Nodes;

namespace Playroma.Sound.Graph.Serialization;

public interface IGraphMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonObject Migrate(JsonObject document);
}
