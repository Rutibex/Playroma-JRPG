using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.Validation;

namespace PlayromaMusic.Core.Generation;

public sealed class FoundationSongGenerator
{
    private static readonly PitchClass[] KeyRoots =
    {
        PitchClass.C, PitchClass.D, PitchClass.E, PitchClass.F, PitchClass.G, PitchClass.A
    };

    private static readonly ScaleKind[] ScaleKinds =
    {
        ScaleKind.Major, ScaleKind.NaturalMinor, ScaleKind.Dorian, ScaleKind.Mixolydian
    };

    private static readonly int[][] MajorProgressions =
    {
        new[] { 0, 4, 5, 3 },
        new[] { 0, 5, 3, 4 },
        new[] { 0, 3, 4, 0 },
        new[] { 5, 3, 0, 4 }
    };

    private static readonly int[][] MinorProgressions =
    {
        new[] { 0, 5, 2, 6 },
        new[] { 0, 6, 5, 6 },
        new[] { 0, 3, 6, 5 },
        new[] { 0, 5, 3, 6 }
    };

    public ProjectDocument Generate(ulong masterSeed, GenerationSettings? settings = null)
    {
        settings ??= new GenerationSettings();
        settings.Validate();

        DeterministicRandom identityRandom = Stream(masterSeed, "identity");
        PitchClass keyRoot = KeyRoots[identityRandom.NextInt(KeyRoots.Length)];
        ScaleKind scaleKind = ScaleKinds[identityRandom.NextInt(ScaleKinds.Length)];
        int tempo = identityRandom.NextInt(settings.MinimumTempo, settings.MaximumTempo + 1);
        ScaleDefinition scale = ScaleLibrary.Get(scaleKind);

        string title = BuildTitle(keyRoot, scaleKind, masterSeed);
        long beatTicks = settings.TicksPerQuarterNote * 4L / settings.BeatUnit;
        long barTicks = beatTicks * settings.BeatsPerBar;
        long songLength = barTicks * settings.Bars;

        ProjectDocument project = new()
        {
            SchemaVersion = BuildInfo.ProjectSchemaVersion,
            EngineVersion = BuildInfo.Version,
            ProjectId = SeedDerivation.StableId("project", masterSeed, "root"),
            Title = title,
            MasterSeed = masterSeed,
            Tags = new List<string> { "stage1", "deterministic", scale.Name.ToLowerInvariant().Replace(' ', '-') },
            Song = new SongDocument
            {
                Title = title,
                TicksPerQuarterNote = settings.TicksPerQuarterNote,
                KeyRoot = keyRoot,
                Scale = scaleKind,
                TempoMap = new List<TempoChange> { new() { Tick = 0, BeatsPerMinute = tempo } },
                MeterMap = new List<MeterChange>
                {
                    new() { Tick = 0, Numerator = settings.BeatsPerBar, Denominator = settings.BeatUnit }
                },
                Sections = BuildSections(masterSeed, settings.Bars, barTicks),
                Tracks = new List<SongTrack>()
            }
        };

        int[] progression = SelectProgression(masterSeed, scaleKind);
        project.Song.Tracks.Add(BuildHarmonyTrack(project, scale, progression, settings, barTicks));
        project.Song.Tracks.Add(BuildBassTrack(project, scale, progression, settings, beatTicks, barTicks));
        project.Song.Tracks.Add(BuildLeadTrack(project, scale, progression, settings, beatTicks, barTicks));
        project.Song.Tracks.Add(BuildDrumTrack(project, settings, beatTicks, barTicks));

        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Generated project failed validation: " + string.Join("; ", validation.Errors));
        }

        if (project.Song.LengthTicks != songLength)
        {
            throw new InvalidOperationException("Generated song length differs from the requested length.");
        }

        return project;
    }

    private static SongTrack BuildHarmonyTrack(
        ProjectDocument project,
        ScaleDefinition scale,
        IReadOnlyList<int> progression,
        GenerationSettings settings,
        long barTicks)
    {
        ulong seed = project.MasterSeed;
        DeterministicRandom random = Stream(seed, "track", "harmony");
        SongTrack track = NewTrack(seed, "harmony", "Harmony", TrackRole.Harmony, 1, 48, false);

        foreach (SongSection section in project.Song.Sections)
        {
            SongClip clip = NewClip(seed, track.Id, section);
            int startBar = (int)(section.StartTick / barTicks);
            int barCount = (int)(section.LengthTicks / barTicks);

            for (int localBar = 0; localBar < barCount; localBar++)
            {
                int globalBar = startBar + localBar;
                int degree = progression[globalBar % progression.Count];
                ChordDefinition chord = ChordBuilder.BuildDiatonic(project.Song.KeyRoot, scale, degree, 3);
                long start = localBar * barTicks;
                long duration = Math.Max(1, barTicks - (settings.TicksPerQuarterNote / 16));
                int velocity = 48 + random.NextInt(20);

                for (int voice = 0; voice < chord.MidiPitches.Count; voice++)
                {
                    int pitch = chord.MidiPitches[voice];
                    if (section.Name == "Intro" && voice == 1 && random.Chance(0.45))
                    {
                        continue;
                    }

                    clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-voice-{voice}", pitch, start, duration, velocity - (voice * 3)));
                }
            }

            track.Clips.Add(clip);
        }

        return track;
    }

    private static SongTrack BuildBassTrack(
        ProjectDocument project,
        ScaleDefinition scale,
        IReadOnlyList<int> progression,
        GenerationSettings settings,
        long beatTicks,
        long barTicks)
    {
        ulong seed = project.MasterSeed;
        DeterministicRandom random = Stream(seed, "track", "bass");
        SongTrack track = NewTrack(seed, "bass", "Bass", TrackRole.Bass, 2, 33, false);

        foreach (SongSection section in project.Song.Sections)
        {
            SongClip clip = NewClip(seed, track.Id, section);
            int startBar = (int)(section.StartTick / barTicks);
            int barCount = (int)(section.LengthTicks / barTicks);

            for (int localBar = 0; localBar < barCount; localBar++)
            {
                int globalBar = startBar + localBar;
                int degree = progression[globalBar % progression.Count];
                int rootPitch = scale.MidiPitchForDegree(project.Song.KeyRoot, degree, 2);
                int fifthPitch = scale.MidiPitchForDegree(project.Song.KeyRoot, degree + 4, 2);

                int attacks = section.Name == "Intro" ? 2 : 4;
                long step = barTicks / attacks;
                for (int attack = 0; attack < attacks; attack++)
                {
                    bool useFifth = attack == attacks - 1 && random.Chance(0.55);
                    int pitch = useFifth ? fifthPitch : rootPitch;
                    if (attack == attacks - 1 && globalBar + 1 < settings.Bars && random.Chance(0.25))
                    {
                        int nextDegree = progression[(globalBar + 1) % progression.Count];
                        int nextRoot = scale.MidiPitchForDegree(project.Song.KeyRoot, nextDegree, 2);
                        pitch = Math.Clamp(nextRoot - (random.Chance(0.5) ? 1 : 2), 0, 127);
                    }

                    long start = (localBar * barTicks) + (attack * step);
                    long duration = Math.Max(1, step - (beatTicks / 12));
                    int velocity = 76 + random.NextInt(19) + (attack == 0 ? 8 : 0);
                    clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-attack-{attack}", pitch, start, duration, Math.Clamp(velocity, 1, 127)));
                }
            }

            track.Clips.Add(clip);
        }

        return track;
    }

    private static SongTrack BuildLeadTrack(
        ProjectDocument project,
        ScaleDefinition scale,
        IReadOnlyList<int> progression,
        GenerationSettings settings,
        long beatTicks,
        long barTicks)
    {
        ulong seed = project.MasterSeed;
        DeterministicRandom random = Stream(seed, "track", "lead");
        SongTrack track = NewTrack(seed, "lead", "Lead", TrackRole.Lead, 0, 0, false);
        int currentDegree = random.NextInt(2, 6);
        int[] motif = BuildMotif(random);

        foreach (SongSection section in project.Song.Sections)
        {
            SongClip clip = NewClip(seed, track.Id, section);
            int startBar = (int)(section.StartTick / barTicks);
            int barCount = (int)(section.LengthTicks / barTicks);
            long eighth = beatTicks / 2;

            for (int localBar = 0; localBar < barCount; localBar++)
            {
                int globalBar = startBar + localBar;
                int chordDegree = progression[globalBar % progression.Count];
                int steps = settings.BeatsPerBar * 2;

                for (int step = 0; step < steps; step++)
                {
                    bool strongBeat = step % 2 == 0;
                    double restProbability = section.Name == "Intro" ? 0.48 : section.Name == "Outro" ? 0.42 : 0.28;
                    if (random.Chance(restProbability))
                    {
                        continue;
                    }

                    int motifIndex = (globalBar * steps + step) % motif.Length;
                    currentDegree += motif[motifIndex];
                    currentDegree = Math.Clamp(currentDegree, -2, 11);

                    int degree = currentDegree;
                    if (strongBeat && random.Chance(0.72))
                    {
                        int[] chordTones = { chordDegree, chordDegree + 2, chordDegree + 4 };
                        degree = NearestDegree(currentDegree, chordTones, scale.Intervals.Count);
                    }

                    int pitch = scale.MidiPitchForDegree(project.Song.KeyRoot, degree, 4);
                    long start = (localBar * barTicks) + (step * eighth);
                    long duration = random.Chance(0.18) ? eighth * 2 : eighth;
                    duration = Math.Min(duration, section.LengthTicks - start);
                    duration = Math.Max(1, duration - (beatTicks / 24));
                    int velocity = 70 + random.NextInt(35) + (strongBeat ? 8 : 0);

                    clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-step-{step}", pitch, start, duration, Math.Clamp(velocity, 1, 127)));
                }
            }

            track.Clips.Add(clip);
        }

        return track;
    }

    private static SongTrack BuildDrumTrack(
        ProjectDocument project,
        GenerationSettings settings,
        long beatTicks,
        long barTicks)
    {
        ulong seed = project.MasterSeed;
        DeterministicRandom random = Stream(seed, "track", "drums");
        SongTrack track = NewTrack(seed, "drums", "Drums", TrackRole.Drums, 9, 0, true);
        long eighth = beatTicks / 2;

        foreach (SongSection section in project.Song.Sections)
        {
            SongClip clip = NewClip(seed, track.Id, section);
            int startBar = (int)(section.StartTick / barTicks);
            int barCount = (int)(section.LengthTicks / barTicks);

            for (int localBar = 0; localBar < barCount; localBar++)
            {
                int globalBar = startBar + localBar;
                for (int step = 0; step < settings.BeatsPerBar * 2; step++)
                {
                    long start = (localBar * barTicks) + (step * eighth);
                    bool onBeat = step % 2 == 0;
                    int hatPitch = step % 4 == 3 && random.Chance(0.22) ? 46 : 42;
                    int hatVelocity = onBeat ? 72 + random.NextInt(20) : 48 + random.NextInt(22);
                    if (!(section.Name == "Intro" && random.Chance(0.28)))
                    {
                        clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-hat-{step}", hatPitch, start, Math.Max(1, eighth / 3), hatVelocity));
                    }

                    int beat = step / 2;
                    if (onBeat && (beat == 0 || beat == 2 || (beat == 3 && random.Chance(0.25))))
                    {
                        clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-kick-{step}", 36, start, Math.Max(1, eighth / 2), 92 + random.NextInt(25)));
                    }

                    if (onBeat && (beat == 1 || beat == 3))
                    {
                        clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-snare-{step}", 38, start, Math.Max(1, eighth / 2), 88 + random.NextInt(28)));
                    }
                }

                if ((globalBar + 1) % 4 == 0 && section.Name != "Intro")
                {
                    for (int fill = 0; fill < 4; fill++)
                    {
                        long start = (localBar * barTicks) + barTicks - beatTicks + (fill * beatTicks / 4);
                        int pitch = 45 + fill;
                        clip.Notes.Add(NewNote(seed, track.Id, section.Id, $"bar-{globalBar}-fill-{fill}", pitch, start, Math.Max(1, beatTicks / 6), 72 + (fill * 9)));
                    }
                }
            }

            track.Clips.Add(clip);
        }

        return track;
    }

    private static List<SongSection> BuildSections(ulong seed, int bars, long barTicks)
    {
        int introBars = Math.Max(2, bars / 4);
        int outroBars = Math.Max(2, bars / 4);
        int bodyBars = bars - introBars - outroBars;
        if (bodyBars <= 0)
        {
            introBars = 1;
            outroBars = 1;
            bodyBars = bars - 2;
        }

        (string Name, int Bars, double Energy)[] definitions =
        {
            ("Intro", introBars, 0.30),
            ("A", bodyBars, 0.72),
            ("Outro", outroBars, 0.42)
        };

        List<SongSection> sections = new();
        long cursor = 0;
        foreach ((string name, int sectionBars, double energy) in definitions)
        {
            sections.Add(new SongSection
            {
                Id = SeedDerivation.StableId("section", seed, name.ToLowerInvariant()),
                Name = name,
                StartTick = cursor,
                LengthTicks = sectionBars * barTicks,
                Energy = energy
            });
            cursor += sectionBars * barTicks;
        }

        return sections;
    }

    private static SongTrack NewTrack(
        ulong seed,
        string key,
        string name,
        TrackRole role,
        int channel,
        int program,
        bool percussion) =>
        new()
        {
            Id = SeedDerivation.StableId("track", seed, key),
            Name = name,
            Role = role,
            MidiChannel = channel,
            GeneralMidiProgram = program,
            IsPercussion = percussion,
            Clips = new List<SongClip>()
        };

    private static SongClip NewClip(ulong seed, string trackId, SongSection section) =>
        new()
        {
            Id = SeedDerivation.StableId("clip", seed, trackId, section.Id),
            Name = section.Name,
            StartTick = section.StartTick,
            LengthTicks = section.LengthTicks,
            Notes = new List<NoteEvent>()
        };

    private static NoteEvent NewNote(
        ulong seed,
        string trackId,
        string sectionId,
        string key,
        int pitch,
        long start,
        long duration,
        int velocity) =>
        new()
        {
            Id = SeedDerivation.StableId("note", seed, trackId, sectionId, key),
            Pitch = Math.Clamp(pitch, 0, 127),
            StartTick = Math.Max(0, start),
            DurationTicks = Math.Max(1, duration),
            Velocity = Math.Clamp(velocity, 1, 127)
        };

    private static int[] SelectProgression(ulong seed, ScaleKind kind)
    {
        DeterministicRandom random = Stream(seed, "harmony", "progression");
        int[][] source = kind is ScaleKind.Major or ScaleKind.Mixolydian ? MajorProgressions : MinorProgressions;
        return source[random.NextInt(source.Length)].ToArray();
    }

    private static int[] BuildMotif(DeterministicRandom random)
    {
        int[] choices = { -2, -1, 0, 1, 1, 2 };
        int[] motif = new int[8];
        for (int index = 0; index < motif.Length; index++)
        {
            motif[index] = choices[random.NextInt(choices.Length)];
        }

        motif[0] = random.Chance(0.5) ? 1 : -1;
        motif[^1] = -motif[0];
        return motif;
    }

    private static int NearestDegree(int source, IReadOnlyList<int> candidates, int scaleCount)
    {
        int best = source;
        int bestDistance = int.MaxValue;
        foreach (int candidate in candidates)
        {
            for (int octave = -2; octave <= 2; octave++)
            {
                int expanded = candidate + (octave * scaleCount);
                int distance = Math.Abs(expanded - source);
                if (distance < bestDistance)
                {
                    best = expanded;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    private static string BuildTitle(PitchClass root, ScaleKind kind, ulong seed)
    {
        string[] adjectives = { "Amber", "Clockwork", "Distant", "Electric", "Fractal", "Hidden", "Luminous", "Midnight" };
        string[] nouns = { "Arc", "Garden", "Machine", "Orbit", "Pattern", "Signal", "Thread", "Window" };
        DeterministicRandom random = Stream(seed, "identity", "title");
        return $"{adjectives[random.NextInt(adjectives.Length)]} {nouns[random.NextInt(nouns.Length)]} in {root.ToDisplayName()} {ScaleLibrary.Get(kind).Name}";
    }

    private static DeterministicRandom Stream(ulong seed, params string[] path) =>
        new(SeedDerivation.Derive(seed, path));
}
