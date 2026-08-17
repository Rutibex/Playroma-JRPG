using System.Text.Json.Nodes;

namespace Playroma.Sound.Streaming.Serialization;

/// <summary>Transforms one .playstream document version into the immediately following version.</summary>
public interface ISoundSceneMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonObject Migrate(JsonObject document);
}
