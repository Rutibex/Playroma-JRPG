using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Generators.Orchestration;

public static class InstrumentSubstitutionGraph
{
    private static readonly IReadOnlyDictionary<InstrumentFamily, InstrumentFamily[]> Neighbours =
        new Dictionary<InstrumentFamily, InstrumentFamily[]>
        {
            [InstrumentFamily.Piano] = new[] { InstrumentFamily.ChromaticPercussion, InstrumentFamily.Guitar, InstrumentFamily.Organ, InstrumentFamily.SynthPad },
            [InstrumentFamily.ChromaticPercussion] = new[] { InstrumentFamily.Piano, InstrumentFamily.Percussive, InstrumentFamily.Ethnic, InstrumentFamily.SynthLead },
            [InstrumentFamily.Organ] = new[] { InstrumentFamily.Piano, InstrumentFamily.SynthPad, InstrumentFamily.Ensemble, InstrumentFamily.Brass },
            [InstrumentFamily.Guitar] = new[] { InstrumentFamily.Piano, InstrumentFamily.Ethnic, InstrumentFamily.Bass, InstrumentFamily.ChromaticPercussion },
            [InstrumentFamily.Bass] = new[] { InstrumentFamily.Guitar, InstrumentFamily.SynthLead, InstrumentFamily.Strings, InstrumentFamily.Organ },
            [InstrumentFamily.Strings] = new[] { InstrumentFamily.Ensemble, InstrumentFamily.SynthPad, InstrumentFamily.Reed, InstrumentFamily.Brass },
            [InstrumentFamily.Ensemble] = new[] { InstrumentFamily.Strings, InstrumentFamily.SynthPad, InstrumentFamily.Organ, InstrumentFamily.Brass },
            [InstrumentFamily.Brass] = new[] { InstrumentFamily.Reed, InstrumentFamily.Ensemble, InstrumentFamily.Organ, InstrumentFamily.SynthLead },
            [InstrumentFamily.Reed] = new[] { InstrumentFamily.Pipe, InstrumentFamily.Brass, InstrumentFamily.Strings, InstrumentFamily.SynthLead },
            [InstrumentFamily.Pipe] = new[] { InstrumentFamily.Reed, InstrumentFamily.Ethnic, InstrumentFamily.Strings, InstrumentFamily.SynthLead },
            [InstrumentFamily.SynthLead] = new[] { InstrumentFamily.Reed, InstrumentFamily.Brass, InstrumentFamily.ChromaticPercussion, InstrumentFamily.SynthEffects },
            [InstrumentFamily.SynthPad] = new[] { InstrumentFamily.Ensemble, InstrumentFamily.Strings, InstrumentFamily.Organ, InstrumentFamily.SynthEffects },
            [InstrumentFamily.SynthEffects] = new[] { InstrumentFamily.SynthPad, InstrumentFamily.SoundEffects, InstrumentFamily.SynthLead, InstrumentFamily.Percussive },
            [InstrumentFamily.Ethnic] = new[] { InstrumentFamily.Guitar, InstrumentFamily.Pipe, InstrumentFamily.ChromaticPercussion, InstrumentFamily.Percussive },
            [InstrumentFamily.Percussive] = new[] { InstrumentFamily.Drums, InstrumentFamily.ChromaticPercussion, InstrumentFamily.Ethnic, InstrumentFamily.SynthEffects },
            [InstrumentFamily.SoundEffects] = new[] { InstrumentFamily.SynthEffects, InstrumentFamily.Percussive, InstrumentFamily.SynthPad },
            [InstrumentFamily.Drums] = new[] { InstrumentFamily.Percussive, InstrumentFamily.ChromaticPercussion, InstrumentFamily.SoundEffects }
        };

    public static double Distance(InstrumentPresetDescriptor? from, InstrumentPresetDescriptor? to)
    {
        if (from is null || to is null) return 1;
        if (from.Key.Equals(to.Key, StringComparison.OrdinalIgnoreCase)) return 0;
        double family = FamilyDistance(from.Family, to.Family);
        TimbreProfile a = from.Timbre;
        TimbreProfile b = to.Timbre;
        double timbre = Math.Sqrt(
            Math.Pow(a.Attack - b.Attack, 2) +
            Math.Pow(a.Sustain - b.Sustain, 2) +
            Math.Pow(a.Brightness - b.Brightness, 2) +
            Math.Pow(a.Density - b.Density, 2) +
            Math.Pow(a.Percussiveness - b.Percussiveness, 2)) / Math.Sqrt(5);
        double sharedTags = JaccardDistance(from.Tags, to.Tags);
        return Math.Clamp(family * 0.5 + timbre * 0.35 + sharedTags * 0.15, 0, 1);
    }

    public static double FamilyDistance(InstrumentFamily from, InstrumentFamily to)
    {
        if (from == to) return 0.08;
        if (from == InstrumentFamily.Unknown || to == InstrumentFamily.Unknown) return 0.75;
        if (Neighbours.TryGetValue(from, out InstrumentFamily[]? direct) && direct.Contains(to))
        {
            int index = Array.IndexOf(direct, to);
            return 0.22 + Math.Max(0, index) * 0.08;
        }
        if (Neighbours.TryGetValue(to, out InstrumentFamily[]? reverse) && reverse.Contains(from))
        {
            int index = Array.IndexOf(reverse, from);
            return 0.26 + Math.Max(0, index) * 0.08;
        }
        bool bothElectronic = IsElectronic(from) && IsElectronic(to);
        bool bothAcoustic = !IsElectronic(from) && !IsElectronic(to);
        return bothElectronic ? 0.55 : bothAcoustic ? 0.68 : 0.84;
    }

    public static double MaximumDistanceForSurprise(int surprise) =>
        Math.Clamp(0.18 + Math.Clamp(surprise, 0, 100) / 100.0 * 0.82, 0.18, 1.0);

    private static bool IsElectronic(InstrumentFamily family) => family is
        InstrumentFamily.SynthLead or InstrumentFamily.SynthPad or InstrumentFamily.SynthEffects or InstrumentFamily.SoundEffects;

    private static double JaccardDistance(IEnumerable<string> first, IEnumerable<string> second)
    {
        HashSet<string> a = first.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> b = second.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int union = a.Union(b).Count();
        if (union == 0) return 0.5;
        return 1 - a.Intersect(b).Count() / (double)union;
    }
}
