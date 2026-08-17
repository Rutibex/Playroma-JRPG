using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;

namespace PlayromaMusic.Generators.Structured;

public sealed class PhraseCandidateScorer
{
    public PhraseCandidateScore Score(
        ulong masterSeed,
        string sectionId,
        string roleId,
        int phraseIndex,
        int candidateIndex,
        IReadOnlyList<NoteEvent> notes,
        IReadOnlyList<int> motifDegrees,
        PitchClass keyRoot,
        ScaleDefinition scale,
        int minimumPitch,
        int maximumPitch,
        long phraseLengthTicks,
        long beatTicks,
        Func<long, HarmonicEventPlan> harmonyAt,
        StyleConstraintProfile? constraints = null,
        StyleScorerWeights? weights = null,
        double minimumMotifContourSimilarity = 0.0)
    {
        constraints ??= StyleConstraintProfile.Default;
        weights ??= StyleScorerWeights.Default;
        PhraseCandidateScore score = new()
        {
            Id = SeedDerivation.StableId("score", masterSeed, sectionId, roleId, phraseIndex.ToString(), candidateIndex.ToString()),
            SectionId = sectionId,
            RoleId = roleId,
            PhraseIndex = phraseIndex,
            CandidateIndex = candidateIndex,
            PassedHardConstraints = true
        };

        ValidateHardConstraints(notes, minimumPitch, maximumPitch, phraseLengthTicks, constraints, score);
        if (notes.Count == 0)
        {
            score.TotalScore = -1000;
            return score;
        }

        int chordTones = 0;
        foreach (NoteEvent note in notes)
        {
            HarmonicEventPlan harmony = harmonyAt(note.StartTick);
            HashSet<int> chordPitchClasses = new();
            foreach (int offset in new[] { 0, 2, 4, harmony.ChordToneCount >= 4 ? 6 : 4 })
            {
                int pitch = scale.MidiPitchForDegree(keyRoot, harmony.ScaleDegree + offset, 4);
                chordPitchClasses.Add(pitch % 12);
            }
            if (chordPitchClasses.Contains(note.Pitch % 12))
            {
                chordTones++;
            }
        }
        double chordRatio = chordTones / (double)notes.Count;
        score.ChordToneScore = weights.ChordTone * chordRatio;
        double chordTargetPenalty = 0;
        if (chordRatio + 0.0001 < constraints.MinimumChordToneRatio)
        {
            double shortfall = constraints.MinimumChordToneRatio - chordRatio;
            chordTargetPenalty = shortfall * Math.Max(1.0, weights.ChordTone);
            score.Diagnostics.Add($"Chord-tone ratio {chordRatio:0.00} is below the style target {constraints.MinimumChordToneRatio:0.00}; treated as a soft penalty.");
        }

        int stepwise = 0;
        double leapPenalty = 0;
        for (int index = 1; index < notes.Count; index++)
        {
            int leap = Math.Abs(notes[index].Pitch - notes[index - 1].Pitch);
            if (leap <= 4)
            {
                stepwise++;
            }
            if (leap > 7)
            {
                leapPenalty += (leap - 7) * 0.75 * weights.LeapPenalty;
            }
        }
        score.StepwiseMotionScore = notes.Count <= 1 ? weights.StepwiseMotion * 0.45 : weights.StepwiseMotion * stepwise / (notes.Count - 1.0);
        score.LeapPenalty = leapPenalty;

        int motifMatches = 0;
        for (int index = 1; index < Math.Min(notes.Count, motifDegrees.Count); index++)
        {
            int generatedDirection = Math.Sign(notes[index].Pitch - notes[index - 1].Pitch);
            int motifDirection = Math.Sign(motifDegrees[index] - motifDegrees[index - 1]);
            if (generatedDirection == motifDirection)
            {
                motifMatches++;
            }
        }
        double motifContourSimilarity = motifDegrees.Count <= 1
            ? 0.5
            : motifMatches / Math.Max(1.0, Math.Min(notes.Count, motifDegrees.Count) - 1.0);
        score.MotifFidelityScore = weights.MotifFidelity * motifContourSimilarity;
        if (minimumMotifContourSimilarity > 0 && motifContourSimilarity + 0.0001 < minimumMotifContourSimilarity)
        {
            Fail(score, $"Required long-range motif contour similarity {motifContourSimilarity:0.00} is below {minimumMotifContourSimilarity:0.00}.");
        }

        NoteEvent final = notes[^1];
        HarmonicEventPlan finalHarmony = harmonyAt(Math.Max(0, final.StartTick));
        int tonicPc = scale.MidiPitchForDegree(keyRoot, finalHarmony.ScaleDegree, 4) % 12;
        int fifthPc = scale.MidiPitchForDegree(keyRoot, finalHarmony.ScaleDegree + 4, 4) % 12;
        bool cadentialTone = final.Pitch % 12 == tonicPc || final.Pitch % 12 == fifthPc;
        score.PhraseClosureScore = final.Pitch % 12 == tonicPc
            ? weights.PhraseClosure
            : final.Pitch % 12 == fifthPc ? weights.PhraseClosure * 0.69 : weights.PhraseClosure * 0.25;
        if (constraints.RequireCadentialFinalTone && !cadentialTone)
        {
            Fail(score, "Style requires the final note to be a cadential chord tone.");
        }

        int distinctOnsets = notes.Select(note => note.StartTick).Distinct().Count();
        int ideal = Math.Max(2, (int)(phraseLengthTicks / Math.Max(1, beatTicks)) * 2);
        score.RhythmBalanceScore = weights.RhythmBalance * Math.Max(0, 1.0 - Math.Abs(distinctOnsets - ideal) / (double)Math.Max(ideal, 1));

        int offBeatNotes = notes.Count(note => note.StartTick % Math.Max(1, beatTicks) != 0);
        double syncopationScore = weights.Syncopation * offBeatNotes / Math.Max(1.0, notes.Count);

        int repeatedTransitions = 0;
        for (int index = 1; index < notes.Count; index++)
        {
            if (notes[index].Pitch == notes[index - 1].Pitch) repeatedTransitions++;
        }
        double repetitionControlScore = weights.RepetitionControl * (1.0 - repeatedTransitions / Math.Max(1.0, notes.Count - 1.0));

        int range = notes.Max(note => note.Pitch) - notes.Min(note => note.Pitch);
        double rangeQuality = range < constraints.PreferredMinimumRange
            ? Math.Max(0.15, range / (double)Math.Max(1, constraints.PreferredMinimumRange))
            : range <= constraints.PreferredMaximumRange
                ? 1.0
                : Math.Max(0.15, 1.0 - (range - constraints.PreferredMaximumRange) / 24.0);
        score.RangeScore = weights.Range * rangeQuality;

        score.TotalScore = score.ChordToneScore + score.StepwiseMotionScore + score.MotifFidelityScore +
                           score.PhraseClosureScore + score.RhythmBalanceScore + score.RangeScore +
                           syncopationScore + repetitionControlScore - score.LeapPenalty - chordTargetPenalty;
        if (!score.PassedHardConstraints)
        {
            score.TotalScore -= 1000;
        }
        return score;
    }

    private static void ValidateHardConstraints(
        IReadOnlyList<NoteEvent> notes,
        int minimumPitch,
        int maximumPitch,
        long phraseLengthTicks,
        StyleConstraintProfile constraints,
        PhraseCandidateScore score)
    {
        if (notes.Count < constraints.MinimumNotesPerPhrase)
        {
            Fail(score, $"Phrase must contain at least {constraints.MinimumNotesPerPhrase} notes.");
        }
        if (notes.Count > constraints.MaximumNotesPerPhrase)
        {
            Fail(score, $"Phrase exceeds the style maximum of {constraints.MaximumNotesPerPhrase} notes.");
        }

        long previousStart = -1;
        int repeatedPitchRun = 1;
        for (int index = 0; index < notes.Count; index++)
        {
            NoteEvent note = notes[index];
            if (note.Pitch < minimumPitch || note.Pitch > maximumPitch)
            {
                Fail(score, $"Pitch {note.Pitch} is outside the role range.");
            }
            if (note.StartTick < 0 || note.DurationTicks <= 0 || note.EndTick > phraseLengthTicks)
            {
                Fail(score, "A note lies outside the phrase.");
            }
            if (note.StartTick < previousStart)
            {
                Fail(score, "Note onsets are not chronological.");
            }
            previousStart = note.StartTick;

            if (index > 0)
            {
                int leap = Math.Abs(note.Pitch - notes[index - 1].Pitch);
                if (leap > constraints.MaximumMelodicLeap)
                {
                    Fail(score, $"A melodic leap exceeds the style maximum of {constraints.MaximumMelodicLeap} semitones.");
                }
                repeatedPitchRun = note.Pitch == notes[index - 1].Pitch ? repeatedPitchRun + 1 : 1;
                if (repeatedPitchRun > constraints.MaximumRepeatedPitch)
                {
                    Fail(score, $"Repeated-pitch run exceeds the style maximum of {constraints.MaximumRepeatedPitch}.");
                }
            }
        }

        double densityRatio = notes.Sum(note => note.DurationTicks) / (double)Math.Max(1, phraseLengthTicks);
        if (densityRatio > constraints.MaximumDensityRatio * 1.5)
        {
            Fail(score, "Phrase density exceeds the style limit.");
        }
    }

    private static void Fail(PhraseCandidateScore score, string diagnostic)
    {
        score.PassedHardConstraints = false;
        if (!score.Diagnostics.Contains(diagnostic, StringComparer.Ordinal))
        {
            score.Diagnostics.Add(diagnostic);
        }
    }
}
