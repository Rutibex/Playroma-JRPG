using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.AnimationEditing;

public static class MvAnimationReader
{
    private static readonly HashSet<string> StandardRecordProperties = new(StringComparer.Ordinal)
    {
        "id", "name", "animation1Name", "animation1Hue", "animation2Name", "animation2Hue", "position", "frames", "timings"
    };

    private static readonly HashSet<string> StandardTimingProperties = new(StringComparer.Ordinal)
    {
        "frame", "se", "flashScope", "flashColor", "flashDuration"
    };

    public static MvAnimationSnapshot Read(MutableJsonProjectDocument document, int recordId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Kind != MvDocumentKind.Animations) throw new ArgumentException("An Animations document is required.", nameof(document));
        if (recordId <= 0) throw new ArgumentOutOfRangeException(nameof(recordId));
        JsonPath root = JsonPath.Root.Append(recordId);
        if (document.Syntax.Resolve(root) is not JsonObjectSyntax) throw new InvalidDataException("The selected animation is empty or invalid.");

        IReadOnlyList<MvAnimationFrameSnapshot> frames = ReadFrames(document, root.Append("frames"));
        IReadOnlyList<MvAnimationTimingSnapshot> timings = ReadTimings(document, root.Append("timings"));
        return new MvAnimationSnapshot(
            recordId,
            document.GetString(root.Append("name")) ?? string.Empty,
            document.GetString(root.Append("animation1Name")) ?? string.Empty,
            document.GetInt32(root.Append("animation1Hue")) ?? 0,
            document.GetString(root.Append("animation2Name")) ?? string.Empty,
            document.GetInt32(root.Append("animation2Hue")) ?? 0,
            document.GetInt32(root.Append("position")) ?? 1,
            frames,
            timings,
            ReadExtensionFields(document.GetRawJson(root), StandardRecordProperties),
            document.GetRawJson(root) ?? "{}");
    }

    public static IReadOnlyList<MvAnimationFrameSnapshot> ReadFrames(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax frames) return [];
        List<MvAnimationFrameSnapshot> result = new(frames.Elements.Count);
        for (int frameIndex = 0; frameIndex < frames.Elements.Count; frameIndex++)
        {
            JsonPath framePath = path.Append(frameIndex);
            if (document.Syntax.Resolve(framePath) is not JsonArraySyntax cells)
            {
                result.Add(new MvAnimationFrameSnapshot(frameIndex, [], document.GetRawJson(framePath) ?? "[]"));
                continue;
            }

            List<MvAnimationCellSnapshot> frameCells = new(cells.Elements.Count);
            for (int lane = 0; lane < cells.Elements.Count; lane++)
            {
                JsonPath cellPath = framePath.Append(lane);
                frameCells.Add(ReadCell(document.GetRawJson(cellPath)));
            }
            result.Add(new MvAnimationFrameSnapshot(frameIndex, frameCells, document.GetRawJson(framePath) ?? "[]"));
        }
        return result;
    }

    public static IReadOnlyList<MvAnimationTimingSnapshot> ReadTimings(MutableJsonProjectDocument document, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax.Resolve(path) is not JsonArraySyntax timings) return [];
        List<MvAnimationTimingSnapshot> result = new(timings.Elements.Count);
        for (int timingIndex = 0; timingIndex < timings.Elements.Count; timingIndex++)
        {
            JsonPath timingPath = path.Append(timingIndex);
            string raw = document.GetRawJson(timingPath) ?? "{}";
            JsonObject? timing = ParseObject(raw);
            if (timing is null)
            {
                result.Add(new MvAnimationTimingSnapshot(timingIndex, 0, 0, [0, 0, 0, 0], 5, null, new Dictionary<string, string>(), raw));
                continue;
            }

            MvAnimationAudioSnapshot? se = ReadAudio(timing["se"]);
            IReadOnlyList<double> color = ReadDoubleArray(timing["flashColor"], [0, 0, 0, 0]);
            result.Add(new MvAnimationTimingSnapshot(
                timingIndex,
                ReadInt(timing, "frame", 0),
                ReadInt(timing, "flashScope", 0),
                color,
                ReadInt(timing, "flashDuration", 5),
                se,
                ReadExtensionFields(raw, StandardTimingProperties),
                raw));
        }
        return result;
    }

    public static MvAnimationCellSnapshot ReadCell(string? rawJson)
    {
        string raw = string.IsNullOrWhiteSpace(rawJson) ? "[-1,0,0,100,0,false,255,0]" : rawJson;
        try
        {
            JsonNode? node = JsonNode.Parse(raw);
            if (node is not JsonArray array) return MvAnimationCellSnapshot.Hidden(raw);
            return new MvAnimationCellSnapshot(
                ReadInt(array, 0, -1),
                ReadDouble(array, 1, 0),
                ReadDouble(array, 2, 0),
                ReadDouble(array, 3, 100),
                ReadDouble(array, 4, 0),
                ReadBool(array, 5, false),
                ReadDouble(array, 6, 255),
                ReadInt(array, 7, 0),
                raw);
        }
        catch (JsonException)
        {
            return MvAnimationCellSnapshot.Hidden(raw);
        }
    }

    private static MvAnimationAudioSnapshot? ReadAudio(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return null;
        if (node is not JsonObject audio) return null;
        return new MvAnimationAudioSnapshot(
            ReadString(audio, "name", string.Empty),
            ReadInt(audio, "volume", 90),
            ReadInt(audio, "pitch", 100),
            ReadInt(audio, "pan", 0),
            audio.ToJsonString());
    }

    private static IReadOnlyDictionary<string, string> ReadExtensionFields(string? rawJson, IReadOnlySet<string> standard)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        JsonObject? objectNode = ParseObject(rawJson);
        if (objectNode is null) return result;
        foreach ((string name, JsonNode? value) in objectNode)
        {
            if (!standard.Contains(name)) result[name] = value?.ToJsonString() ?? "null";
        }
        return result;
    }

    private static JsonObject? ParseObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonNode.Parse(raw) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<double> ReadDoubleArray(JsonNode? node, IReadOnlyList<double> fallback)
    {
        if (node is not JsonArray array) return fallback.ToArray();
        double[] result = new double[Math.Max(4, array.Count)];
        for (int index = 0; index < result.Length; index++) result[index] = index < array.Count ? ReadDouble(array, index, 0) : 0;
        return result;
    }

    private static int ReadInt(JsonObject node, string name, int fallback) => node[name] is JsonValue value && value.TryGetValue(out int parsed) ? parsed : fallback;
    private static double ReadDouble(JsonObject node, string name, double fallback) => node[name] is JsonValue value && value.TryGetValue(out double parsed) ? parsed : fallback;
    private static string ReadString(JsonObject node, string name, string fallback) => node[name] is JsonValue value && value.TryGetValue(out string? parsed) ? parsed ?? fallback : fallback;
    private static int ReadInt(JsonArray array, int index, int fallback) => index < array.Count && array[index] is JsonValue value && value.TryGetValue(out int parsed) ? parsed : fallback;
    private static double ReadDouble(JsonArray array, int index, double fallback) => index < array.Count && array[index] is JsonValue value && value.TryGetValue(out double parsed) ? parsed : fallback;
    private static bool ReadBool(JsonArray array, int index, bool fallback)
    {
        if (index >= array.Count || array[index] is not JsonValue value) return fallback;
        if (value.TryGetValue(out bool boolean)) return boolean;
        if (value.TryGetValue(out int numeric)) return numeric != 0;
        return fallback;
    }
}
