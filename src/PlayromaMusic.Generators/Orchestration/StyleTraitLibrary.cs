using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.StylePacks;

namespace PlayromaMusic.Generators.Orchestration;

public static class StyleTraitLibrary
{
    private static readonly IReadOnlyList<StyleTraitDefinition> BuiltIns = Build();

    public static IReadOnlyList<StyleTraitDefinition> All => BuiltIns;

    public static StyleTraitDefinition Get(string id) =>
        All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown style trait '{id}'.");

    public static IReadOnlyList<StyleTraitDefinition> Compatible(
        IEnumerable<string> selectedIds,
        StyleFamily family,
        int remainingBudget)
    {
        HashSet<string> selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> blocked = selected.SelectMany(id => Get(id).Conflicts).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(item =>
            !selected.Contains(item.Id) &&
            !blocked.Contains(item.Id) &&
            item.SurpriseCost <= remainingBudget &&
            (item.Families.Count == 0 || item.Families.Contains(family)) &&
            item.Requires.All(selected.Contains)).ToArray();
    }

    private static IReadOnlyList<StyleTraitDefinition> Build()
    {
        List<StyleTraitDefinition> traits = new();
        void Add(
            string id,
            string name,
            string category,
            int cost,
            string description,
            string[]? preferred = null,
            string[]? avoid = null,
            double energy = 0,
            double syncopation = 0,
            double humanization = 0,
            Dictionary<TrackRole, int>? density = null,
            string[]? requires = null,
            string[]? conflicts = null,
            StyleFamily[]? families = null) => traits.Add(new StyleTraitDefinition(
                id, name, description, category, cost,
                requires ?? Array.Empty<string>(), conflicts ?? Array.Empty<string>(),
                preferred ?? Array.Empty<string>(), avoid ?? Array.Empty<string>(),
                density ?? new Dictionary<TrackRole, int>(), energy, syncopation, humanization,
                families ?? Array.Empty<StyleFamily>()));

        Add("four-on-floor", "Four on the Floor", "rhythm", 4, "Stable quarter-note kick foundation.", new[] { "rhythmic", "electronic" }, syncopation: -0.08, density: new() { [TrackRole.Drums] = 12 }, families: new[] { StyleFamily.Dance, StyleFamily.Pop });
        Add("syncopated-bass", "Syncopated Bass", "rhythm", 6, "Bass attacks answer rather than duplicate the kick.", new[] { "articulate", "rounded" }, syncopation: 0.22, density: new() { [TrackRole.Bass] = 10 });
        Add("walking-bass", "Walking Bass", "bass", 7, "Continuous chord-connected bass motion.", new[] { "organic", "clear" }, density: new() { [TrackRole.Bass] = 25 }, families: new[] { StyleFamily.Jazz });
        Add("pedal-harmony", "Pedal Harmony", "harmony", 5, "Sustained common-tone or bass pedal anchors changing harmony.", new[] { "sustaining", "warm" }, density: new() { [TrackRole.Chords] = -18 });
        Add("quartal-colour", "Quartal Colour", "harmony", 9, "Fourth-based voicing colour and open spacing.", new[] { "clear", "sustaining" }, families: new[] { StyleFamily.Jazz, StyleFamily.Algorithmic, StyleFamily.Ambient });
        Add("chromatic-approaches", "Chromatic Approaches", "melody", 7, "Approach tones increase local tension.", new[] { "distinct", "clear" }, syncopation: 0.04, families: new[] { StyleFamily.Jazz, StyleFamily.Algorithmic });
        Add("octave-lead", "Octave Lead", "orchestration", 5, "Lead is reinforced at an octave in energetic sections.", new[] { "distinct", "bright" }, energy: 0.06);
        Add("call-response", "Call and Response", "form", 6, "Lead and countermelody alternate phrases.", new[] { "contrasting", "clear" });
        Add("broken-chords", "Broken Chords", "accompaniment", 4, "Chord tones are unfolded into a pulse.", new[] { "articulate", "rhythmic" }, density: new() { [TrackRole.Arpeggio] = 18 });
        Add("low-drone", "Low Register Drone", "texture", 5, "A sparse low sustaining voice anchors the form.", new[] { "low", "sustaining", "dark" }, density: new() { [TrackRole.Texture] = -22 });
        Add("wide-pad", "Wide Pad", "texture", 4, "Slow, wide stereo pad supports long phrases.", new[] { "atmospheric", "sustaining", "warm" }, avoid: new[] { "short-attack" }, density: new() { [TrackRole.Texture] = -15 });
        Add("metallic-pulse", "Metallic Pulse", "orchestration", 8, "Bright metallic short attacks add a repeating high layer.", new[] { "metallic", "short-attack", "bright" }, energy: 0.04, density: new() { [TrackRole.Arpeggio] = 12 });
        Add("brush-drums", "Brush Drums", "rhythm", 3, "Soft brushed kit and restrained accents.", new[] { "organic", "warm" }, avoid: new[] { "aggressive" }, humanization: 0.16, families: new[] { StyleFamily.Jazz, StyleFamily.Ambient });
        Add("breakbeat", "Breakbeat", "rhythm", 10, "Fragmented syncopated drum vocabulary.", new[] { "rhythmic", "electronic", "articulate" }, syncopation: 0.32, density: new() { [TrackRole.Drums] = 20 }, conflicts: new[] { "four-on-floor" }, families: new[] { StyleFamily.Dance, StyleFamily.Algorithmic });
        Add("euclidean-percussion", "Euclidean Percussion", "rhythm", 7, "Evenly distributed pulses create rotating accents.", new[] { "percussive", "unusual" }, syncopation: 0.18, families: new[] { StyleFamily.Dance, StyleFamily.Algorithmic, StyleFamily.Ambient });
        Add("polymetric-layer", "Polymetric Layer", "rhythm", 14, "One support layer cycles against the primary meter.", new[] { "articulate", "unusual" }, syncopation: 0.28, conflicts: new[] { "minimal-pulse" }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Jazz });
        Add("minimal-pulse", "Minimal Pulse", "rhythm", 4, "A small pattern evolves through gradual mutation.", new[] { "clear", "rhythmic" }, density: new() { [TrackRole.Arpeggio] = -8, [TrackRole.Drums] = -12 });
        Add("rubato-surface", "Rubato Surface", "performance", 5, "Melodic and texture timing is loosened over stable harmony.", new[] { "organic", "airy" }, humanization: 0.28, conflicts: new[] { "mechanical-grid" }, families: new[] { StyleFamily.Ambient, StyleFamily.Jazz });
        Add("mechanical-grid", "Mechanical Grid", "performance", 3, "Tight timing and repeated velocity shapes.", new[] { "electronic", "articulate" }, humanization: -0.25, conflicts: new[] { "rubato-surface" }, families: new[] { StyleFamily.Dance, StyleFamily.Algorithmic });
        Add("loose-pocket", "Loose Pocket", "performance", 4, "Microtiming and accents produce a relaxed groove.", new[] { "organic", "warm" }, humanization: 0.2, families: new[] { StyleFamily.Pop, StyleFamily.Jazz });
        Add("gradual-layer-build", "Gradual Layer Build", "arrangement", 5, "New roles enter across successive sections.", new[] { "supportive", "atmospheric" }, energy: 0.05);
        Add("abrupt-bridge-texture", "Abrupt Bridge Texture", "arrangement", 9, "Bridge swaps the dominant timbral family.", new[] { "contrasting", "unusual" });
        Add("sparse-verses", "Sparse Verses", "arrangement", 3, "Verse instrumentation leaves room for the lead.", avoid: new[] { "very-dense" }, density: new() { [TrackRole.Chords] = -18, [TrackRole.Arpeggio] = -12 });
        Add("maximal-chorus", "Maximal Chorus", "arrangement", 6, "Choruses add layers, octave span and brighter presets.", new[] { "bright", "dense" }, energy: 0.12);
        Add("acoustic-core", "Acoustic Core", "orchestration", 2, "Prioritize organic instrument families.", new[] { "organic" }, avoid: new[] { "electronic" });
        Add("electronic-core", "Electronic Core", "orchestration", 2, "Prioritize synthesizers and electronic percussion.", new[] { "electronic" }, avoid: new[] { "organic" });
        Add("chamber-colours", "Chamber Colours", "orchestration", 4, "Small contrasting acoustic ensemble.", new[] { "organic", "clear" }, avoid: new[] { "very-dense" });
        Add("hybrid-acoustic-electronic", "Hybrid Acoustic/Electronic", "orchestration", 6, "Balance organic attacks with electronic sustain.", new[] { "organic", "electronic" });
        Add("woodwind-choir", "Woodwind Choir", "orchestration", 5, "Reed and pipe families distribute harmony and counterline.", new[] { "airy", "organic", "clear" }, families: new[] { StyleFamily.Jazz, StyleFamily.Ambient });
        Add("mallet-ensemble", "Mallet Ensemble", "orchestration", 6, "Chromatic percussion carries pulse and harmony.", new[] { "metallic", "short-attack", "articulate" });
        Add("organ-trio", "Organ Trio", "orchestration", 4, "Organ, bass and drums form the ensemble core.", new[] { "sustaining", "warm", "rhythmic" }, families: new[] { StyleFamily.Jazz, StyleFamily.Pop });
        Add("percussion-motif", "Percussion Motif", "orchestration", 10, "A pitched or unpitched percussion layer states the motif.", new[] { "percussive", "distinct", "unusual" }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Dance });
        Add("hocketed-chords", "Hocketed Chords", "orchestration", 9, "Chord tones alternate among several short-attack instruments.", new[] { "articulate", "contrasting" }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Jazz });
        Add("timbre-brightening", "Timbral Brightening", "tension", 4, "Preset brightness rises toward the climax.", new[] { "bright" }, energy: 0.04);
        Add("dark-resolution", "Dark Resolution", "tension", 5, "Final section resolves with darker, softer timbres.", new[] { "dark", "warm" }, avoid: new[] { "very-bright" });
        Add("rare-colour-instrument", "Rare Colour Instrument", "surprise", 8, "One underused preset appears as a restrained colour layer.", new[] { "unusual", "texture" });
        Add("family-role-swap", "Family Role Swap", "surprise", 12, "An instrument family serves an atypical role while retaining range suitability.", new[] { "unusual", "contrasting" }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Ambient });
        Add("sound-effect-punctuation", "Sound Effect Punctuation", "surprise", 11, "Sound-effect presets punctuate transitions only.", new[] { "effect", "unusual" }, density: new() { [TrackRole.Texture] = -25 }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Dance });
        Add("solo-with-texture", "Solo with Texture", "ensemble", 3, "One lead remains foregrounded over sparse evolving support.", new[] { "distinct", "atmospheric" }, density: new() { [TrackRole.Chords] = -25, [TrackRole.Texture] = -10 });
        Add("two-bass-interlock", "Two Bass Interlock", "ensemble", 15, "Two low-register voices alternate rhythmic responsibility.", new[] { "low", "articulate" }, conflicts: new[] { "walking-bass" }, families: new[] { StyleFamily.Algorithmic, StyleFamily.Dance });
        Add("phrase-instrument-swap", "Phrase Instrument Swap", "ensemble", 8, "Lead timbre alternates by phrase or section.", new[] { "contrasting", "distinct" });
        Add("dynamic-crossfade", "Dynamic Crossfade", "ensemble", 7, "Layer weights trade places across the tension curve.", new[] { "sustaining", "contrasting" });
        Add("intimate", "Intimate", "emotion", 2, "Small ensemble, soft velocities and close register.", new[] { "warm", "soft" }, avoid: new[] { "aggressive", "very-dense" }, energy: -0.08);
        Add("grand", "Grand", "emotion", 5, "Wide register, layered ensemble and strong cadential support.", new[] { "bold", "dense" }, energy: 0.12);
        Add("mysterious", "Mysterious", "emotion", 4, "Dark, airy and unusual timbral colours.", new[] { "dark", "airy", "unusual" });
        Add("playful", "Playful", "emotion", 3, "Short attacks, bright colours and active call-and-response.", new[] { "bright", "short-attack", "contrasting" }, syncopation: 0.08);
        Add("nostalgic", "Nostalgic", "emotion", 3, "Warm decaying sounds and restrained ensemble changes.", new[] { "warm", "organic", "decaying" });
        Add("aggressive", "Aggressive", "emotion", 5, "Dense attacks, bright upper harmonics and high rhythmic energy.", new[] { "aggressive", "bright", "dense" }, energy: 0.14);
        Add("serene", "Serene", "emotion", 2, "Slow attacks, sparse movement and stable support.", new[] { "warm", "airy", "sustaining" }, avoid: new[] { "aggressive" }, energy: -0.06);
        Add("jrpg-cadential-rise", "JRPG Cadential Rise", "harmony", 5, "Melody and accompaniment intensify into clear, emotionally legible cadences.", new[] { "expressive", "grand", "clear" }, energy: 0.07, families: new[] { StyleFamily.Jrpg });
        Add("boss-ostinato", "Boss Ostinato", "rhythm", 7, "A relentless low or middle-register ostinato drives escalating combat sections.", new[] { "aggressive", "rhythmic", "dark" }, density: new() { [TrackRole.Bass] = 16, [TrackRole.Arpeggio] = 18 }, energy: 0.11, families: new[] { StyleFamily.Jrpg, StyleFamily.Game });
        Add("chiptune-voice-budget", "Chiptune Voice Budget", "orchestration", 3, "Prefer a small set of sharply separated synthetic voices and economical accompaniment.", new[] { "electronic", "clear", "short-attack" }, avoid: new[] { "very-dense", "organic" }, density: new() { [TrackRole.Chords] = -14, [TrackRole.Texture] = -24 }, families: new[] { StyleFamily.RetroConsole });
        Add("dungeon-synth-drone", "Dungeon Synth Drone", "texture", 4, "Ancient pads, organ tones and low drones create a vast exploratory space.", new[] { "dark", "atmospheric", "sustaining" }, density: new() { [TrackRole.Texture] = -8, [TrackRole.Drums] = -25 }, energy: -0.02, families: new[] { StyleFamily.Game });
        Add("industrial-riff", "Industrial Riff", "rhythm", 6, "Tight repeated attacks and mechanized percussion create an aggressive PC-action pulse.", new[] { "aggressive", "electronic", "percussive" }, syncopation: 0.12, density: new() { [TrackRole.Chords] = 14, [TrackRole.Drums] = 16 }, energy: 0.1, families: new[] { StyleFamily.Game });
        Add("exploration-loop", "Exploration Loop", "form", 2, "Sections remain loopable, memorable and restrained enough for long gameplay sessions.", new[] { "atmospheric", "supportive", "clear" }, energy: -0.03, families: new[] { StyleFamily.Game, StyleFamily.Jrpg, StyleFamily.RetroConsole });
        return traits;
    }
}
