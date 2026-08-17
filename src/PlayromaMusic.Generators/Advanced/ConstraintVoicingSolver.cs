using PlayromaMusic.Core.AdvancedComposition;

namespace PlayromaMusic.Generators.Advanced;

public sealed record VoicingSolution(
    IReadOnlyList<int> Pitches,
    double Cost,
    int ExploredCandidates,
    IReadOnlyList<string> Diagnostics);

public sealed class ConstraintVoicingSolver
{
    public VoicingSolution Solve(
        IReadOnlyList<int> chordPitches,
        IReadOnlyList<int>? previousVoicing,
        int minimumPitch,
        int maximumPitch,
        ConstraintVoicingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(chordPitches);
        ArgumentNullException.ThrowIfNull(settings);
        if (chordPitches.Count == 0)
        {
            return new VoicingSolution(Array.Empty<int>(), 0, 0, new[] { "Chord contained no pitches." });
        }

        int voices = Math.Clamp(settings.VoiceCount, 2, 6);
        int budget = Math.Max(64, settings.CandidateBudget);
        int[] pitchClasses = chordPitches.Select(pitch => ((pitch % 12) + 12) % 12).Distinct().OrderBy(value => value).ToArray();
        List<int>[] candidates = new List<int>[voices];
        for (int voice = 0; voice < voices; voice++)
        {
            candidates[voice] = Enumerable.Range(minimumPitch, Math.Max(1, maximumPitch - minimumPitch + 1))
                .Where(pitch => pitchClasses.Contains(pitch % 12))
                .ToList();
        }

        int[] current = new int[voices];
        int[]? best = null;
        double bestCost = double.PositiveInfinity;
        int explored = 0;
        Search(0);

        if (best is null)
        {
            List<int> fallback = BuildFallback(chordPitches, minimumPitch, maximumPitch, voices);
            return new VoicingSolution(fallback, 9999, explored, new[] { "No strict voicing satisfied the constraints; a deterministic close-position fallback was used." });
        }

        return new VoicingSolution(best, bestCost, explored, Array.Empty<string>());

        void Search(int voice)
        {
            if (explored >= budget) return;
            if (voice == voices)
            {
                explored++;
                double cost = Score(current, previousVoicing, settings, pitchClasses);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = current.ToArray();
                }
                return;
            }

            foreach (int pitch in candidates[voice])
            {
                if (explored >= budget) break;
                if (voice > 0)
                {
                    if (!settings.AllowVoiceCrossing && pitch <= current[voice - 1]) continue;
                    if (pitch - current[voice - 1] > settings.MaximumAdjacentSpacing) continue;
                }
                if (previousVoicing is { Count: > 0 } && voice < previousVoicing.Count &&
                    Math.Abs(pitch - previousVoicing[voice]) > Math.Max(2, settings.MaximumVoiceLeap))
                {
                    continue;
                }

                current[voice] = pitch;
                Search(voice + 1);
            }
        }
    }

    private static double Score(
        IReadOnlyList<int> voicing,
        IReadOnlyList<int>? previous,
        ConstraintVoicingSettings settings,
        IReadOnlyCollection<int> pitchClasses)
    {
        double cost = 0;
        if (previous is { Count: > 0 })
        {
            for (int index = 0; index < Math.Min(voicing.Count, previous.Count); index++)
            {
                int leap = Math.Abs(voicing[index] - previous[index]);
                cost += leap;
                if (leap > settings.MaximumVoiceLeap) cost += (leap - settings.MaximumVoiceLeap) * 9.0;
                if (settings.PreferCommonTones && voicing[index] == previous[index]) cost -= 4.0;
            }
        }

        for (int index = 1; index < voicing.Count; index++)
        {
            int spacing = voicing[index] - voicing[index - 1];
            if (spacing > 12) cost += (spacing - 12) * 1.5;
            if (spacing < 3) cost += (3 - spacing) * 1.2;
        }

        int distinct = voicing.Select(pitch => pitch % 12).Distinct().Count();
        cost += Math.Max(0, pitchClasses.Count - distinct) * 6.0;
        double center = voicing.Average();
        cost += Math.Abs(center - 60.0) * 0.03;
        return cost;
    }

    private static List<int> BuildFallback(IReadOnlyList<int> chordPitches, int minimum, int maximum, int voices)
    {
        List<int> result = new();
        int cursor = minimum;
        for (int voice = 0; voice < voices; voice++)
        {
            int source = chordPitches[voice % chordPitches.Count];
            int pitch = source;
            while (pitch < cursor) pitch += 12;
            while (pitch > maximum && pitch - 12 >= minimum) pitch -= 12;
            pitch = Math.Clamp(pitch, minimum, maximum);
            if (result.Count > 0 && pitch <= result[^1]) pitch = Math.Min(maximum, result[^1] + 3);
            result.Add(pitch);
            cursor = pitch + 1;
        }
        return result;
    }
}
