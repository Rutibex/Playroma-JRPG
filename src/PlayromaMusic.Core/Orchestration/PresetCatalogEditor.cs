namespace PlayromaMusic.Core.Orchestration;

public enum PresetAuditionPhraseKind
{
    ChromaticRange,
    Chords,
    BassLine,
    LeadMelody,
    StaccatoPulse,
    SustainedPad,
    DrumPattern
}

public sealed class PresetOverrideDefinition
{
    public string PresetKey { get; set; } = string.Empty;
    public InstrumentFamily? Family { get; set; }
    public List<string> AddTags { get; set; } = new();
    public List<string> RemoveTags { get; set; } = new();
    public int? SuggestedLowNote { get; set; }
    public int? SuggestedHighNote { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsFavorite { get; set; }
    public bool? IsExcluded { get; set; }
    public TimbreProfile? Timbre { get; set; }
}

public static class PresetCatalogEditor
{
    public static InstrumentPresetDescriptor Apply(
        InstrumentCatalogDocument catalog,
        PresetOverrideDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.PresetKey))
            throw new ArgumentException("A preset key is required.", nameof(definition));

        InstrumentPresetDescriptor preset = catalog.Presets.FirstOrDefault(item =>
            item.Key.Equals(definition.PresetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown preset: {definition.PresetKey}.");

        if (definition.Family.HasValue) preset.Family = definition.Family.Value;
        HashSet<string> tags = preset.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in definition.RemoveTags.Where(item => !string.IsNullOrWhiteSpace(item))) tags.Remove(tag.Trim());
        foreach (string tag in definition.AddTags.Where(item => !string.IsNullOrWhiteSpace(item))) tags.Add(tag.Trim().ToLowerInvariant());
        preset.Tags = tags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

        if (definition.SuggestedLowNote.HasValue) preset.SuggestedLowNote = Math.Clamp(definition.SuggestedLowNote.Value, 0, 127);
        if (definition.SuggestedHighNote.HasValue) preset.SuggestedHighNote = Math.Clamp(definition.SuggestedHighNote.Value, 0, 127);
        if (preset.SuggestedLowNote > preset.SuggestedHighNote)
            (preset.SuggestedLowNote, preset.SuggestedHighNote) = (preset.SuggestedHighNote, preset.SuggestedLowNote);

        if (definition.IsEnabled.HasValue) preset.IsEnabled = definition.IsEnabled.Value;
        if (definition.IsFavorite.HasValue) preset.IsFavorite = definition.IsFavorite.Value;
        if (definition.IsExcluded.HasValue) preset.IsExcluded = definition.IsExcluded.Value;
        if (definition.Timbre is not null)
        {
            preset.Timbre = Sanitize(definition.Timbre);
            preset.WasAudioProfiled = true;
        }
        return preset;
    }

    public static int ApplyRange(InstrumentCatalogDocument catalog, IEnumerable<PresetOverrideDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        int count = 0;
        foreach (PresetOverrideDefinition definition in definitions)
        {
            Apply(catalog, definition);
            count++;
        }
        return count;
    }

    private static TimbreProfile Sanitize(TimbreProfile source) => new()
    {
        Attack = Math.Clamp(source.Attack, 0, 1),
        Sustain = Math.Clamp(source.Sustain, 0, 1),
        Brightness = Math.Clamp(source.Brightness, 0, 1),
        Density = Math.Clamp(source.Density, 0, 1),
        Percussiveness = Math.Clamp(source.Percussiveness, 0, 1),
        PitchStability = Math.Clamp(source.PitchStability, 0, 1),
        Confidence = Math.Clamp(source.Confidence, 0, 1)
    };
}
