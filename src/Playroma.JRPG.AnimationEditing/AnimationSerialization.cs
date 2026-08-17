using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playroma.JRPG.AnimationEditing;

public static class MvAnimationSerializer
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string SerializeFrames(IReadOnlyList<MvAnimationFrameSnapshot> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        JsonArray root = [];
        foreach (MvAnimationFrameSnapshot frame in frames.OrderBy(frame => frame.Index))
        {
            JsonArray raw = ParseArray(frame.RawJson) ?? new JsonArray();
            while (raw.Count < frame.Cells.Count) raw.Add(MvAnimationConstants.HiddenCell.DeepClone());
            while (raw.Count > frame.Cells.Count) raw.RemoveAt(raw.Count - 1);
            for (int lane = 0; lane < frame.Cells.Count; lane++) raw[lane] = SerializeCellNode(frame.Cells[lane]);
            root.Add(raw);
        }
        return root.ToJsonString(Compact);
    }

    public static string SerializeTimings(IReadOnlyList<MvAnimationTimingSnapshot> timings)
    {
        ArgumentNullException.ThrowIfNull(timings);
        JsonArray root = [];
        foreach (MvAnimationTimingSnapshot timing in timings.OrderBy(timing => timing.Index))
        {
            JsonObject raw = ParseObject(timing.RawJson) ?? new JsonObject();
            raw["frame"] = timing.Frame;
            raw["flashScope"] = timing.FlashScope;
            raw["flashColor"] = new JsonArray(timing.FlashColor.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            raw["flashDuration"] = timing.FlashDuration;
            raw["se"] = timing.SoundEffect is null ? null : SerializeAudioNode(timing.SoundEffect);
            root.Add(raw);
        }
        return root.ToJsonString(Compact);
    }

    public static string SerializeCell(MvAnimationCellSnapshot cell) => SerializeCellNode(cell).ToJsonString(Compact);

    public static JsonArray SerializeCellNode(MvAnimationCellSnapshot cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        JsonArray raw = ParseArray(cell.RawJson) ?? new JsonArray();
        while (raw.Count < 8) raw.Add(null);
        raw[0] = cell.Pattern;
        raw[1] = Number(cell.X);
        raw[2] = Number(cell.Y);
        raw[3] = Number(cell.Scale);
        raw[4] = Number(cell.Rotation);
        raw[5] = PreserveMirrorRepresentation(raw[5], cell.Mirror);
        raw[6] = Number(cell.Opacity);
        raw[7] = cell.BlendMode;
        return raw;
    }

    public static JsonObject SerializeAudioNode(MvAnimationAudioSnapshot audio)
    {
        JsonObject raw = ParseObject(audio.RawJson) ?? new JsonObject();
        raw["name"] = audio.Name;
        raw["volume"] = audio.Volume;
        raw["pitch"] = audio.Pitch;
        raw["pan"] = audio.Pan;
        return raw;
    }

    public static string SerializeClipboard(AnimationClipboardEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });

    public static AnimationClipboardEnvelope DeserializeClipboard(string json)
    {
        AnimationClipboardEnvelope? envelope = JsonSerializer.Deserialize<AnimationClipboardEnvelope>(json);
        if (envelope is null || envelope.Format != AnimationClipboardEnvelope.CurrentFormat || envelope.Version != AnimationClipboardEnvelope.CurrentVersion)
        {
            throw new InvalidDataException("The clipboard does not contain a supported Playroma animation payload.");
        }
        return envelope;
    }

    private static JsonNode PreserveMirrorRepresentation(JsonNode? original, bool mirror)
    {
        if (original is JsonValue value && value.TryGetValue(out int _)) return JsonValue.Create(mirror ? 1 : 0)!;
        return JsonValue.Create(mirror)!;
    }

    private static JsonNode Number(double value) => Math.Abs(value - Math.Round(value)) < 0.0000001 ? JsonValue.Create((long)Math.Round(value))! : JsonValue.Create(value)!;
    private static JsonArray? ParseArray(string? raw) { try { return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw) as JsonArray; } catch (JsonException) { return null; } }
    private static JsonObject? ParseObject(string? raw) { try { return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw) as JsonObject; } catch (JsonException) { return null; } }
}

public static class AnimationClipboardAnalyzer
{
    public static AnimationDependencyReport Analyze(
        AnimationClipboardEnvelope envelope,
        IReadOnlySet<string> availableSheets,
        IReadOnlySet<string> availableSoundEffects)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(availableSheets);
        ArgumentNullException.ThrowIfNull(availableSoundEffects);
        List<string> missingSheets = [];
        if (!string.IsNullOrWhiteSpace(envelope.Sheet1) && !availableSheets.Contains(envelope.Sheet1)) missingSheets.Add(envelope.Sheet1);
        if (!string.IsNullOrWhiteSpace(envelope.Sheet2) && !availableSheets.Contains(envelope.Sheet2)) missingSheets.Add(envelope.Sheet2);
        string[] missingSe = envelope.Timings
            .Select(timing => timing.SoundEffect?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !availableSoundEffects.Contains(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        int[] patterns = envelope.Frames.SelectMany(frame => frame.Cells).Select(cell => cell.Pattern)
            .Where(pattern => pattern >= 0 && !MvAnimationSheetMapper.IsStandardPattern(pattern)).Distinct().Order().ToArray();
        return new AnimationDependencyReport(missingSheets, missingSe, patterns);
    }
}
