using System.Globalization;
using PlayromaMusic.Core.Generation;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.Validation;
using PlayromaMusic.Generators.Algorithms;
using PlayromaMusic.Generators.Graphs;
using PlayromaMusic.Generators.Presets;

namespace PlayromaMusic.Generators.Lab;

public sealed class LabGenerationService
{
    public const string DefaultPresetId = "fractmus-orchestra";

    private readonly AlgorithmSourceFactory _sourceFactory = new();
    private readonly ValueGraphEvaluator _graphEvaluator = new();
    private readonly FoundationSongGenerator _foundationGenerator = new();

    public ProjectDocument CreateProject(
        ulong masterSeed,
        string presetId = DefaultPresetId,
        GenerationSettings? settings = null)
    {
        ProjectDocument project = _foundationGenerator.Generate(masterSeed, settings);
        LabPresetDefinition preset = LabPresetLibrary.Get(presetId);
        project.GenerationLab = LabPresetLibrary.CreateLab(presetId);
        project.Tags.RemoveAll(tag => string.Equals(tag, "stage1", StringComparison.OrdinalIgnoreCase));
        project.Tags.AddRange(new[] { "stage2", "generation-laboratory", "fractmus-style", preset.Id });
        project.Title = $"{preset.Name} - {project.Title}";
        project.Song.Title = project.Title;
        project.Song.Tracks.Clear();
        RebuildTracks(project, advanceRevisions: false, includeLocked: true);
        ValidateOrThrow(project);
        return project;
    }

    public ProjectDocument ApplyPreset(ProjectDocument project, string presetId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectDocument replacement = CreateProject(project.MasterSeed, presetId, SettingsFrom(project));
        replacement.Song.KeyRoot = project.Song.KeyRoot;
        replacement.Song.Scale = project.Song.Scale;
        replacement.Song.TempoMap = project.Song.TempoMap.Select(tempo => new TempoChange
        {
            Tick = tempo.Tick,
            BeatsPerMinute = tempo.BeatsPerMinute
        }).ToList();
        RebuildTracks(replacement, advanceRevisions: false, includeLocked: true);
        ValidateOrThrow(replacement);
        return replacement;
    }

    public void RegenerateVoice(
        ProjectDocument project,
        string voiceId,
        bool advanceRevision = true,
        bool forceLocked = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        AlgorithmVoiceDefinition voice = GetVoice(project, voiceId);
        NormalizeVoiceSettings(voice);
        if (voice.Locked && !forceLocked)
        {
            return;
        }

        if (advanceRevision)
        {
            voice.Revision++;
        }

        ReplaceTrack(project, voice);
        OrderTracks(project);
        ValidateOrThrow(project);
    }

    public void RegenerateUnlocked(ProjectDocument project, bool advanceRevisions = true)
    {
        RebuildTracks(project, advanceRevisions, includeLocked: false);
        ValidateOrThrow(project);
    }

    public void RebuildAll(ProjectDocument project)
    {
        RebuildTracks(project, advanceRevisions: false, includeLocked: true);
        ValidateOrThrow(project);
    }

    public GraphEvaluationResult PreviewVoice(ProjectDocument project, string voiceId, int? sampleCount = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        AlgorithmVoiceDefinition voice = GetVoice(project, voiceId);
        NormalizeVoiceSettings(voice);
        int count = Math.Clamp(sampleCount ?? project.GenerationLab.PreviewSampleCount, 16, 8192);
        ulong sourceSeed = GetVoiceSeed(project, voice);
        AlgorithmSeries series = _sourceFactory.Generate(
            voice.Algorithm,
            sourceSeed,
            count,
            voice.AlgorithmParameters);
        return _graphEvaluator.Evaluate(series, voice.Graph);
    }

    public static AlgorithmVoiceDefinition GetVoice(ProjectDocument project, string voiceId) =>
        project.GenerationLab.Voices.FirstOrDefault(voice =>
            string.Equals(voice.Id, voiceId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown generation voice: {voiceId}");

    public static SongTrack? GetTrack(ProjectDocument project, string voiceId) =>
        project.Song.Tracks.FirstOrDefault(track =>
            string.Equals(track.SourceVoiceId, voiceId, StringComparison.Ordinal));

    public static ulong GetVoiceSeed(ProjectDocument project, AlgorithmVoiceDefinition voice) =>
        SeedDerivation.Derive(
            project.MasterSeed,
            "stage2",
            "voice",
            voice.Id,
            voice.Revision.ToString(CultureInfo.InvariantCulture));

    private void RebuildTracks(ProjectDocument project, bool advanceRevisions, bool includeLocked)
    {
        if (!project.GenerationLab.Enabled)
        {
            throw new InvalidOperationException("The generation laboratory is not enabled for this project.");
        }

        foreach (AlgorithmVoiceDefinition voice in project.GenerationLab.Voices)
        {
            NormalizeVoiceSettings(voice);
            if (!includeLocked && voice.Locked)
            {
                continue;
            }

            if (advanceRevisions)
            {
                voice.Revision++;
            }

            ReplaceTrack(project, voice);
        }

        OrderTracks(project);
    }

    private void ReplaceTrack(ProjectDocument project, AlgorithmVoiceDefinition voice)
    {
        int existing = project.Song.Tracks.FindIndex(track =>
            string.Equals(track.SourceVoiceId, voice.Id, StringComparison.Ordinal));

        if (!voice.Enabled)
        {
            if (existing >= 0)
            {
                project.Song.Tracks.RemoveAt(existing);
            }

            return;
        }

        SongTrack track = GenerateTrack(project, voice);
        if (existing >= 0)
        {
            project.Song.Tracks[existing] = track;
        }
        else
        {
            project.Song.Tracks.Add(track);
        }
    }

    private SongTrack GenerateTrack(ProjectDocument project, AlgorithmVoiceDefinition voice)
    {
        MeterChange meter = project.Song.MeterMap.OrderBy(change => change.Tick).First();
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / meter.Denominator;
        long stepTicks = Math.Max(1, beatTicks / voice.RhythmMapping.StepsPerBeat);
        long songLength = Math.Max(1, project.Song.LengthTicks);
        int totalSteps = checked((int)Math.Min(1_000_000, ((songLength + stepTicks - 1) / stepTicks) + 1));

        ulong sourceSeed = GetVoiceSeed(project, voice);
        AlgorithmSeries source = _sourceFactory.Generate(
            voice.Algorithm,
            sourceSeed,
            totalSteps,
            voice.AlgorithmParameters);
        GraphEvaluationResult graph = _graphEvaluator.Evaluate(source, voice.Graph);
        double[] pitchValues = ValueGraphEvaluator.Normalize01(graph.PitchValues);
        double[] rhythmValues = ValueGraphEvaluator.Normalize01(graph.RhythmValues);
        double[] velocityValues = ValueGraphEvaluator.Normalize01(graph.VelocityValues);
        double[] controlValues = ValueGraphEvaluator.Normalize01(graph.ControlValues);

        SongTrack track = new()
        {
            Id = SeedDerivation.StableId("track", project.MasterSeed, "stage2", voice.Id),
            Name = voice.Name,
            Role = voice.Role,
            MidiChannel = voice.MidiChannel,
            GeneralMidiProgram = voice.GeneralMidiProgram,
            IsPercussion = voice.IsPercussion,
            SourceVoiceId = voice.Id,
            GeneratorId = voice.Algorithm.ToString(),
            GenerationRevision = voice.Revision,
            Clips = project.Song.Sections.Select(section => new SongClip
            {
                Id = SeedDerivation.StableId("clip", project.MasterSeed, "stage2", voice.Id, section.Id),
                Name = $"{voice.Name} - {section.Name}",
                StartTick = section.StartTick,
                LengthTicks = section.LengthTicks
            }).ToList()
        };
        Dictionary<string, SongClip> clipsBySectionId = project.Song.Sections
            .Zip(track.Clips, (section, clip) => new { section.Id, Clip = clip })
            .ToDictionary(item => item.Id, item => item.Clip, StringComparer.Ordinal);

        for (int step = 0; step < totalSteps; step++)
        {
            long gridTick = step * stepTicks;
            if (gridTick >= songLength)
            {
                break;
            }

            SongSection? section = FindSection(project.Song.Sections, gridTick);
            if (section is null)
            {
                continue;
            }

            SongClip clip = clipsBySectionId[section.Id];
            long swingOffset = step % 2 == 1
                ? (long)Math.Round(stepTicks * voice.RhythmMapping.Swing * 0.5)
                : 0;
            long absoluteStart = Math.Min(section.EndTick - 1, gridTick + swingOffset);
            long localStart = absoluteStart - section.StartTick;

            if (voice.ControlChangeMapping.Enabled && step % voice.ControlChangeMapping.SampleEverySteps == 0)
            {
                int controlValue = MapInt(
                    controlValues[step],
                    voice.ControlChangeMapping.MinimumValue,
                    voice.ControlChangeMapping.MaximumValue);
                clip.ControlChanges.Add(new ControlChangeEvent
                {
                    Id = SeedDerivation.StableId(
                        "cc",
                        project.MasterSeed,
                        voice.Id,
                        voice.Revision.ToString(CultureInfo.InvariantCulture),
                        step.ToString(CultureInfo.InvariantCulture)),
                    ControllerNumber = voice.ControlChangeMapping.ControllerNumber,
                    Value = controlValue,
                    StartTick = localStart
                });
            }

            if (rhythmValues[step] < voice.RhythmMapping.GateThreshold)
            {
                continue;
            }

            int pitch = MapPitch(project, voice, pitchValues[step]);
            int durationSteps = MapInt(
                rhythmValues[step],
                voice.RhythmMapping.MinimumDurationSteps,
                voice.RhythmMapping.MaximumDurationSteps);
            long duration = Math.Max(1, (long)Math.Round(durationSteps * stepTicks * voice.RhythmMapping.NoteLengthRatio));
            duration = Math.Min(duration, section.LengthTicks - localStart);
            if (duration <= 0)
            {
                continue;
            }

            int velocity = MapInt(
                velocityValues[step],
                voice.VelocityMapping.MinimumVelocity,
                voice.VelocityMapping.MaximumVelocity);
            if (voice.VelocityMapping.AccentEverySteps > 0 && step % voice.VelocityMapping.AccentEverySteps == 0)
            {
                velocity += voice.VelocityMapping.AccentAmount;
            }

            clip.Notes.Add(new NoteEvent
            {
                Id = SeedDerivation.StableId(
                    "note",
                    project.MasterSeed,
                    voice.Id,
                    voice.Revision.ToString(CultureInfo.InvariantCulture),
                    step.ToString(CultureInfo.InvariantCulture)),
                Pitch = pitch,
                StartTick = localStart,
                DurationTicks = duration,
                Velocity = Math.Clamp(velocity, 1, 127)
            });
        }

        return track;
    }

    private static int MapPitch(ProjectDocument project, AlgorithmVoiceDefinition voice, double value)
    {
        ScaleMapperSettings mapping = voice.PitchMapping;
        if (voice.IsPercussion)
        {
            int index = MapInt(value, 0, mapping.PercussionPalette.Count - 1);
            return mapping.PercussionPalette[index];
        }

        ScaleDefinition scale = ScaleLibrary.Get(mapping.UseProjectScale ? project.Song.Scale : mapping.ScaleOverride);
        int degree = MapInt(value, mapping.MinimumDegree, mapping.MaximumDegree);
        int pitch = scale.MidiPitchForDegree(project.Song.KeyRoot, degree, mapping.BaseOctave);
        while (pitch < mapping.MinimumMidiPitch && pitch <= 115)
        {
            pitch += 12;
        }

        while (pitch > mapping.MaximumMidiPitch && pitch >= 12)
        {
            pitch -= 12;
        }

        pitch = Math.Clamp(pitch, mapping.MinimumMidiPitch, mapping.MaximumMidiPitch);
        pitch = scale.NearestPitch(project.Song.KeyRoot, pitch);
        return Math.Clamp(pitch, mapping.MinimumMidiPitch, mapping.MaximumMidiPitch);
    }

    private static int MapInt(double normalized, int minimum, int maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        double value = Math.Clamp(double.IsFinite(normalized) ? normalized : 0.5, 0.0, 1.0);
        return minimum + (int)Math.Round(value * (maximum - minimum));
    }

    private static SongSection? FindSection(IEnumerable<SongSection> sections, long tick) =>
        sections.FirstOrDefault(section => tick >= section.StartTick && tick < section.EndTick);

    private static void OrderTracks(ProjectDocument project)
    {
        Dictionary<string, int> order = project.GenerationLab.Voices
            .Select((voice, index) => new { voice.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        project.Song.Tracks = project.Song.Tracks
            .OrderBy(track => order.GetValueOrDefault(track.SourceVoiceId, int.MaxValue))
            .ThenBy(track => track.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static void NormalizeVoiceSettings(AlgorithmVoiceDefinition voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        voice.MidiChannel = Math.Clamp(voice.MidiChannel, 0, 15);
        voice.GeneralMidiProgram = Math.Clamp(voice.GeneralMidiProgram, 0, 127);
        if (voice.IsPercussion)
        {
            voice.MidiChannel = 9;
        }

        ScaleMapperSettings pitch = voice.PitchMapping;
        pitch.MinimumDegree = Math.Clamp(pitch.MinimumDegree, -128, 128);
        pitch.MaximumDegree = Math.Clamp(pitch.MaximumDegree, pitch.MinimumDegree, 128);
        pitch.MinimumMidiPitch = Math.Clamp(pitch.MinimumMidiPitch, 0, 126);
        pitch.MaximumMidiPitch = Math.Clamp(pitch.MaximumMidiPitch, pitch.MinimumMidiPitch + 1, 127);
        pitch.PercussionPalette = pitch.PercussionPalette
            .Where(value => value is >= 0 and <= 127)
            .Distinct()
            .ToList();
        if (voice.IsPercussion && pitch.PercussionPalette.Count == 0)
        {
            pitch.PercussionPalette.AddRange([36, 38, 42, 46]);
        }

        RhythmMapperSettings rhythm = voice.RhythmMapping;
        rhythm.StepsPerBeat = Math.Clamp(rhythm.StepsPerBeat, 1, 32);
        rhythm.GateThreshold = ClampFinite(rhythm.GateThreshold, 0, 1, 0.25);
        rhythm.MinimumDurationSteps = Math.Clamp(rhythm.MinimumDurationSteps, 1, 64);
        rhythm.MaximumDurationSteps = Math.Clamp(rhythm.MaximumDurationSteps, rhythm.MinimumDurationSteps, 64);
        rhythm.NoteLengthRatio = ClampFinite(rhythm.NoteLengthRatio, 0.05, 1.5, 0.82);
        // The renderer delays off-beats; negative values previously produced an invalid project.
        rhythm.Swing = ClampFinite(rhythm.Swing, 0, 0.95, 0);

        VelocityMapperSettings velocity = voice.VelocityMapping;
        velocity.MinimumVelocity = Math.Clamp(velocity.MinimumVelocity, 1, 127);
        velocity.MaximumVelocity = Math.Clamp(velocity.MaximumVelocity, velocity.MinimumVelocity, 127);
        velocity.AccentEverySteps = Math.Max(0, velocity.AccentEverySteps);
        velocity.AccentAmount = Math.Clamp(velocity.AccentAmount, -127, 127);

        ControlChangeMapperSettings control = voice.ControlChangeMapping;
        control.ControllerNumber = Math.Clamp(control.ControllerNumber, 0, 127);
        control.MinimumValue = Math.Clamp(control.MinimumValue, 0, 127);
        control.MaximumValue = Math.Clamp(control.MaximumValue, control.MinimumValue, 127);
        control.SampleEverySteps = Math.Max(1, control.SampleEverySteps);

        foreach (string key in voice.AlgorithmParameters.Keys.ToArray())
        {
            if (!double.IsFinite(voice.AlgorithmParameters[key]))
            {
                voice.AlgorithmParameters[key] = 0;
            }
        }

        foreach (ValueTransformNodeDefinition node in voice.Graph.Nodes)
        {
            foreach (string key in node.Parameters.Keys.ToArray())
            {
                if (!double.IsFinite(node.Parameters[key]))
                {
                    node.Parameters[key] = 0;
                }
            }
        }
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);

    private static GenerationSettings SettingsFrom(ProjectDocument project)
    {
        MeterChange meter = project.Song.MeterMap.OrderBy(change => change.Tick).First();
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / meter.Denominator;
        long barTicks = beatTicks * meter.Numerator;
        int bars = Math.Max(4, (int)Math.Ceiling(project.Song.LengthTicks / (double)barTicks));
        double tempo = project.Song.TempoMap.OrderBy(change => change.Tick).First().BeatsPerMinute;
        int roundedTempo = Math.Clamp((int)Math.Round(tempo), 20, 300);
        return new GenerationSettings
        {
            Bars = bars,
            BeatsPerBar = meter.Numerator,
            BeatUnit = meter.Denominator,
            TicksPerQuarterNote = project.Song.TicksPerQuarterNote,
            MinimumTempo = roundedTempo,
            MaximumTempo = roundedTempo
        };
    }

    private static void ValidateOrThrow(ProjectDocument project)
    {
        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Generated laboratory project failed validation: " + string.Join("; ", validation.Errors));
        }
    }
}
