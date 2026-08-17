using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playroma.Sound.Streaming.Snapshots;

public sealed class SoundSceneSnapshotSerializer
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IncludeFields = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Serialize(SoundSceneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, _options);
    }

    public SoundSceneSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        SoundSceneSnapshot snapshot = JsonSerializer.Deserialize<SoundSceneSnapshot>(json, _options)
            ?? throw new InvalidDataException("Snapshot JSON was empty.");
        if (snapshot.FormatVersion < 1)
            throw new NotSupportedException($"Snapshot format {snapshot.FormatVersion} is not supported.");
        if (snapshot.FormatVersion > SoundSceneSnapshot.CurrentFormatVersion)
            throw new NotSupportedException($"Snapshot format {snapshot.FormatVersion} is newer than this runtime supports.");
        snapshot.Parameters ??= [];
        snapshot.StateMachines ??= [];
        snapshot.Layers ??= [];
        snapshot.Emitters ??= [];
        snapshot.EventClusters ??= [];
        snapshot.Voices ??= [];
        return snapshot;
    }
}
