using System.Text.Json;
using System.Text.Json.Serialization;
using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Core.ReferenceAnalysis;

public static class ReferenceBlueprintSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string ToJson(ReferenceCompositionBlueprint blueprint, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return JsonSerializer.Serialize(blueprint, new JsonSerializerOptions(Options) { WriteIndented = indented });
    }

    public static ReferenceCompositionBlueprint FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ReferenceCompositionBlueprint blueprint = JsonSerializer.Deserialize<ReferenceCompositionBlueprint>(json, Options)
            ?? throw new InvalidDataException("Reference blueprint JSON did not contain a document.");
        ReferenceBlueprintEditor.Normalize(blueprint);
        return blueprint;
    }

    public static void Save(ReferenceCompositionBlueprint blueprint, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        File.WriteAllText(fullPath, ToJson(blueprint));
    }

    public static ReferenceCompositionBlueprint Load(string filePath) => FromJson(File.ReadAllText(filePath));

    public static ReferenceCompositionBlueprint Clone(ReferenceCompositionBlueprint blueprint) =>
        FromJson(ToJson(blueprint, indented: false));

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class ReferenceBlueprintEditor
{
    public static void Normalize(ReferenceCompositionBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        blueprint.Source ??= new ReferenceSourceInfo();
        blueprint.Form ??= new ReferenceFormProfile();
        blueprint.Harmony ??= new ReferenceHarmonyProfile();
        blueprint.Motifs ??= new ReferenceMotifProfile();
        blueprint.Rhythm ??= new ReferenceRhythmProfile();
        blueprint.Arrangement ??= new ReferenceArrangementProfile();
        blueprint.Instrumentation ??= new ReferenceInstrumentationProfile();
        blueprint.Performance ??= new ReferencePerformanceProfile();
        blueprint.Tension ??= new ReferenceTensionProfile();
        blueprint.Confidence ??= new ReferenceAnalysisConfidence();
        blueprint.OriginalitySignature ??= new ReferenceSourceSignature();
        blueprint.Generation ??= new ReferenceGenerationPolicy();
        blueprint.Generation.Preservation ??= ReferenceGenerationPolicy.DefaultPreservation();
        foreach (ReferenceBlueprintDimension dimension in Enum.GetValues<ReferenceBlueprintDimension>())
        {
            if (!blueprint.Generation.Preservation.ContainsKey(dimension)) blueprint.Generation.Preservation[dimension] = 0;
            blueprint.Generation.Preservation[dimension] = Math.Clamp(blueprint.Generation.Preservation[dimension], 0, 1);
        }
        blueprint.Generation.Originality ??= new ReferenceOriginalitySettings();
        blueprint.StyleHints ??= new List<string>();
        blueprint.Observations ??= new List<string>();
        blueprint.Form.Sections ??= new List<ReferenceSectionProfile>();
        blueprint.Form.Families ??= new List<ReferenceSectionFamily>();
        blueprint.Harmony.KeyRegions ??= new List<ReferenceKeyRegion>();
        blueprint.Harmony.Events ??= new List<ReferenceHarmonicEvent>();
        blueprint.Harmony.CadenceBars ??= new List<int>();
        blueprint.Motifs.Motifs ??= new List<ReferenceMotifPattern>();
        blueprint.Motifs.PreferredIntervals ??= new List<int>();
        blueprint.Motifs.PreferredRhythmUnits ??= new List<int>();
        blueprint.Rhythm.RoleGrooves ??= new List<ReferenceRoleGroove>();
        blueprint.Rhythm.CommonDurationUnits ??= new List<int>();
        blueprint.Rhythm.FillBars ??= new List<int>();
        blueprint.Arrangement.Sections ??= new List<ReferenceSectionArrangement>();
        blueprint.Instrumentation.Tracks ??= new List<ReferenceTrackProfile>();
        blueprint.Performance.Controllers ??= new List<ReferenceControllerProfile>();
        blueprint.Performance.Roles ??= new List<ReferenceRolePerformance>();
        blueprint.Tension.Points ??= new List<ReferenceTensionPoint>();
        blueprint.OriginalitySignature.ExactNoteTokens ??= new List<string>();
        blueprint.OriginalitySignature.IntervalNGrams ??= new List<string>();
        blueprint.OriginalitySignature.RhythmNGrams ??= new List<string>();
        blueprint.OriginalitySignature.ChordTokens ??= new List<string>();
        blueprint.OriginalitySignature.MotifContourTokens ??= new List<string>();
        foreach (ReferenceSectionFamily family in blueprint.Form.Families) family.SectionIds ??= new List<string>();
        foreach (ReferenceHarmonicEvent chord in blueprint.Harmony.Events) chord.PitchClasses ??= new List<int>();
        foreach (ReferenceMotifPattern motif in blueprint.Motifs.Motifs)
        {
            motif.Intervals ??= new List<int>();
            motif.RhythmUnits ??= new List<int>();
            motif.Contour ??= new List<int>();
            motif.ScaleDegrees ??= new List<int>();
            motif.Occurrences ??= new List<ReferenceMotifOccurrence>();
        }
        foreach (ReferenceRoleGroove groove in blueprint.Rhythm.RoleGrooves)
        {
            groove.OnsetProbabilities ??= new List<double>();
            groove.AccentProfile ??= new List<double>();
            groove.RepresentativePattern ??= new List<int>();
        }
        foreach (ReferenceSectionArrangement section in blueprint.Arrangement.Sections) section.Roles ??= new List<ReferenceRoleActivity>();
        blueprint.Form.Sections = blueprint.Form.Sections.OrderBy(item => item.StartBar).ToList();
        RecalculateSectionStarts(blueprint);
        blueprint.Form.TotalBars = Math.Max(1, blueprint.Form.Sections.Sum(item => Math.Max(1, item.LengthBars)));
        blueprint.Source.BarCount = Math.Max(blueprint.Source.BarCount, blueprint.Form.TotalBars);
        blueprint.Rhythm.StepsPerBar = Math.Clamp(blueprint.Rhythm.StepsPerBar, 4, 64);
        blueprint.Source.TicksPerQuarterNote = Math.Clamp(blueprint.Source.TicksPerQuarterNote, 24, 32767);
    }

    public static void SetTrackRole(ReferenceCompositionBlueprint blueprint, string trackId, TrackRole role)
    {
        ReferenceTrackProfile track = blueprint.Instrumentation.Tracks.FirstOrDefault(item => item.TrackId == trackId)
            ?? throw new KeyNotFoundException($"Unknown reference track '{trackId}'.");
        track.InferredRole = role;
        track.RoleConfidence = 1;
        track.UserOverrodeRole = true;
        foreach (ReferenceSectionArrangement section in blueprint.Arrangement.Sections)
        {
            foreach (ReferenceRoleActivity activity in section.Roles.Where(item => item.SourceTrackId == trackId)) activity.Role = role;
        }
        foreach (ReferenceRoleGroove groove in blueprint.Rhythm.RoleGrooves.Where(item => item.SourceTrackId == trackId)) groove.Role = role;
    }

    public static void SetTrackIgnored(ReferenceCompositionBlueprint blueprint, string trackId, bool ignored)
    {
        ReferenceTrackProfile track = blueprint.Instrumentation.Tracks.FirstOrDefault(item => item.TrackId == trackId)
            ?? throw new KeyNotFoundException($"Unknown reference track '{trackId}'.");
        track.Ignored = ignored;
    }

    public static void ResizeSection(ReferenceCompositionBlueprint blueprint, string sectionId, int lengthBars)
    {
        ReferenceSectionProfile section = blueprint.Form.Sections.FirstOrDefault(item => item.Id == sectionId)
            ?? throw new KeyNotFoundException($"Unknown reference section '{sectionId}'.");
        section.LengthBars = Math.Clamp(lengthBars, 1, 64);
        section.UserEdited = true;
        RecalculateSectionStarts(blueprint);
    }

    public static void SplitSection(ReferenceCompositionBlueprint blueprint, string sectionId, int firstLengthBars)
    {
        int index = blueprint.Form.Sections.FindIndex(item => item.Id == sectionId);
        if (index < 0) throw new KeyNotFoundException($"Unknown reference section '{sectionId}'.");
        ReferenceSectionProfile source = blueprint.Form.Sections[index];
        if (firstLengthBars <= 0 || firstLengthBars >= source.LengthBars) throw new ArgumentOutOfRangeException(nameof(firstLengthBars));
        ReferenceSectionProfile second = new()
        {
            Id = source.Id + "-split",
            Label = source.Label + "b",
            FamilyId = source.FamilyId,
            Kind = source.Kind,
            StartBar = source.StartBar + firstLengthBars,
            LengthBars = source.LengthBars - firstLengthBars,
            VariationIndex = source.VariationIndex + 1,
            Energy = source.Energy,
            Density = source.Density,
            MeanPitch = source.MeanPitch,
            MeanVelocity = source.MeanVelocity,
            BoundaryConfidence = 1,
            UserEdited = true
        };
        source.LengthBars = firstLengthBars;
        source.Label += "a";
        source.UserEdited = true;
        blueprint.Form.Sections.Insert(index + 1, second);
        RecalculateSectionStarts(blueprint);
    }

    public static void MergeSectionWithNext(ReferenceCompositionBlueprint blueprint, string sectionId)
    {
        int index = blueprint.Form.Sections.FindIndex(item => item.Id == sectionId);
        if (index < 0 || index >= blueprint.Form.Sections.Count - 1) throw new InvalidOperationException("The selected section has no following section to merge.");
        ReferenceSectionProfile first = blueprint.Form.Sections[index];
        ReferenceSectionProfile second = blueprint.Form.Sections[index + 1];
        int combined = first.LengthBars + second.LengthBars;
        first.Energy = (first.Energy * first.LengthBars + second.Energy * second.LengthBars) / Math.Max(1, combined);
        first.Density = (first.Density * first.LengthBars + second.Density * second.LengthBars) / Math.Max(1, combined);
        first.MeanPitch = (first.MeanPitch * first.LengthBars + second.MeanPitch * second.LengthBars) / Math.Max(1, combined);
        first.MeanVelocity = (first.MeanVelocity * first.LengthBars + second.MeanVelocity * second.LengthBars) / Math.Max(1, combined);
        first.LengthBars = combined;
        first.Label = first.Label + "+" + second.Label;
        first.UserEdited = true;
        blueprint.Form.Sections.RemoveAt(index + 1);
        RecalculateSectionStarts(blueprint);
    }

    public static void SetPreservation(ReferenceCompositionBlueprint blueprint, ReferenceBlueprintDimension dimension, double amount) =>
        blueprint.Generation.Preservation[dimension] = Math.Clamp(amount, 0, 1);

    public static void RecalculateSectionStarts(ReferenceCompositionBlueprint blueprint)
    {
        int start = 0;
        foreach (ReferenceSectionProfile section in blueprint.Form.Sections)
        {
            section.StartBar = start;
            section.LengthBars = Math.Max(1, section.LengthBars);
            start += section.LengthBars;
        }
        blueprint.Form.TotalBars = Math.Max(1, start);
    }
}
