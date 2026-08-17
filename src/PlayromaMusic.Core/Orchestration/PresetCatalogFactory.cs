using System.Security.Cryptography;
using System.Text;
using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Core.Orchestration;

public static class PresetCatalogFactory
{
    private static readonly string[] GeneralMidiNames =
    {
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar nylon", "Acoustic Guitar steel", "Electric Guitar jazz", "Electric Guitar clean", "Electric Guitar muted", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Electric Bass finger", "Electric Bass pick", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 square", "Lead 2 sawtooth", "Lead 3 calliope", "Lead 4 chiff", "Lead 5 charang", "Lead 6 voice", "Lead 7 fifths", "Lead 8 bass and lead",
        "Pad 1 new age", "Pad 2 warm", "Pad 3 polysynth", "Pad 4 choir", "Pad 5 bowed", "Pad 6 metallic", "Pad 7 halo", "Pad 8 sweep",
        "FX 1 rain", "FX 2 soundtrack", "FX 3 crystal", "FX 4 atmosphere", "FX 5 brightness", "FX 6 goblins", "FX 7 echoes", "FX 8 sci-fi",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
    };

    private static readonly (string Name, int Program)[] DrumKits =
    {
        ("Standard Kit", 0), ("Room Kit", 8), ("Power Kit", 16), ("Electronic Kit", 24),
        ("TR-808 Kit", 25), ("Jazz Kit", 32), ("Brush Kit", 40), ("Orchestra Kit", 48), ("Sound FX Kit", 56)
    };

    public static InstrumentCatalogDocument CreateGeneralMidiFallback()
    {
        InstrumentCatalogDocument catalog = new()
        {
            SoundFontId = "gm-fallback",
            SoundFontName = "General MIDI complete fallback",
            SourceFingerprint = "gm-level1-128-plus-9-kits",
            IsComplete = true
        };
        for (int program = 0; program < GeneralMidiNames.Length; program++)
        {
            catalog.Presets.Add(Classify("gm-fallback", catalog.SoundFontName, 0, program, GeneralMidiNames[program], false));
        }
        foreach ((string name, int program) in DrumKits)
        {
            catalog.Presets.Add(Classify("gm-fallback", catalog.SoundFontName, 128, program, name, true));
        }
        return catalog;
    }

    public static InstrumentPresetDescriptor Classify(
        string soundFontId,
        string soundFontName,
        int bank,
        int program,
        string presetName,
        bool isDrumKit)
    {
        string normalizedName = string.IsNullOrWhiteSpace(presetName) ? $"Bank {bank} Program {program}" : presetName.Trim();
        bool drumKit = isDrumKit || bank == 128 || LooksLikeDrumKit(normalizedName);
        InstrumentFamily namedFamily = FamilyFromName(normalizedName);
        InstrumentFamily family = drumKit
            ? InstrumentFamily.Drums
            : namedFamily != InstrumentFamily.Unknown
                ? namedFamily
                : FamilyFromProgram(program);
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        AddFamilyTags(family, tags);
        AddNameTags(normalizedName, tags);
        TimbreProfile timbre = EstimateTimbre(family, program, tags);
        (int low, int high) = SuggestedRange(family);
        string key = BuildKey(soundFontId, bank, program, normalizedName);
        return new InstrumentPresetDescriptor
        {
            Key = key,
            SoundFontId = soundFontId,
            SoundFontName = soundFontName,
            Bank = Math.Clamp(bank, 0, 16383),
            Program = Math.Clamp(program, 0, 127),
            Name = normalizedName,
            Family = family,
            Tags = tags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            SuggestedLowNote = low,
            SuggestedHighNote = high,
            Timbre = timbre,
            IsDrumKit = drumKit
        };
    }

    public static InstrumentSelectorSettings DefaultSelector(TrackRole role, bool percussion = false)
    {
        if (percussion || role == TrackRole.Drums)
        {
            return new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Drums, InstrumentFamily.Percussive },
                RequiredTags = new List<string> { "percussive" },
                PreferredTags = new List<string> { "rhythmic", "short-attack" },
                MinimumPercussiveness = 0.55,
                MaximumLayers = 2,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.LayeredUnison }
            };
        }

        return role switch
        {
            TrackRole.Lead => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Pipe, InstrumentFamily.Reed, InstrumentFamily.SynthLead, InstrumentFamily.Guitar, InstrumentFamily.Piano, InstrumentFamily.Ethnic },
                PreferredTags = new List<string> { "distinct", "melodic", "clear" },
                AvoidTags = new List<string> { "very-dense", "sub-bass" },
                MinimumPitch = 48,
                MaximumPitch = 108,
                MinimumSustain = 0.2,
                MaximumLayers = 2,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.OctaveDoubling, RoleDistributionStrategy.CallAndResponse, RoleDistributionStrategy.PhraseAlternation }
            },
            TrackRole.CounterMelody => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Reed, InstrumentFamily.Pipe, InstrumentFamily.Strings, InstrumentFamily.SynthLead, InstrumentFamily.ChromaticPercussion },
                PreferredTags = new List<string> { "contrasting", "clear", "melodic" },
                MinimumPitch = 48,
                MaximumPitch = 112,
                MaximumLayers = 2,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.CallAndResponse, RoleDistributionStrategy.Hocket }
            },
            TrackRole.Bass => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Bass, InstrumentFamily.Strings, InstrumentFamily.Organ, InstrumentFamily.SynthLead },
                PreferredTags = new List<string> { "low", "grounding", "rounded" },
                AvoidTags = new List<string> { "thin", "very-bright" },
                MinimumPitch = 20,
                MaximumPitch = 67,
                MaximumBrightness = 0.75,
                MinimumSustain = 0.2,
                MaximumLayers = 2,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.LayeredUnison, RoleDistributionStrategy.RegisterSplit }
            },
            TrackRole.Chords or TrackRole.Harmony => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Piano, InstrumentFamily.Organ, InstrumentFamily.Guitar, InstrumentFamily.Strings, InstrumentFamily.Ensemble, InstrumentFamily.SynthPad },
                PreferredTags = new List<string> { "polyphonic", "supportive" },
                AvoidTags = new List<string> { "monophonic", "sub-bass" },
                MinimumPitch = 36,
                MaximumPitch = 108,
                MaximumLayers = 3,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.LayeredUnison, RoleDistributionStrategy.RegisterSplit, RoleDistributionStrategy.SectionSubstitution }
            },
            TrackRole.Arpeggio => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.Piano, InstrumentFamily.ChromaticPercussion, InstrumentFamily.Guitar, InstrumentFamily.SynthLead, InstrumentFamily.Ethnic },
                PreferredTags = new List<string> { "short-attack", "articulate", "rhythmic" },
                MinimumAttack = 0.35,
                MinimumPercussiveness = 0.25,
                MinimumPitch = 36,
                MaximumPitch = 112,
                MaximumLayers = 2,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.Hocket, RoleDistributionStrategy.OctaveDoubling }
            },
            _ => new InstrumentSelectorSettings
            {
                Families = new List<InstrumentFamily> { InstrumentFamily.SynthPad, InstrumentFamily.Ensemble, InstrumentFamily.Strings, InstrumentFamily.SynthEffects, InstrumentFamily.Organ },
                PreferredTags = new List<string> { "sustaining", "atmospheric" },
                MinimumSustain = 0.45,
                MinimumPitch = 28,
                MaximumPitch = 116,
                MaximumLayers = 3,
                AllowedStrategies = new List<RoleDistributionStrategy> { RoleDistributionStrategy.SingleInstrument, RoleDistributionStrategy.LayeredUnison, RoleDistributionStrategy.RegisterSplit, RoleDistributionStrategy.SectionSubstitution }
            }
        };
    }

    private static string BuildKey(string soundFontId, int bank, int program, string name)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{soundFontId}|{bank}|{program}|{name}"));
        return $"{soundFontId}:{bank}:{program}:{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}";
    }

    private static bool LooksLikeDrumKit(string name)
    {
        string value = name.ToLowerInvariant();
        return Contains(value, "drum kit", "drumkit", "percussion kit", "percussion set", "standard kit", "room kit", "power kit", "brush kit", "orchestra kit", "808 kit", "909 kit");
    }

    private static InstrumentFamily FamilyFromName(string name)
    {
        string value = name.ToLowerInvariant();
        if (Contains(value, "piano", "rhodes", "wurlitzer", "harpsichord", "clavinet", "clav ")) return InstrumentFamily.Piano;
        if (Contains(value, "celesta", "glock", "vibraphone", "vibes", "marimba", "xylophone", "music box", "tubular bell", "mallet")) return InstrumentFamily.ChromaticPercussion;
        if (Contains(value, "organ", "accordion", "harmonica")) return InstrumentFamily.Organ;
        if (Contains(value, "guitar", "ukulele", "mandolin")) return InstrumentFamily.Guitar;
        if (Contains(value, "violin", "viola", "cello", "contrabass", "double bass", "string", "harp", "pizzicato")) return InstrumentFamily.Strings;
        if (Contains(value, "choir", "voice", "vocal", "ensemble", "orchestra hit")) return InstrumentFamily.Ensemble;
        if (Contains(value, "trumpet", "trombone", "tuba", "horn", "brass", "flugel")) return InstrumentFamily.Brass;
        if (Contains(value, "sax", "oboe", "english horn", "bassoon", "clarinet", "reed")) return InstrumentFamily.Reed;
        if (Contains(value, "flute", "piccolo", "recorder", "shakuhachi", "whistle", "ocarina", "pipe", "bottle")) return InstrumentFamily.Pipe;
        if (Contains(value, "pad", "atmosphere", "halo", "sweep", "soundscape")) return InstrumentFamily.SynthPad;
        if (Contains(value, "lead", "sawtooth", "square", "charang", "calliope")) return InstrumentFamily.SynthLead;
        if (Contains(value, "synth fx", "synth effect", "rain fx", "crystal fx", "sci-fi")) return InstrumentFamily.SynthEffects;
        if (Contains(value, "sitar", "shamisen", "koto", "kalimba", "bagpipe", "shanai", "dulcimer", "fiddle", "banjo")) return InstrumentFamily.Ethnic;
        if (Contains(value, "timpani", "agogo", "steel drum", "woodblock", "taiko", "tom", "cymbal", "percussion")) return InstrumentFamily.Percussive;
        if (Contains(value, "noise", "seashore", "bird", "telephone", "helicopter", "applause", "gunshot", "effect", " fx")) return InstrumentFamily.SoundEffects;
        if (Contains(value, "bass")) return InstrumentFamily.Bass;
        return InstrumentFamily.Unknown;
    }

    private static InstrumentFamily FamilyFromProgram(int program) => (Math.Clamp(program, 0, 127) / 8) switch
    {
        0 => InstrumentFamily.Piano,
        1 => InstrumentFamily.ChromaticPercussion,
        2 => InstrumentFamily.Organ,
        3 => InstrumentFamily.Guitar,
        4 => InstrumentFamily.Bass,
        5 => InstrumentFamily.Strings,
        6 => InstrumentFamily.Ensemble,
        7 => InstrumentFamily.Brass,
        8 => InstrumentFamily.Reed,
        9 => InstrumentFamily.Pipe,
        10 => InstrumentFamily.SynthLead,
        11 => InstrumentFamily.SynthPad,
        12 => InstrumentFamily.SynthEffects,
        13 => InstrumentFamily.Ethnic,
        14 => InstrumentFamily.Percussive,
        15 => InstrumentFamily.SoundEffects,
        _ => InstrumentFamily.Unknown
    };

    private static void AddFamilyTags(InstrumentFamily family, ISet<string> tags)
    {
        switch (family)
        {
            case InstrumentFamily.Piano: Add(tags, "polyphonic", "decaying", "clear", "supportive", "melodic"); break;
            case InstrumentFamily.ChromaticPercussion: Add(tags, "percussive", "short-attack", "bright", "articulate", "rhythmic"); break;
            case InstrumentFamily.Organ: Add(tags, "polyphonic", "sustaining", "warm", "supportive"); break;
            case InstrumentFamily.Guitar: Add(tags, "decaying", "articulate", "melodic", "rhythmic"); break;
            case InstrumentFamily.Bass: Add(tags, "low", "grounding", "rounded", "monophonic"); break;
            case InstrumentFamily.Strings: Add(tags, "sustaining", "organic", "warm", "melodic"); break;
            case InstrumentFamily.Ensemble: Add(tags, "polyphonic", "sustaining", "dense", "supportive"); break;
            case InstrumentFamily.Brass: Add(tags, "bold", "bright", "sustaining", "accent"); break;
            case InstrumentFamily.Reed: Add(tags, "melodic", "distinct", "organic", "clear"); break;
            case InstrumentFamily.Pipe: Add(tags, "melodic", "airy", "clear", "distinct"); break;
            case InstrumentFamily.SynthLead: Add(tags, "electronic", "distinct", "melodic", "monophonic"); break;
            case InstrumentFamily.SynthPad: Add(tags, "electronic", "sustaining", "atmospheric", "polyphonic"); break;
            case InstrumentFamily.SynthEffects: Add(tags, "electronic", "atmospheric", "unusual", "texture"); break;
            case InstrumentFamily.Ethnic: Add(tags, "organic", "distinct", "melodic", "unusual"); break;
            case InstrumentFamily.Percussive: Add(tags, "percussive", "rhythmic", "short-attack", "accent"); break;
            case InstrumentFamily.SoundEffects: Add(tags, "unusual", "texture", "effect"); break;
            case InstrumentFamily.Drums: Add(tags, "percussive", "rhythmic", "short-attack", "kit"); break;
        }
    }

    private static void AddNameTags(string name, ISet<string> tags)
    {
        string value = name.ToLowerInvariant();
        if (Contains(value, "warm", "mellow", "soft")) Add(tags, "warm", "rounded");
        if (Contains(value, "bright", "metal", "bell", "crystal", "glass")) Add(tags, "bright", "metallic");
        if (Contains(value, "dark", "bass", "low")) Add(tags, "dark", "low");
        if (Contains(value, "pad", "strings", "choir", "ensemble", "sweep")) Add(tags, "sustaining", "atmospheric");
        if (Contains(value, "pluck", "pizz", "clav", "marimba", "xyl", "guitar", "harp")) Add(tags, "short-attack", "articulate");
        if (Contains(value, "lead", "solo", "sax", "flute", "violin", "oboe")) Add(tags, "distinct", "melodic");
        if (Contains(value, "distort", "overdrive", "power", "rock")) Add(tags, "aggressive", "dense");
        if (Contains(value, "breath", "air", "flute", "pipe", "whistle")) Add(tags, "airy");
        if (Contains(value, "analog", "synth", "electronic", "808")) Add(tags, "electronic");
        if (Contains(value, "acoustic", "nylon", "wood", "orchestra")) Add(tags, "organic");
        if (Contains(value, "fx", "effect", "noise", "rain", "goblin", "sci")) Add(tags, "unusual", "effect");
        if (Contains(value, "thin", "nasal")) Add(tags, "thin");
    }

    private static TimbreProfile EstimateTimbre(InstrumentFamily family, int program, ISet<string> tags)
    {
        double attack = family switch
        {
            InstrumentFamily.ChromaticPercussion or InstrumentFamily.Percussive or InstrumentFamily.Drums => 0.95,
            InstrumentFamily.Piano or InstrumentFamily.Guitar or InstrumentFamily.Ethnic => 0.78,
            InstrumentFamily.Brass or InstrumentFamily.Reed or InstrumentFamily.Pipe or InstrumentFamily.SynthLead => 0.58,
            InstrumentFamily.Strings or InstrumentFamily.Ensemble or InstrumentFamily.SynthPad => 0.25,
            _ => 0.5
        };
        double sustain = family switch
        {
            InstrumentFamily.Organ or InstrumentFamily.Strings or InstrumentFamily.Ensemble or InstrumentFamily.Brass or InstrumentFamily.Reed or InstrumentFamily.Pipe or InstrumentFamily.SynthLead or InstrumentFamily.SynthPad => 0.82,
            InstrumentFamily.Piano or InstrumentFamily.Guitar or InstrumentFamily.ChromaticPercussion or InstrumentFamily.Percussive or InstrumentFamily.Drums => 0.25,
            _ => 0.55
        };
        double brightness = 0.25 + ((program % 8) / 7.0) * 0.45;
        if (tags.Contains("bright")) brightness += 0.2;
        if (tags.Contains("dark")) brightness -= 0.18;
        double density = family is InstrumentFamily.Ensemble or InstrumentFamily.SynthPad ? 0.82 : family is InstrumentFamily.Organ or InstrumentFamily.Strings ? 0.65 : 0.4;
        double percussiveness = family is InstrumentFamily.ChromaticPercussion or InstrumentFamily.Percussive or InstrumentFamily.Drums ? 0.95 : family is InstrumentFamily.Piano or InstrumentFamily.Guitar or InstrumentFamily.Ethnic ? 0.62 : 0.18;
        return new TimbreProfile
        {
            Attack = Math.Clamp(attack, 0, 1),
            Sustain = Math.Clamp(sustain, 0, 1),
            Brightness = Math.Clamp(brightness, 0, 1),
            Density = Math.Clamp(density, 0, 1),
            Percussiveness = Math.Clamp(percussiveness, 0, 1),
            PitchStability = family is InstrumentFamily.SoundEffects or InstrumentFamily.SynthEffects ? 0.45 : 0.9,
            Confidence = 0.52
        };
    }

    private static (int Low, int High) SuggestedRange(InstrumentFamily family) => family switch
    {
        InstrumentFamily.Bass => (20, 67),
        InstrumentFamily.Strings => (28, 108),
        InstrumentFamily.Brass => (34, 94),
        InstrumentFamily.Reed => (34, 103),
        InstrumentFamily.Pipe => (48, 112),
        InstrumentFamily.Guitar => (40, 88),
        InstrumentFamily.Piano or InstrumentFamily.Organ or InstrumentFamily.SynthPad => (24, 112),
        InstrumentFamily.ChromaticPercussion => (48, 108),
        InstrumentFamily.Drums => (0, 127),
        _ => (36, 108)
    };

    private static void Add(ISet<string> tags, params string[] values)
    {
        foreach (string value in values) tags.Add(value);
    }

    private static bool Contains(string text, params string[] values) => values.Any(text.Contains);
}
