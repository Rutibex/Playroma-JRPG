using System.Text.Json.Nodes;

namespace Playroma.Sound.Graph.Serialization;

/// <summary>
/// Moves early prototype files to graph schema version 2 by adding metadata and an explicit output object.
/// </summary>
public sealed class GraphMigrationV1ToV2 : IGraphMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        JsonObject clone = (JsonObject)document.DeepClone();

        clone["metadata"] ??= new JsonObject();
        clone["displayName"] ??= clone["id"]?.GetValue<string>() ?? "Untitled Sound";
        clone["rootSeed"] ??= 1;

        if (clone["output"] is null)
        {
            string outputNode = clone["masterOutput"]?.GetValue<string>() ?? FindOutputNode(clone);
            clone["output"] = new JsonObject
            {
                ["nodeId"] = outputNode,
                ["portId"] = "out",
                ["enableLatencyCompensation"] = true,
                ["maximumTailSeconds"] = 12
            };
        }

        clone.Remove("masterOutput");
        clone["formatVersion"] = ToVersion;
        return clone;
    }

    private static string FindOutputNode(JsonObject document)
    {
        if (document["nodes"] is not JsonArray nodes)
        {
            return "";
        }

        foreach (JsonNode? node in nodes)
        {
            if (node is JsonObject item && item["typeId"]?.GetValue<string>() == "core.output")
            {
                return item["id"]?.GetValue<string>() ?? "";
            }
        }

        return "";
    }
}
