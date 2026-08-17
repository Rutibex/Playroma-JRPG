using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class MetadataRecommendationService
{
    public TileGameplayMetadata Recommend(string name, IEnumerable<string>? tags = null)
    {
        var text = (name + " " + string.Join(' ', tags ?? Array.Empty<string>())).ToLowerInvariant();
        var metadata = new TileGameplayMetadata();
        if (ContainsAny(text, "wall", "cliff", "rock face", "deep water", "ocean", "pit", "chasm"))
            metadata.Passage = PassageMode.Impassable;
        if (ContainsAny(text, "canopy", "tree top", "arch", "overhead", "roof overhang"))
            metadata.Passage = PassageMode.AboveCharacters;
        if (ContainsAny(text, "ladder", "climb")) metadata.Ladder = true;
        if (ContainsAny(text, "bush", "tall grass", "shrub")) metadata.Bush = true;
        if (ContainsAny(text, "counter", "bar top", "shop desk")) metadata.Counter = true;
        if (ContainsAny(text, "lava", "acid", "poison", "spikes", "damage", "fire floor"))
        {
            metadata.DamageFloor = true;
            metadata.Passage = PassageMode.Passable;
        }
        metadata.SemanticType = InferSemantic(text);
        metadata.FootstepType = InferFootstep(text);
        return metadata;
    }

    public IReadOnlyList<string> DescribeDifferences(TileGameplayMetadata current, TileGameplayMetadata recommended)
    {
        var changes = new List<string>();
        if (current.Passage != recommended.Passage) changes.Add($"Passage: {current.Passage} → {recommended.Passage}");
        if (current.Ladder != recommended.Ladder) changes.Add($"Ladder: {recommended.Ladder}");
        if (current.Bush != recommended.Bush) changes.Add($"Bush: {recommended.Bush}");
        if (current.Counter != recommended.Counter) changes.Add($"Counter: {recommended.Counter}");
        if (current.DamageFloor != recommended.DamageFloor) changes.Add($"Damage floor: {recommended.DamageFloor}");
        if (!string.Equals(current.SemanticType, recommended.SemanticType, StringComparison.OrdinalIgnoreCase))
            changes.Add($"Semantic type: {recommended.SemanticType}");
        if (!string.Equals(current.FootstepType, recommended.FootstepType, StringComparison.OrdinalIgnoreCase))
            changes.Add($"Footstep: {recommended.FootstepType}");
        return changes;
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static string InferSemantic(string text)
    {
        foreach (var pair in new[]
                 {
                     ("water", new[] { "water", "river", "ocean", "pond" }),
                     ("wall", new[] { "wall", "cliff" }),
                     ("door", new[] { "door", "gate", "airlock" }),
                     ("vegetation", new[] { "tree", "grass", "plant", "bush" }),
                     ("furniture", new[] { "chair", "table", "bed", "shelf" }),
                     ("hazard", new[] { "lava", "acid", "poison", "spike", "trap" }),
                     ("road", new[] { "road", "path", "sidewalk" })
                 })
            if (pair.Item2.Any(text.Contains)) return pair.Item1;
        return string.Empty;
    }

    private static string InferFootstep(string text)
    {
        foreach (var pair in new[]
                 {
                     ("water", new[] { "water", "puddle", "swamp" }),
                     ("wood", new[] { "wood", "plank", "parquet" }),
                     ("stone", new[] { "stone", "brick", "cobble", "concrete" }),
                     ("metal", new[] { "metal", "steel", "grate", "panel" }),
                     ("grass", new[] { "grass", "moss", "plant" }),
                     ("sand", new[] { "sand", "desert" }),
                     ("snow", new[] { "snow", "ice" }),
                     ("dirt", new[] { "dirt", "mud", "soil" })
                 })
            if (pair.Item2.Any(text.Contains)) return pair.Item1;
        return string.Empty;
    }
}
