using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Generators.Orchestration;

public static class EnsembleArchetypeLibrary
{
    private static readonly IReadOnlyList<EnsembleArchetypeDefinition> BuiltIns = Build();

    public static IReadOnlyList<EnsembleArchetypeDefinition> All => BuiltIns;

    public static EnsembleArchetypeDefinition Get(string id) =>
        All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown ensemble archetype '{id}'.");

    private static IReadOnlyList<EnsembleArchetypeDefinition> Build()
    {
        List<EnsembleArchetypeDefinition> result = new();
        EnsembleSlotDefinition Slot(string id, TrackRole role, bool required, int min, int max, InstrumentSelectorSettings? selector = null) =>
            new(id, role, required, min, max, selector ?? PresetCatalogFactory.DefaultSelector(role, role == TrackRole.Drums));
        void Add(string id, string name, string description, int min, int max, IEnumerable<EnsembleSlotDefinition> slots, string[]? prefer = null, string[]? avoid = null) =>
            result.Add(new EnsembleArchetypeDefinition(id, name, description, min, max, slots.ToArray(), prefer ?? Array.Empty<string>(), avoid ?? Array.Empty<string>()));

        Add("balanced-band", "Balanced Band", "Lead, harmony, bass, pulse and optional colour roles.", 4, 8,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2), Slot("chords", TrackRole.Chords, true, 1, 2), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, true, 1, 1), Slot("arp", TrackRole.Arpeggio, false, 0, 1), Slot("texture", TrackRole.Texture, false, 0, 2) });
        Add("power-trio", "Power Trio", "Lead/harmony instrument, bass and drums with minimal layering.", 3, 4,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 1), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, true, 1, 1), Slot("chords", TrackRole.Chords, false, 0, 1) },
            new[] { "aggressive", "sparse-verses" });
        Add("piano-rhythm-section", "Piano Rhythm Section", "Keyboard-centred harmony with bass, drums and an optional soloist.", 3, 6,
            new[] { Slot("chords", TrackRole.Chords, true, 1, 1, Selector(InstrumentFamily.Piano, InstrumentFamily.Organ)), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, true, 1, 1), Slot("lead", TrackRole.Lead, false, 0, 1), Slot("counter", TrackRole.CounterMelody, false, 0, 1) },
            new[] { "organ-trio", "walking-bass" });
        Add("synth-trio", "Synth Trio", "Electronic lead, bass and pad/pulse core.", 3, 6,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2, Selector(InstrumentFamily.SynthLead)), Slot("bass", TrackRole.Bass, true, 1, 2, Selector(InstrumentFamily.Bass, InstrumentFamily.SynthLead)), Slot("texture", TrackRole.Texture, true, 1, 2, Selector(InstrumentFamily.SynthPad, InstrumentFamily.SynthEffects)), Slot("drums", TrackRole.Drums, false, 0, 1) },
            new[] { "electronic-core", "mechanical-grid" });
        Add("chamber-quartet", "Chamber Quartet", "Four contrasting acoustic voices with no mandatory drum kit.", 4, 5,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 1, Selector(InstrumentFamily.Strings, InstrumentFamily.Reed, InstrumentFamily.Pipe)), Slot("counter", TrackRole.CounterMelody, true, 1, 1), Slot("harmony", TrackRole.Harmony, true, 1, 1), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("texture", TrackRole.Texture, false, 0, 1) },
            new[] { "chamber-colours", "acoustic-core" });
        Add("woodwind-choir", "Woodwind Choir", "Pipe and reed families distribute melody, harmony and bass support.", 4, 7,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 1, Selector(InstrumentFamily.Pipe, InstrumentFamily.Reed)), Slot("counter", TrackRole.CounterMelody, true, 1, 2, Selector(InstrumentFamily.Pipe, InstrumentFamily.Reed)), Slot("harmony", TrackRole.Harmony, true, 1, 2, Selector(InstrumentFamily.Reed, InstrumentFamily.Pipe)), Slot("bass", TrackRole.Bass, true, 1, 1, Selector(InstrumentFamily.Reed, InstrumentFamily.Strings)) },
            new[] { "woodwind-choir" });
        Add("mallet-circle", "Mallet Circle", "Pitched percussion supplies lead, harmony and pulse.", 3, 7,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2, Selector(InstrumentFamily.ChromaticPercussion, InstrumentFamily.Ethnic)), Slot("arp", TrackRole.Arpeggio, true, 1, 2, Selector(InstrumentFamily.ChromaticPercussion, InstrumentFamily.Percussive)), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, false, 0, 1) },
            new[] { "mallet-ensemble", "hocketed-chords" });
        Add("cinematic-layers", "Cinematic Layers", "Sustaining harmony, low foundation and several colour layers.", 4, 10,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2), Slot("chords", TrackRole.Chords, true, 1, 3, Selector(InstrumentFamily.Strings, InstrumentFamily.Ensemble, InstrumentFamily.SynthPad)), Slot("bass", TrackRole.Bass, true, 1, 2), Slot("texture", TrackRole.Texture, true, 1, 3), Slot("drums", TrackRole.Drums, false, 0, 1) },
            new[] { "grand", "gradual-layer-build" });
        Add("ambient-solo", "Solo and Evolving Texture", "A foreground solo voice over sparse pads, drones and colour.", 2, 6,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2), Slot("texture", TrackRole.Texture, true, 1, 3, Selector(InstrumentFamily.SynthPad, InstrumentFamily.Ensemble, InstrumentFamily.SynthEffects, InstrumentFamily.Strings)), Slot("bass", TrackRole.Bass, false, 0, 1), Slot("arp", TrackRole.Arpeggio, false, 0, 1) },
            new[] { "solo-with-texture", "wide-pad" });
        Add("percussion-forward", "Percussion Forward", "Multiple rhythmic layers with restrained harmony.", 4, 9,
            new[] { Slot("drums", TrackRole.Drums, true, 1, 2), Slot("arp", TrackRole.Arpeggio, true, 1, 2, Selector(InstrumentFamily.Percussive, InstrumentFamily.ChromaticPercussion, InstrumentFamily.Ethnic)), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("lead", TrackRole.Lead, true, 1, 1), Slot("texture", TrackRole.Texture, false, 0, 1) },
            new[] { "euclidean-percussion", "percussion-motif" });
        Add("organ-trio", "Organ Trio", "Organ, bass and drums with optional reed soloist.", 3, 5,
            new[] { Slot("chords", TrackRole.Chords, true, 1, 1, Selector(InstrumentFamily.Organ)), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, true, 1, 1), Slot("lead", TrackRole.Lead, false, 0, 1, Selector(InstrumentFamily.Reed, InstrumentFamily.Guitar)) },
            new[] { "organ-trio" });
        Add("algorithmic-hocket", "Algorithmic Hocket", "Several contrasting voices exchange notes and phrases.", 4, 10,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 2), Slot("counter", TrackRole.CounterMelody, true, 1, 2), Slot("arp", TrackRole.Arpeggio, true, 1, 2), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("texture", TrackRole.Texture, false, 0, 2), Slot("drums", TrackRole.Drums, false, 0, 1) },
            new[] { "hocketed-chords", "polymetric-layer", "family-role-swap" });
        Add("two-bass-machine", "Two-Bass Machine", "Interlocking low voices, percussion and sparse upper colour.", 4, 7,
            new[] { Slot("bass", TrackRole.Bass, true, 2, 2), Slot("drums", TrackRole.Drums, true, 1, 1), Slot("lead", TrackRole.Lead, true, 1, 1), Slot("texture", TrackRole.Texture, false, 0, 2) },
            new[] { "two-bass-interlock", "mechanical-grid" });
        Add("unusual-colour-box", "Unusual Colour Box", "Conventional foundation plus one or two rare SoundFont colours.", 4, 8,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 1), Slot("chords", TrackRole.Chords, true, 1, 1), Slot("bass", TrackRole.Bass, true, 1, 1), Slot("drums", TrackRole.Drums, false, 0, 1), Slot("texture", TrackRole.Texture, true, 1, 2, Selector(InstrumentFamily.SynthEffects, InstrumentFamily.SoundEffects, InstrumentFamily.Ethnic, InstrumentFamily.Percussive)) },
            new[] { "rare-colour-instrument" });
        Add("single-instrument-study", "Single Instrument Study", "One broad-range preset carries several sparse roles.", 1, 3,
            new[] { Slot("lead", TrackRole.Lead, true, 1, 1), Slot("chords", TrackRole.Chords, false, 0, 1), Slot("bass", TrackRole.Bass, false, 0, 1) },
            new[] { "intimate" });
        return result;
    }

    private static InstrumentSelectorSettings Selector(params InstrumentFamily[] families)
    {
        InstrumentSelectorSettings selector = new()
        {
            Families = families.ToList(),
            MaximumLayers = 3,
            AllowedStrategies = new List<RoleDistributionStrategy>
            {
                RoleDistributionStrategy.SingleInstrument,
                RoleDistributionStrategy.LayeredUnison,
                RoleDistributionStrategy.OctaveDoubling,
                RoleDistributionStrategy.RegisterSplit,
                RoleDistributionStrategy.CallAndResponse,
                RoleDistributionStrategy.Hocket,
                RoleDistributionStrategy.SectionSubstitution
            }
        };
        return selector;
    }
}
