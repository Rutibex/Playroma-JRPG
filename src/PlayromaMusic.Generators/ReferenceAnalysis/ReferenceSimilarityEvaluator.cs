using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.Validation;

namespace PlayromaMusic.Generators.ReferenceAnalysis;

public sealed class ReferenceSimilarityEvaluator
{
    public ReferenceDescendantEvaluation Evaluate(
        ReferenceCompositionBlueprint blueprint,
        ProjectDocument project,
        ulong seed,
        ReferenceGenerationMode mode,
        int repairPasses = 0)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(project);
        ReferenceSourceSignature generated = BuildSignature(project, blueprint.OriginalitySignature);
        ReferenceOriginalitySettings limits = blueprint.Generation.Originality;
        ReferenceDescendantEvaluation evaluation = new()
        {
            CandidateId = $"descendant-{seed}-{repairPasses}",
            Seed = seed,
            Mode = mode,
            ProjectFingerprint = ProjectFingerprint.Compute(project),
            StructuralMatch = SafeMetric(StructuralMatch(blueprint, project)),
            HarmonicMatch = SafeMetric(HarmonicMatch(blueprint, project)),
            GrooveMatch = SafeMetric(GrooveMatch(blueprint, project)),
            ArrangementMatch = SafeMetric(ArrangementMatch(blueprint, project)),
            TensionMatch = SafeMetric(TensionMatch(blueprint, project)),
            ExactNoteOverlap = SafeMetric(SetOverlap(blueprint.OriginalitySignature.ExactNoteTokens, generated.ExactNoteTokens)),
            IntervalNGramOverlap = SafeMetric(SetOverlap(blueprint.OriginalitySignature.IntervalNGrams, generated.IntervalNGrams)),
            RhythmNGramOverlap = SafeMetric(SetOverlap(blueprint.OriginalitySignature.RhythmNGrams, generated.RhythmNGrams)),
            MotifContourSimilarity = SafeMetric(MaximumContourSimilarity(blueprint.OriginalitySignature.MotifContourTokens, generated.MotifContourTokens)),
            ChordProgressionOverlap = SafeMetric(SequenceNGramOverlap(blueprint.OriginalitySignature.ChordTokens, generated.ChordTokens, 3)),
            RepairPasses = repairPasses
        };
        (double exactLimit, double intervalLimit, double rhythmLimit, double contourLimit, double chordLimit) = EffectiveLimits(blueprint.Generation.OriginalityPolicy, limits);
        evaluation.PassedOriginality = evaluation.ExactNoteOverlap <= exactLimit &&
                                      evaluation.IntervalNGramOverlap <= intervalLimit &&
                                      evaluation.RhythmNGramOverlap <= rhythmLimit &&
                                      evaluation.MotifContourSimilarity <= contourLimit &&
                                      evaluation.ChordProgressionOverlap <= chordLimit;
        evaluation.NoveltyScore = SafeMetric(1.0 - (
            evaluation.ExactNoteOverlap * 0.28 +
            evaluation.IntervalNGramOverlap * 0.24 +
            evaluation.RhythmNGramOverlap * 0.16 +
            evaluation.MotifContourSimilarity * 0.16 +
            evaluation.ChordProgressionOverlap * 0.16));
        evaluation.QualityScore = SafeMetric(QualityScore(project, evaluation));
        if (!evaluation.PassedOriginality)
        {
            if (evaluation.ExactNoteOverlap > exactLimit) evaluation.Diagnostics.Add($"Exact-note overlap {evaluation.ExactNoteOverlap:P1} exceeds {exactLimit:P1}.");
            if (evaluation.IntervalNGramOverlap > intervalLimit) evaluation.Diagnostics.Add($"Interval n-gram overlap {evaluation.IntervalNGramOverlap:P1} exceeds {intervalLimit:P1}.");
            if (evaluation.RhythmNGramOverlap > rhythmLimit) evaluation.Diagnostics.Add($"Rhythm n-gram overlap {evaluation.RhythmNGramOverlap:P1} exceeds {rhythmLimit:P1}.");
            if (evaluation.MotifContourSimilarity > contourLimit) evaluation.Diagnostics.Add($"Motif contour similarity {evaluation.MotifContourSimilarity:P1} exceeds {contourLimit:P1}.");
            if (evaluation.ChordProgressionOverlap > chordLimit) evaluation.Diagnostics.Add($"Chord-progression overlap {evaluation.ChordProgressionOverlap:P1} exceeds {chordLimit:P1}.");
        }
        return evaluation;
    }

    public ReferenceSourceSignature BuildSignature(ProjectDocument project, ReferenceSourceSignature template)
    {
        int quantization = Math.Max(1, template.QuantizationTicks > 0 ? template.QuantizationTicks : project.Song.TicksPerQuarterNote / 8);
        AbsoluteNote[] all = project.Song.EnumerateNotes().Where(item => !item.Track.IsPercussion)
            .OrderBy(item => item.AbsoluteStartTick).ThenBy(item => item.Note.Pitch).ToArray();
        SongTrack? lead = project.Song.Tracks.Where(item => !item.IsPercussion)
            .OrderByDescending(item => item.Role is TrackRole.Lead or TrackRole.CounterMelody)
            .ThenByDescending(item => item.NoteCount).FirstOrDefault();
        AbsoluteNote[] leadNotes = lead is null ? all : EnumerateTrackNotes(lead).OrderBy(item => item.AbsoluteStartTick).ToArray();
        int[] intervals = leadNotes.Skip(1).Select((item, index) => item.Note.Pitch - leadNotes[index].Note.Pitch).ToArray();
        int[] rhythms = leadNotes.Select(item => Math.Max(1, (int)Math.Round(item.Note.DurationTicks / (double)quantization))).ToArray();
        return new ReferenceSourceSignature
        {
            QuantizationTicks = quantization,
            ExactNoteTokens = all.Select(item => $"{item.Track.Role}:{item.Note.Pitch}:{item.AbsoluteStartTick / quantization}:{Math.Max(1, item.Note.DurationTicks / quantization)}")
                .Distinct(StringComparer.Ordinal).Take(20_000).ToList(),
            IntervalNGrams = BuildNGrams(intervals, 4).Take(10_000).ToList(),
            RhythmNGrams = BuildNGrams(rhythms, 4).Take(10_000).ToList(),
            ChordTokens = project.StructuredComposition.HarmonyPlan.OrderBy(item => item.StartBar)
                .Select(item => item.ScaleDegree.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
            MotifContourTokens = project.StructuredComposition.Motifs.Select(item => string.Join(',', item.DegreeOffsets.Skip(1).Select((degree, index) => Math.Sign(degree - item.DegreeOffsets[index]))))
                .Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static double StructuralMatch(ReferenceCompositionBlueprint blueprint, ProjectDocument project)
    {
        ReferenceSectionProfile[] reference = blueprint.Form.Sections.ToArray();
        FormSectionPlan[] generated = project.StructuredComposition.FormPlan.ToArray();
        if (reference.Length == 0 || generated.Length == 0) return 0;
        double count = 1.0 - Math.Min(1, Math.Abs(reference.Length - generated.Length) / (double)Math.Max(reference.Length, generated.Length));
        int pairs = Math.Min(reference.Length, generated.Length);
        double lengths = Enumerable.Range(0, pairs).Average(index =>
            1.0 - Math.Min(1, Math.Abs(reference[index].LengthBars - generated[index].LengthBars) /
                                (double)Math.Max(1, Math.Max(reference[index].LengthBars, generated[index].LengthBars))));
        double kinds = Enumerable.Range(0, pairs).Count(index => reference[index].Kind == generated[index].Kind) / (double)pairs;
        return Math.Clamp(count * 0.35 + lengths * 0.40 + kinds * 0.25, 0, 1);
    }

    private static double HarmonicMatch(ReferenceCompositionBlueprint blueprint, ProjectDocument project)
    {
        int[] reference = blueprint.Harmony.Events.Select(item => item.ScaleDegree).ToArray();
        int[] generated = project.StructuredComposition.HarmonyPlan.OrderBy(item => item.StartBar).Select(item => item.ScaleDegree).ToArray();
        if (reference.Length == 0 || generated.Length == 0) return 0;
        return SequenceNGramOverlap(reference.Select(item => item.ToString()).ToArray(), generated.Select(item => item.ToString()).ToArray(), 2);
    }

    private static double GrooveMatch(ReferenceCompositionBlueprint blueprint, ProjectDocument project)
    {
        if (blueprint.Rhythm.RoleGrooves.Count == 0) return 0.5;
        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / Math.Max(1, meter.Denominator);
        long barTicks = beatTicks * Math.Max(1, meter.Numerator);
        int steps = Math.Max(4, blueprint.Rhythm.StepsPerBar);
        long stepTicks = Math.Max(1, barTicks / steps);
        List<double> matches = new();
        foreach (ReferenceRoleGroove groove in blueprint.Rhythm.RoleGrooves)
        {
            SongTrack[] tracks = project.Song.Tracks.Where(item => item.Role == groove.Role).ToArray();
            if (tracks.Length == 0)
            {
                matches.Add(0);
                continue;
            }
            double[] generated = new double[steps];
            foreach (AbsoluteNote note in tracks.SelectMany(EnumerateTrackNotes))
            {
                int step = (int)Math.Round((note.AbsoluteStartTick % barTicks) / (double)stepTicks) % steps;
                generated[step]++;
            }
            Normalize(generated);
            double[] reference = groove.OnsetProbabilities.Take(steps).Concat(Enumerable.Repeat(0.0, steps)).Take(steps).ToArray();
            Normalize(reference);
            matches.Add(Cosine(reference, generated));
        }
        return matches.DefaultIfEmpty(0.5).Average();
    }

    private static double ArrangementMatch(ReferenceCompositionBlueprint blueprint, ProjectDocument project)
    {
        if (blueprint.Arrangement.Sections.Count == 0 || project.StructuredComposition.FormPlan.Count == 0) return 0.5;
        List<double> matches = new();
        foreach (FormSectionPlan section in project.StructuredComposition.FormPlan)
        {
            ReferenceSectionArrangement reference = MapSection(blueprint, section.StartBar, project.StructuredComposition.FormPlan.Sum(item => item.LengthBars));
            HashSet<TrackRole> expected = reference.Roles.Where(item => item.Active).Select(item => item.Role).ToHashSet();
            ArrangementSectionPlan? arrangementSection = project.AdvancedComposition.ArrangementPlan.FirstOrDefault(item => item.SectionId == section.Id);
            HashSet<TrackRole> actual = arrangementSection is null
                ? project.Song.Tracks.Where(item => item.Clips.Any(clip => clip.StartTick == project.Song.Sections.FirstOrDefault(songSection => songSection.Id == section.Id)?.StartTick && clip.Notes.Count > 0)).Select(item => item.Role).ToHashSet()
                : arrangementSection.Roles.Where(item => item.Active).Select(item => item.Role).ToHashSet();
            int union = expected.Union(actual).Count();
            matches.Add(union == 0 ? 1 : expected.Intersect(actual).Count() / (double)union);
        }
        return matches.Average();
    }

    private static double TensionMatch(ReferenceCompositionBlueprint blueprint, ProjectDocument project)
    {
        if (blueprint.Tension.Points.Count == 0 || project.AdvancedComposition.TensionCurve.Count == 0) return 0.5;
        int bars = Math.Max(1, project.StructuredComposition.FormPlan.Sum(item => item.LengthBars));
        List<double> similarity = new();
        foreach (TensionPoint point in project.AdvancedComposition.TensionCurve)
        {
            int sourceBar = (int)Math.Round(point.Bar / (double)Math.Max(1, bars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
            ReferenceTensionPoint reference = blueprint.Tension.Points.OrderBy(item => Math.Abs(item.Bar - sourceBar)).First();
            similarity.Add(1.0 - Math.Min(1, Math.Abs(point.Value - reference.Tension)));
        }
        return similarity.Average();
    }

    private static ReferenceSectionArrangement MapSection(ReferenceCompositionBlueprint blueprint, int generatedStartBar, int generatedTotalBars)
    {
        int sourceBar = (int)Math.Round(generatedStartBar / (double)Math.Max(1, generatedTotalBars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
        return blueprint.Arrangement.Sections.OrderBy(item => Math.Abs(item.StartBar - sourceBar)).FirstOrDefault() ?? new ReferenceSectionArrangement();
    }

    private static double QualityScore(ProjectDocument project, ReferenceDescendantEvaluation evaluation)
    {
        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid) return 0;
        double noteHealth = Math.Clamp(project.Song.EnumerateNotes().Count() / 500.0, 0.2, 1);
        double trackHealth = project.Song.Tracks.Count == 0 ? 0 : project.Song.Tracks.Count(item => item.NoteCount > 0) / (double)project.Song.Tracks.Count;
        double selectedScores = project.StructuredComposition.CandidateScores.Where(item => item.Selected).Select(item => SafeMetric(item.TotalScore / 100.0, 0.55)).DefaultIfEmpty(0.55).Average();
        double fidelity = evaluation.StructuralMatch * 0.22 + evaluation.HarmonicMatch * 0.14 + evaluation.GrooveMatch * 0.18 + evaluation.ArrangementMatch * 0.20 + evaluation.TensionMatch * 0.14;
        return Math.Clamp(noteHealth * 0.12 + trackHealth * 0.12 + selectedScores * 0.18 + fidelity + evaluation.NoveltyScore * 0.12, 0, 1);
    }

    private static (double Exact, double Interval, double Rhythm, double Contour, double Chord) EffectiveLimits(
        ReferenceOriginalityPolicy policy,
        ReferenceOriginalitySettings limits) => policy switch
    {
        ReferenceOriginalityPolicy.RecognizableRemix => (Math.Max(limits.MaximumExactNoteOverlap, 0.22), Math.Max(limits.MaximumIntervalNGramOverlap, 0.55), Math.Max(limits.MaximumRhythmNGramOverlap, 0.68), Math.Max(limits.MaximumMotifContourSimilarity, 0.90), Math.Max(limits.MaximumChordProgressionOverlap, 0.88)),
        ReferenceOriginalityPolicy.NovelMelody => (Math.Min(limits.MaximumExactNoteOverlap, 0.04), Math.Min(limits.MaximumIntervalNGramOverlap, 0.20), limits.MaximumRhythmNGramOverlap, Math.Min(limits.MaximumMotifContourSimilarity, 0.55), limits.MaximumChordProgressionOverlap),
        ReferenceOriginalityPolicy.NovelHarmonyAndMelody => (Math.Min(limits.MaximumExactNoteOverlap, 0.03), Math.Min(limits.MaximumIntervalNGramOverlap, 0.18), Math.Min(limits.MaximumRhythmNGramOverlap, 0.38), Math.Min(limits.MaximumMotifContourSimilarity, 0.50), Math.Min(limits.MaximumChordProgressionOverlap, 0.42)),
        ReferenceOriginalityPolicy.StructureOnly => (0.01, 0.10, 0.20, 0.30, 0.25),
        _ => (limits.MaximumExactNoteOverlap, limits.MaximumIntervalNGramOverlap, limits.MaximumRhythmNGramOverlap, limits.MaximumMotifContourSimilarity, limits.MaximumChordProgressionOverlap)
    };

    private static double SafeMetric(double value, double fallback = 0) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : Math.Clamp(fallback, 0, 1);

    private static double SetOverlap(IReadOnlyCollection<string> reference, IReadOnlyCollection<string> candidate)
    {
        if (reference.Count == 0 || candidate.Count == 0) return 0;
        HashSet<string> left = reference.ToHashSet(StringComparer.Ordinal);
        int matches = candidate.Count(left.Contains);
        return matches / (double)Math.Max(1, Math.Min(left.Count, candidate.Distinct(StringComparer.Ordinal).Count()));
    }

    private static double SequenceNGramOverlap(IReadOnlyList<string> reference, IReadOnlyList<string> candidate, int size)
    {
        if (reference.Count < size || candidate.Count < size) return 0;
        HashSet<string> left = BuildStringNGrams(reference, size).ToHashSet(StringComparer.Ordinal);
        string[] right = BuildStringNGrams(candidate, size).ToArray();
        return right.Length == 0 ? 0 : right.Count(left.Contains) / (double)Math.Max(1, Math.Min(left.Count, right.Distinct(StringComparer.Ordinal).Count()));
    }

    private static double MaximumContourSimilarity(IReadOnlyList<string> reference, IReadOnlyList<string> candidate)
    {
        double maximum = 0;
        foreach (string left in reference)
        {
            int[] a = ParseInts(left);
            foreach (string right in candidate)
            {
                int[] b = ParseInts(right);
                int count = Math.Min(a.Length, b.Length);
                if (count == 0) continue;
                double similarity = Enumerable.Range(0, count).Count(index => a[index] == b[index]) / (double)Math.Max(a.Length, b.Length);
                maximum = Math.Max(maximum, similarity);
            }
        }
        return maximum;
    }

    private static int[] ParseInts(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(item => int.TryParse(item, out int parsed) ? parsed : 0).ToArray();

    private static IEnumerable<string> BuildNGrams(IReadOnlyList<int> values, int size)
    {
        if (size <= 0 || values.Count < size) yield break;
        for (int index = 0; index + size <= values.Count; index++) yield return string.Join(',', values.Skip(index).Take(size));
    }

    private static IEnumerable<string> BuildStringNGrams(IReadOnlyList<string> values, int size)
    {
        for (int index = 0; index + size <= values.Count; index++) yield return string.Join('|', values.Skip(index).Take(size));
    }

    private static IEnumerable<AbsoluteNote> EnumerateTrackNotes(SongTrack track)
    {
        foreach (SongClip clip in track.Clips)
            foreach (NoteEvent note in clip.Notes)
                yield return new AbsoluteNote(track, clip, note, clip.StartTick + note.StartTick);
    }

    private static double Cosine(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        double dot = 0;
        double a = 0;
        double b = 0;
        for (int index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            dot += left[index] * right[index];
            a += left[index] * left[index];
            b += right[index] * right[index];
        }
        return a <= 0 || b <= 0 ? 0 : dot / Math.Sqrt(a * b);
    }

    private static void Normalize(double[] values)
    {
        double sum = values.Sum();
        if (sum <= 0) return;
        for (int index = 0; index < values.Length; index++) values[index] /= sum;
    }
}
