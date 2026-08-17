using System.Globalization;
using PlayromaMusic.Core;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Extensions;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.Validation;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.Advanced;
using PlayromaMusic.Generators.Extensions;
using PlayromaMusic.Generators.Rhythms;
using PlayromaMusic.Generators.Orchestration;
using PlayromaMusic.Generators.Scripting;

namespace PlayromaMusic.Generators.Structured;

public sealed class StructuredCompositionService
{
    public const string DefaultStyleId = "pop-modern";

    private static readonly PitchClass[] KeyRoots =
    {
        PitchClass.C, PitchClass.D, PitchClass.DSharp, PitchClass.E,
        PitchClass.F, PitchClass.G, PitchClass.A, PitchClass.ASharp
    };

    private static readonly string[] TitleAdjectives =
    {
        "Amber", "Luminous", "Silent", "Velvet", "Electric", "Distant", "Silver", "Hidden", "Midnight", "Glass"
    };

    private static readonly string[] TitleNouns =
    {
        "Arc", "Memory", "Circuit", "Garden", "Voyage", "Signal", "Lantern", "Horizon", "Machine", "Constellation"
    };

    private readonly FormGrammarEngine _formEngine = new();
    private readonly HarmonicGrammarEngine _harmonyEngine = new();
    private readonly MotifEngine _motifEngine = new();
    private readonly PhraseCandidateScorer _scorer = new();
    private readonly ConstraintVoicingSolver _voicingSolver = new();
    private readonly AdaptiveTensionPlanner _tensionPlanner = new();
    private readonly LongRangeMotifPlanner _motifPlanner = new();
    private readonly EvolutionaryArrangementOptimizer _arrangementOptimizer = new();
    private readonly MusicalExpressionEngine _expressionEngine = new();
    private readonly ProceduralOrchestrationPlanner _orchestrationPlanner = new();

    public ProjectDocument CreateProject(ulong masterSeed, string styleId = DefaultStyleId)
    {
        StructuredStyleDefinition style = StructuredStyleLibrary.Get(styleId);
        return CreateProjectFromResolvedStyle(masterSeed, style, null);
    }

    private ProjectDocument CreateProjectFromResolvedStyle(ulong masterSeed, StructuredStyleDefinition style, StyleBlendSettings? blend)
    {
        DeterministicRandom identity = Random(masterSeed, "identity", style.Id);
        ScaleKind scaleKind = style.Scales[identity.NextInt(style.Scales.Count)];
        PitchClass keyRoot = KeyRoots[identity.NextInt(KeyRoots.Length)];
        int tempo = identity.NextInt(style.MinimumTempo, style.MaximumTempo + 1);
        string title = BuildTitle(masterSeed, style, keyRoot, scaleKind);

        ProjectDocument project = new()
        {
            SchemaVersion = BuildInfo.ProjectSchemaVersion,
            EngineVersion = BuildInfo.Version,
            ProjectId = SeedDerivation.StableId("project", masterSeed, "stage3", style.Id),
            Title = title,
            MasterSeed = masterSeed,
            Tags = new List<string> { "stage7", "procedural-orchestration", "advanced-composition", "structured-tonal", "style-pack", style.Family.ToString().ToLowerInvariant(), style.Id },
            GenerationLab = new GenerationLabDocument { Enabled = false },
            AdvancedComposition = new AdvancedCompositionDocument { Enabled = true },
            Orchestration = new ProceduralOrchestrationDocument
            {
                Enabled = true,
                Catalog = PresetCatalogFactory.CreateGeneralMidiFallback()
            },
            StructuredComposition = new StructuredCompositionDocument
            {
                Enabled = true,
                ActiveStyleId = style.Id,
                FormGrammarId = style.FormGrammarId,
                HarmonicGrammarId = style.HarmonicGrammarId,
                CandidateCount = style.CandidateCount,
                BarsPerPhrase = 2,
                Roles = StructuredStyleLibrary.CloneRoles(style),
                HybridStyle = blend?.Clone() ?? new StyleBlendSettings
                {
                    Enabled = false,
                    PrimaryPackId = style.Id
                },
                ResolvedStylePackIds = style.EffectiveSourcePackIds.Count > 0
                    ? style.EffectiveSourcePackIds.ToList()
                    : new List<string> { style.Id }
            },
            Song = new SongDocument
            {
                Title = title,
                TicksPerQuarterNote = BuildInfo.DefaultTicksPerQuarterNote,
                KeyRoot = keyRoot,
                Scale = scaleKind,
                TempoMap = new List<TempoChange> { new() { Tick = 0, BeatsPerMinute = tempo } },
                MeterMap = new List<MeterChange>
                {
                    new() { Tick = 0, Numerator = style.BeatsPerBar, Denominator = style.BeatUnit }
                }
            }
        };

        ProceduralOrchestrationPlanner.EnsureRoleSelectors(project.StructuredComposition.Roles);
        RebuildStructure(project, advanceRevision: false);
        return project;
    }

    public ProjectDocument ApplyStyle(ProjectDocument project, string styleId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectDocument replacement = CreateProject(project.MasterSeed, styleId);
        replacement.ProjectId = project.ProjectId;
        replacement.StructuredComposition.HybridStyle.Enabled = false;
        return replacement;
    }

    public ProjectDocument CreateHybridProject(ulong masterSeed, StyleBlendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StyleBlendSettings normalized = NormalizeBlend(settings);
        StructuredStyleDefinition style = StructuredStyleLibrary.ComposeHybrid(normalized);
        normalized.Enabled = true;
        ProjectDocument project = CreateProjectFromResolvedStyle(masterSeed, style, normalized);
        project.StructuredComposition.HybridStyle = normalized;
        project.StructuredComposition.HybridStyle.Enabled = true;
        project.StructuredComposition.ActiveStyleId = style.Id;
        project.StructuredComposition.ResolvedStylePackIds = style.EffectiveSourcePackIds.ToList();
        project.Tags.RemoveAll(tag => tag.StartsWith("hybrid:", StringComparison.OrdinalIgnoreCase));
        project.Tags.Add($"hybrid:{normalized.PrimaryPackId}+{normalized.SecondaryPackId}:{normalized.SecondaryWeight}");
        return project;
    }

    public ProjectDocument ApplyHybrid(ProjectDocument project, StyleBlendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectDocument replacement = CreateHybridProject(project.MasterSeed, settings);
        replacement.ProjectId = project.ProjectId;
        return replacement;
    }

    public void RegenerateStructure(ProjectDocument project)
    {
        EnsureStructured(project);
        RebuildStructure(project, advanceRevision: true);
    }

    public void RegenerateRole(ProjectDocument project, string roleId, bool forceLocked = false)
    {
        EnsureStructured(project);
        CompositionRoleSettings role = GetRole(project, roleId);
        if (role.Locked && !forceLocked)
        {
            throw new InvalidOperationException($"Role {role.Name} is locked.");
        }

        role.Revision++;
        ReplaceRoleTrack(project, role);
        Validate(project);
    }

    public void RegenerateUnlocked(ProjectDocument project)
    {
        EnsureStructured(project);
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(role => role.Enabled && !role.Locked))
        {
            role.Revision++;
            ReplaceRoleTrack(project, role);
        }

        OrderTracks(project);
        Validate(project);
    }

    public void RegenerateMotif(ProjectDocument project)
    {
        EnsureStructured(project);
        int revision = (project.StructuredComposition.Motifs.FirstOrDefault()?.Revision ?? 0) + 1;
        StructuredStyleDefinition style = ResolveStyle(project.StructuredComposition);
        project.StructuredComposition.Motifs = new List<MotifDefinition>
        {
            _motifEngine.Generate(project.MasterSeed, style, revision)
        };
        project.AdvancedComposition.MotifOccurrences = project.AdvancedComposition.Enabled && project.AdvancedComposition.MotifConstraints.Enabled
            ? _motifPlanner.Apply(
                project.StructuredComposition.FormPlan,
                project.StructuredComposition.Motifs[0],
                project.AdvancedComposition.MotifConstraints).ToList()
            : new List<MotifOccurrencePlan>();
        CompositionRoleSettings melody = project.StructuredComposition.Roles.First(role => role.Role == TrackRole.Lead);
        melody.Revision++;
        ReplaceRoleTrack(project, melody);
        Validate(project);
    }

    public void RegenerateAdvanced(ProjectDocument project)
    {
        EnsureStructured(project);
        StructuredCompositionDocument composition = project.StructuredComposition;
        StructuredStyleDefinition style = ResolveStyle(composition);
        ulong structureSeed = SeedDerivation.Derive(
            project.MasterSeed,
            "structure-revision",
            composition.GenerationRevision.ToString(CultureInfo.InvariantCulture));
        composition.HarmonyPlan = _harmonyEngine.Generate(structureSeed, style, composition.FormPlan).ToList();
        ApplyAdvancedPlans(project);
        SynchronizeSongSections(project);
        composition.CandidateScores.Clear();
        foreach (CompositionRoleSettings role in composition.Roles.Where(role => role.Enabled))
        {
            ReplaceRoleTrack(project, role);
        }
        OrderTracks(project);
        Validate(project);
    }

    private static void SynchronizeSongSections(ProjectDocument project)
    {
        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / Math.Max(1, meter.Denominator);
        long barTicks = beatTicks * Math.Max(1, meter.Numerator);
        project.Song.Sections = project.StructuredComposition.FormPlan
            .OrderBy(section => section.StartBar)
            .Select(section => new SongSection
            {
                Id = section.Id,
                Name = section.Name,
                StartTick = section.StartBar * barTicks,
                LengthTicks = section.LengthBars * barTicks,
                Energy = section.Energy
            })
            .ToList();
    }

    public void RebuildFromCurrentPlans(ProjectDocument project, bool replanOrchestration = true)
    {
        EnsureStructured(project);
        StructuredCompositionDocument composition = project.StructuredComposition;
        if (composition.FormPlan.Count == 0) throw new InvalidOperationException("A current form plan is required.");
        if (composition.HarmonyPlan.Count == 0) throw new InvalidOperationException("A current harmony plan is required.");
        if (composition.Motifs.Count == 0) throw new InvalidOperationException("At least one current motif is required.");
        SynchronizeSongSections(project);
        if (replanOrchestration && project.Orchestration?.Enabled == true)
        {
            StructuredStyleDefinition style = ResolveStyle(composition);
            _orchestrationPlanner.Plan(project, style.Family);
        }
        composition.CandidateScores.Clear();
        project.Song.Tracks.Clear();
        foreach (CompositionRoleSettings role in composition.Roles.Where(role => role.Enabled))
        {
            project.Song.Tracks.Add(GenerateRoleTrack(project, role));
        }
        BuildLayerTracks(project);
        OrderTracks(project);
        Validate(project);
    }

    public static CompositionRoleSettings GetRole(ProjectDocument project, string roleId) =>
        project.StructuredComposition.Roles.FirstOrDefault(role => string.Equals(role.Id, roleId, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown composition role: {roleId}.");

    public static SongTrack? GetTrack(ProjectDocument project, string roleId) =>
        project.Song.Tracks.FirstOrDefault(track => string.Equals(track.SourceVoiceId, roleId, StringComparison.Ordinal));

    private void RebuildStructure(ProjectDocument project, bool advanceRevision)
    {
        StructuredCompositionDocument composition = project.StructuredComposition;
        StructuredStyleDefinition style = ResolveStyle(composition);
        if (advanceRevision)
        {
            composition.GenerationRevision++;
        }

        ulong structureSeed = SeedDerivation.Derive(
            project.MasterSeed,
            "structure-revision",
            composition.GenerationRevision.ToString(CultureInfo.InvariantCulture));
        IReadOnlyList<FormSectionPlan> form = _formEngine.Generate(structureSeed, style, out List<FormDerivationStep> derivation);
        composition.FormDerivation = derivation;
        composition.FormPlan = form.ToList();
        composition.HarmonyPlan = _harmonyEngine.Generate(structureSeed, style, form).ToList();
        composition.Motifs = new List<MotifDefinition>
        {
            _motifEngine.Generate(project.MasterSeed, style, composition.Motifs.FirstOrDefault()?.Revision ?? 0)
        };
        composition.CandidateScores.Clear();

        ApplyAdvancedPlans(project);

        SynchronizeSongSections(project);

        project.Song.Tracks.Clear();
        foreach (CompositionRoleSettings role in composition.Roles.Where(role => role.Enabled))
        {
            project.Song.Tracks.Add(GenerateRoleTrack(project, role));
        }
        BuildLayerTracks(project);

        OrderTracks(project);
        Validate(project);
    }

    private void ReplaceRoleTrack(ProjectDocument project, CompositionRoleSettings role)
    {
        int index = project.Song.Tracks.FindIndex(track => string.Equals(track.SourceVoiceId, role.Id, StringComparison.Ordinal));
        project.Song.Tracks.RemoveAll(track => track.SourceVoiceId.StartsWith(role.Id + "#layer", StringComparison.Ordinal));
        project.StructuredComposition.CandidateScores.RemoveAll(score => string.Equals(score.RoleId, role.Id, StringComparison.Ordinal));
        if (!role.Enabled)
        {
            if (index >= 0) project.Song.Tracks.RemoveAt(index);
            return;
        }

        SongTrack replacement = GenerateRoleTrack(project, role);
        if (index >= 0) project.Song.Tracks[index] = replacement;
        else project.Song.Tracks.Add(replacement);
        BuildLayerTracksForRole(project, role);
        OrderTracks(project);
    }

    private SongTrack GenerateRoleTrack(ProjectDocument project, CompositionRoleSettings role)
    {
        SongTrack track = new()
        {
            Id = SeedDerivation.StableId("track", project.MasterSeed, "stage3", role.Id),
            Name = role.Name,
            Role = role.Role,
            MidiChannel = role.MidiChannel,
            GeneralMidiProgram = role.GeneralMidiProgram,
            IsPercussion = role.IsPercussion,
            SourceVoiceId = role.Id,
            GeneratorId = string.IsNullOrWhiteSpace(role.GeneratorId) ? "StructuredTonal." + role.Role : role.GeneratorId,
            GenerationRevision = role.Revision,
            Clips = project.Song.Sections.Select(section => new SongClip
            {
                Id = SeedDerivation.StableId("clip", project.MasterSeed, "stage3", role.Id, section.Id),
                Name = $"{role.Name} - {section.Name}",
                StartTick = section.StartTick,
                LengthTicks = section.LengthTicks
            }).ToList()
        };

        ApplyPrimaryInstrumentPlan(project, role, track);

        Dictionary<string, SongClip> clips = project.Song.Sections
            .Zip(track.Clips, (section, clip) => new { section.Id, Clip = clip })
            .ToDictionary(item => item.Id, item => item.Clip, StringComparer.Ordinal);

        bool pluginGenerated = TryGenerateWithPlugin(project, role, clips, track);
        if (!pluginGenerated)
        {
            switch (role.Role)
            {
            case TrackRole.Lead:
                GenerateMelody(project, role, clips);
                break;
            case TrackRole.Bass:
                GenerateBass(project, role, clips);
                break;
            case TrackRole.Chords:
            case TrackRole.Harmony:
                GenerateChords(project, role, clips);
                break;
            case TrackRole.Arpeggio:
                GenerateArpeggio(project, role, clips);
                break;
            case TrackRole.Drums:
                GenerateDrums(project, role, clips);
                break;
            default:
                GenerateArpeggio(project, role, clips);
                break;
            }
        }
        ApplyArrangementIntensity(project, role, track);
        _expressionEngine.Apply(project, role, track);
        return track;
    }

    private void GenerateMelody(ProjectDocument project, CompositionRoleSettings role, IReadOnlyDictionary<string, SongClip> clips)
    {
        StructuredCompositionDocument composition = project.StructuredComposition;
        StructuredStyleDefinition activeStyle = ResolveStyle(composition);
        MotifDefinition motif = composition.Motifs[0];
        ScaleDefinition scale = ScaleLibrary.Get(project.Song.Scale);
        Timing timing = GetTiming(project);

        foreach (FormSectionPlan sectionPlan in composition.FormPlan)
        {
            if (!IsRoleActive(project, role.Id, sectionPlan.Id)) continue;
            SongClip clip = clips[sectionPlan.Id];
            int phraseBars = Math.Max(1, Math.Min(composition.BarsPerPhrase, sectionPlan.LengthBars));
            int phraseCount = (int)Math.Ceiling(sectionPlan.LengthBars / (double)phraseBars);
            IReadOnlyList<int> transformedDegrees = _motifEngine.TransformDegrees(motif, sectionPlan.MotifTreatment, sectionPlan.VariationIndex);
            IReadOnlyList<int> transformedRhythm = _motifEngine.TransformRhythm(motif, sectionPlan.MotifTreatment);
            MotifOccurrencePlan? motifOccurrence = project.AdvancedComposition.MotifOccurrences
                .FirstOrDefault(item => item.SectionId == sectionPlan.Id);
            double requiredContourSimilarity = motifOccurrence?.Required == true
                ? project.AdvancedComposition.MotifConstraints.MinimumContourSimilarity
                : 0.0;

            for (int phraseIndex = 0; phraseIndex < phraseCount; phraseIndex++)
            {
                int localStartBar = phraseIndex * phraseBars;
                int actualBars = Math.Min(phraseBars, sectionPlan.LengthBars - localStartBar);
                long phraseStartTick = localStartBar * timing.BarTicks;
                long phraseLengthTicks = actualBars * timing.BarTicks;
                List<MelodyCandidate> candidates = new();

                for (int candidateIndex = 0; candidateIndex < composition.CandidateCount; candidateIndex++)
                {
                    MelodyCandidate candidate = BuildMelodyCandidate(
                        project,
                        role,
                        sectionPlan,
                        phraseIndex,
                        candidateIndex,
                        phraseStartTick,
                        phraseLengthTicks,
                        transformedDegrees,
                        transformedRhythm,
                        scale,
                        timing);
                    PhraseCandidateScore candidateScore = _scorer.Score(
                        project.MasterSeed,
                        sectionPlan.Id,
                        role.Id,
                        phraseIndex,
                        candidateIndex,
                        candidate.Notes,
                        transformedDegrees,
                        project.Song.KeyRoot,
                        scale,
                        role.MinimumPitch,
                        role.MaximumPitch,
                        phraseLengthTicks,
                        timing.BeatTicks,
                        localTick => HarmonyAt(project, sectionPlan.StartBar + localStartBar + (int)(localTick / timing.BarTicks)),
                        activeStyle.EffectiveConstraints,
                        activeStyle.EffectiveScoring,
                        phraseIndex == 0 ? requiredContourSimilarity : 0.0);
                    candidates.Add(candidate with { Score = candidateScore });
                    composition.CandidateScores.Add(candidateScore);
                }

                MelodyCandidate selected = SelectCandidate(project, role, sectionPlan, phraseIndex, candidates);
                selected.Score.Selected = true;
                foreach (NoteEvent note in selected.Notes)
                {
                    note.StartTick += phraseStartTick;
                    clip.Notes.Add(note);
                }
            }
        }
    }

    private MelodyCandidate BuildMelodyCandidate(
        ProjectDocument project,
        CompositionRoleSettings role,
        FormSectionPlan section,
        int phraseIndex,
        int candidateIndex,
        long phraseStartTick,
        long phraseLengthTicks,
        IReadOnlyList<int> motifDegrees,
        IReadOnlyList<int> motifRhythm,
        ScaleDefinition scale,
        Timing timing)
    {
        DeterministicRandom random = Random(
            project.MasterSeed,
            "melody-candidate",
            role.Revision.ToString(CultureInfo.InvariantCulture),
            section.Id,
            phraseIndex.ToString(CultureInfo.InvariantCulture),
            candidateIndex.ToString(CultureInfo.InvariantCulture));
        List<NoteEvent> notes = new();
        long sixteenth = Math.Max(1, timing.BeatTicks / 4);
        IReadOnlyList<bool> rolePattern = RhythmPatternLibrary.Get(role.RhythmPatternId);
        long cursor = 0;
        int motifIndex = 0;
        int previousPitch = -1;
        int safety = 0;

        while (cursor < phraseLengthTicks && safety++ < 256)
        {
            int rhythmUnits = motifRhythm[motifIndex % motifRhythm.Count];
            long spacing = Math.Max(sixteenth, rhythmUnits * sixteenth);
            long absoluteWithinSection = phraseStartTick + cursor;
            int globalBar = section.StartBar + (int)(absoluteWithinSection / timing.BarTicks);
            HarmonicEventPlan harmony = HarmonyAt(project, globalBar);
            bool strongBeat = absoluteWithinSection % timing.BeatTicks == 0;
            int patternStep = (int)(cursor / sixteenth) % rolePattern.Count;
            bool patternAllowsOnset = rolePattern[patternStep];
            bool densityRest = candidateIndex > 0 && random.NextInt(100) > role.Density;
            bool patternRest = !patternAllowsOnset && random.Chance(candidateIndex == 0 ? 0.72 : 0.84);
            bool rest = densityRest || patternRest;
            if (!rest)
            {
                int degree = harmony.ScaleDegree + motifDegrees[motifIndex % motifDegrees.Count];
                if (candidateIndex > 0 && random.Chance(0.26))
                {
                    degree += random.Pick(new[] { -2, -1, 1, 2 });
                }

                if (strongBeat)
                {
                    degree = NearestDegree(degree, new[]
                    {
                        harmony.ScaleDegree,
                        harmony.ScaleDegree + 2,
                        harmony.ScaleDegree + 4,
                        harmony.ScaleDegree + 6
                    }, scale.Intervals.Count);
                }

                int pitch = scale.MidiPitchForDegree(project.Song.KeyRoot, degree, 4);
                pitch = FitPitch(pitch, role.MinimumPitch, role.MaximumPitch);
                if (previousPitch >= 0 && Math.Abs(pitch - previousPitch) > 12)
                {
                    pitch = FitNear(pitch, previousPitch, role.MinimumPitch, role.MaximumPitch);
                }

                long duration = Math.Min(
                    phraseLengthTicks - cursor,
                    Math.Max(1, (long)Math.Round(spacing * (strongBeat ? 0.88 : 0.76))));
                int velocity = Math.Clamp(65 + (int)Math.Round(section.Energy * 32) + (strongBeat ? 8 : 0) + random.NextInt(-5, 6), 35, 124);
                notes.Add(new NoteEvent
                {
                    Id = SeedDerivation.StableId(
                        "note",
                        project.MasterSeed,
                        "stage3",
                        role.Id,
                        role.Revision.ToString(CultureInfo.InvariantCulture),
                        section.Id,
                        phraseIndex.ToString(CultureInfo.InvariantCulture),
                        candidateIndex.ToString(CultureInfo.InvariantCulture),
                        motifIndex.ToString(CultureInfo.InvariantCulture)),
                    Pitch = pitch,
                    StartTick = cursor,
                    DurationTicks = duration,
                    Velocity = velocity
                });
                previousPitch = pitch;
            }

            cursor += spacing;
            motifIndex++;
        }

        RepairPhraseEnding(project, section, phraseIndex, candidateIndex, phraseStartTick, phraseLengthTicks, role, scale, notes, timing);
        return new MelodyCandidate(notes, new PhraseCandidateScore());
    }

    private static MelodyCandidate SelectCandidate(
        ProjectDocument project,
        CompositionRoleSettings role,
        FormSectionPlan section,
        int phraseIndex,
        IReadOnlyList<MelodyCandidate> candidates)
    {
        List<MelodyCandidate> valid = candidates
            .Where(candidate => candidate.Score.PassedHardConstraints)
            .OrderByDescending(candidate => candidate.Score.TotalScore)
            .ThenBy(candidate => candidate.Score.CandidateIndex)
            .ToList();
        if (valid.Count == 0)
        {
            MelodyCandidate fallback = candidates
                .OrderByDescending(candidate => candidate.Score.TotalScore)
                .ThenBy(candidate => candidate.Score.Diagnostics.Count)
                .ThenBy(candidate => candidate.Score.CandidateIndex)
                .First();
            fallback.Score.Diagnostics.Add(
                $"No candidate passed every style constraint for {section.Name} phrase {phraseIndex + 1}; the highest-scoring deterministic fallback was selected.");
            return fallback;
        }

        int band = Math.Min(3, valid.Count);
        DeterministicRandom selector = Random(
            project.MasterSeed,
            "candidate-selector",
            role.Revision.ToString(CultureInfo.InvariantCulture),
            section.Id,
            phraseIndex.ToString(CultureInfo.InvariantCulture));
        double roll = selector.NextDouble();
        int chosen = roll < 0.62 ? 0 : roll < 0.88 ? Math.Min(1, band - 1) : Math.Min(2, band - 1);
        return valid[chosen];
    }

    private void GenerateChords(ProjectDocument project, CompositionRoleSettings role, IReadOnlyDictionary<string, SongClip> clips)
    {
        ScaleDefinition scale = ScaleLibrary.Get(project.Song.Scale);
        Timing timing = GetTiming(project);
        int stepsPerBar = timing.BeatsPerBar * 4;
        long stepTicks = timing.BarTicks / stepsPerBar;
        IReadOnlyList<bool> pattern = RhythmPatternLibrary.Get(role.RhythmPatternId);
        IReadOnlyList<int>? previousVoicing = null;
        foreach (HarmonicEventPlan harmony in project.StructuredComposition.HarmonyPlan)
        {
            FormSectionPlan section = project.StructuredComposition.FormPlan.First(item => item.Id == harmony.SectionId);
            if (!IsRoleActive(project, role.Id, section.Id)) continue;
            SongClip clip = clips[section.Id];
            int localBar = harmony.StartBar - section.StartBar;
            ChordDefinition chord = ChordBuilder.BuildDiatonic(
                project.Song.KeyRoot,
                scale,
                harmony.ScaleDegree,
                3,
                includeSeventh: harmony.ChordToneCount >= 4);
            List<int> pitches;
            if (project.AdvancedComposition.Enabled && project.AdvancedComposition.Voicing.Enabled)
            {
                VoicingSolution solution = _voicingSolver.Solve(
                    ApplyInversion(chord.MidiPitches, harmony.Inversion + role.Revision),
                    previousVoicing,
                    role.MinimumPitch,
                    role.MaximumPitch,
                    project.AdvancedComposition.Voicing);
                pitches = solution.Pitches.ToList();
                previousVoicing = pitches;
                foreach (string diagnostic in solution.Diagnostics)
                {
                    project.AdvancedComposition.Diagnostics.Add($"Voicing bar {harmony.StartBar + 1}: {diagnostic}");
                }
            }
            else
            {
                pitches = ApplyInversion(chord.MidiPitches, harmony.Inversion + role.Revision)
                    .Select(pitch => FitPitch(pitch, role.MinimumPitch, role.MaximumPitch))
                    .Distinct()
                    .ToList();
                previousVoicing = pitches;
            }

            List<int> attacks = Enumerable.Range(0, stepsPerBar)
                .Where(step => pattern[step % pattern.Count])
                .ToList();
            if (attacks.Count == 0)
            {
                attacks.Add(0);
            }

            for (int attackIndex = 0; attackIndex < attacks.Count; attackIndex++)
            {
                int step = attacks[attackIndex];
                int nextStep = attackIndex + 1 < attacks.Count ? attacks[attackIndex + 1] : stepsPerBar;
                long start = (localBar * timing.BarTicks) + (step * stepTicks);
                long duration = Math.Max(1, ((nextStep - step) * stepTicks) - (timing.BeatTicks / 16));
                for (int voice = 0; voice < pitches.Count; voice++)
                {
                    clip.Notes.Add(NewRoleNote(project, role, section.Id, $"chord-{localBar}-{step}-{voice}", pitches[voice], start, duration,
                        Math.Clamp(52 + (int)(section.Energy * 27) - (voice * 2) + (step == 0 ? 5 : 0), 32, 112)));
                }
            }
        }
    }

    private void GenerateBass(ProjectDocument project, CompositionRoleSettings role, IReadOnlyDictionary<string, SongClip> clips)
    {
        ScaleDefinition scale = ScaleLibrary.Get(project.Song.Scale);
        Timing timing = GetTiming(project);
        int stepsPerBar = timing.BeatsPerBar * 4;
        long stepTicks = timing.BarTicks / stepsPerBar;
        IReadOnlyList<bool> pattern = RhythmPatternLibrary.Get(role.RhythmPatternId);
        int pulses = Math.Clamp((int)Math.Round(stepsPerBar * role.Density / 100.0), 1, stepsPerBar);
        DeterministicRandom random = Random(project.MasterSeed, "bass", role.Revision.ToString(CultureInfo.InvariantCulture));
        foreach (HarmonicEventPlan harmony in project.StructuredComposition.HarmonyPlan)
        {
            FormSectionPlan section = project.StructuredComposition.FormPlan.First(item => item.Id == harmony.SectionId);
            if (!IsRoleActive(project, role.Id, section.Id)) continue;
            SongClip clip = clips[section.Id];
            int localBar = harmony.StartBar - section.StartBar;
            IReadOnlyList<bool> densityPattern = EuclideanRhythm.Generate(pulses, stepsPerBar, section.VariationIndex + role.Revision);
            for (int step = 0; step < stepsPerBar; step++)
            {
                bool active = pattern[step % pattern.Count] && densityPattern[step];
                if (!active && step != 0)
                {
                    continue;
                }

                int beat = step / 4;
                int degree = step == 0
                    ? harmony.ScaleDegree
                    : beat == timing.BeatsPerBar - 1 && random.Chance(0.42)
                        ? harmony.ScaleDegree + 4
                        : random.Chance(0.28) ? harmony.ScaleDegree + 2 : harmony.ScaleDegree;
                int pitch = FitPitch(scale.MidiPitchForDegree(project.Song.KeyRoot, degree, 2), role.MinimumPitch, role.MaximumPitch);
                long start = (localBar * timing.BarTicks) + (step * stepTicks);
                long desiredDuration = Math.Max(1, (step % 4 == 0 ? timing.BeatTicks : stepTicks * 2) - (timing.BeatTicks / 12));
                long duration = Math.Max(1, Math.Min(desiredDuration, timing.BarTicks - (step * stepTicks)));
                clip.Notes.Add(NewRoleNote(project, role, section.Id, $"bass-{localBar}-{step}", pitch, start, duration,
                    Math.Clamp(70 + (step == 0 ? 13 : 0) + (int)(section.Energy * 17), 42, 122)));
            }
        }
    }

    private void GenerateArpeggio(ProjectDocument project, CompositionRoleSettings role, IReadOnlyDictionary<string, SongClip> clips)
    {
        ScaleDefinition scale = ScaleLibrary.Get(project.Song.Scale);
        Timing timing = GetTiming(project);
        int stepsPerBar = timing.BeatsPerBar * 4;
        int pulses = Math.Clamp((int)Math.Round(stepsPerBar * role.Density / 100.0), 1, stepsPerBar);
        long stepTicks = timing.BarTicks / stepsPerBar;
        IReadOnlyList<bool> pattern = RhythmPatternLibrary.Get(role.RhythmPatternId);
        foreach (HarmonicEventPlan harmony in project.StructuredComposition.HarmonyPlan)
        {
            FormSectionPlan section = project.StructuredComposition.FormPlan.First(item => item.Id == harmony.SectionId);
            if (!IsRoleActive(project, role.Id, section.Id)) continue;
            SongClip clip = clips[section.Id];
            int localBar = harmony.StartBar - section.StartBar;
            IReadOnlyList<bool> rhythm = EuclideanRhythm.Generate(pulses, stepsPerBar, section.VariationIndex + role.Revision);
            ChordDefinition chord = ChordBuilder.BuildDiatonic(project.Song.KeyRoot, scale, harmony.ScaleDegree, 3, harmony.ChordToneCount >= 4);
            List<int> tones = ApplyInversion(chord.MidiPitches, harmony.Inversion + role.Revision)
                .Select(pitch => FitPitch(pitch + 12, role.MinimumPitch, role.MaximumPitch))
                .ToList();
            int toneIndex = 0;
            bool emitted = false;
            for (int step = 0; step < stepsPerBar; step++)
            {
                if (!rhythm[step] || !pattern[step % pattern.Count])
                {
                    continue;
                }
                int pitch = tones[toneIndex++ % tones.Count];
                long start = (localBar * timing.BarTicks) + (step * stepTicks);
                long duration = Math.Max(1, (long)Math.Round(stepTicks * 0.72));
                clip.Notes.Add(NewRoleNote(project, role, section.Id, $"arp-{localBar}-{step}", pitch, start, duration,
                    Math.Clamp(50 + (int)(section.Energy * 34) + (step % 4 == 0 ? 7 : 0), 30, 118)));
                emitted = true;
            }
            if (!emitted)
            {
                clip.Notes.Add(NewRoleNote(project, role, section.Id, $"arp-{localBar}-fallback", tones[0],
                    localBar * timing.BarTicks, Math.Max(1, stepTicks), 64));
            }
        }
    }

    private void GenerateDrums(ProjectDocument project, CompositionRoleSettings role, IReadOnlyDictionary<string, SongClip> clips)
    {
        Timing timing = GetTiming(project);
        int stepsPerBar = timing.BeatsPerBar * 4;
        long stepTicks = timing.BarTicks / stepsPerBar;
        IReadOnlyList<bool> kickPattern = RhythmPatternLibrary.Get(role.RhythmPatternId);
        foreach (FormSectionPlan section in project.StructuredComposition.FormPlan)
        {
            if (!IsRoleActive(project, role.Id, section.Id)) continue;
            SongClip clip = clips[section.Id];
            int hatPulses = Math.Clamp((int)Math.Round(stepsPerBar * role.Density / 100.0), timing.BeatsPerBar, stepsPerBar);
            IReadOnlyList<bool> hats = EuclideanRhythm.Generate(hatPulses, stepsPerBar, section.VariationIndex + role.Revision);
            for (int localBar = 0; localBar < section.LengthBars; localBar++)
            {
                bool fillBar = localBar == section.LengthBars - 1 && section.Kind is not FormSectionKind.Intro;
                for (int step = 0; step < stepsPerBar; step++)
                {
                    long start = (localBar * timing.BarTicks) + (step * stepTicks);
                    if (hats[step])
                    {
                        int hat = fillBar && step >= stepsPerBar - 4 ? 46 : 42;
                        clip.Notes.Add(NewRoleNote(project, role, section.Id, $"hat-{localBar}-{step}", hat, start,
                            Math.Max(1, stepTicks / 2), 52 + (step % 4 == 0 ? 15 : 0)));
                    }

                    bool beatStart = step % 4 == 0;
                    int beat = step / 4;
                    bool patternedKick = kickPattern[step % kickPattern.Count];
                    if (patternedKick || (beatStart && beat == 0))
                    {
                        clip.Notes.Add(NewRoleNote(project, role, section.Id, $"kick-{localBar}-{step}", 36, start,
                            Math.Max(1, stepTicks), 86 + (int)(section.Energy * 23) + (beatStart ? 4 : 0)));
                    }

                    bool snareBeat = beatStart && (timing.BeatsPerBar == 3 ? beat == 1 : beat is 1 or 3);
                    if (snareBeat)
                    {
                        clip.Notes.Add(NewRoleNote(project, role, section.Id, $"snare-{localBar}-{step}", 38, start,
                            Math.Max(1, stepTicks), 82 + (int)(section.Energy * 22)));
                    }

                    if (fillBar && step >= stepsPerBar - 4)
                    {
                        int tom = 45 + ((step - (stepsPerBar - 4)) % 3) * 2;
                        clip.Notes.Add(NewRoleNote(project, role, section.Id, $"fill-{localBar}-{step}", tom, start,
                            Math.Max(1, stepTicks), 74 + ((step - stepsPerBar + 4) * 6)));
                    }
                }
            }
        }
    }

    private static void ApplyPrimaryInstrumentPlan(ProjectDocument project, CompositionRoleSettings role, SongTrack track)
    {
        SectionOrchestrationPlan? firstSection = project.Orchestration.SectionPlans
            .OrderBy(item => project.StructuredComposition.FormPlan.FirstOrDefault(section => section.Id == item.SectionId)?.StartBar ?? int.MaxValue)
            .FirstOrDefault();
        InstrumentAssignmentPlan? firstAssignment = firstSection?.Assignments.FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == 0);
        if (firstAssignment is not null)
        {
            track.MidiChannel = role.IsPercussion ? 9 : firstAssignment.MidiChannel;
            track.MidiBank = firstAssignment.Bank;
            track.GeneralMidiProgram = firstAssignment.Program;
            track.PresetKey = firstAssignment.PresetKey;
            track.SoundFontId = firstAssignment.SoundFontId;
            track.Volume = firstAssignment.Volume;
            track.Pan = firstAssignment.Pan;
        }

        foreach (SongClip clip in track.Clips)
        {
            SongSection? section = project.Song.Sections.FirstOrDefault(item => item.StartTick == clip.StartTick);
            if (section is null) continue;
            InstrumentAssignmentPlan? assignment = project.Orchestration.SectionPlans
                .FirstOrDefault(item => item.SectionId == section.Id)?.Assignments
                .FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == 0 && item.Active);
            if (assignment is null) continue;
            clip.ProgramChanges.Add(new ProgramChangeEvent
            {
                Id = SeedDerivation.StableId("program", project.MasterSeed, role.Id, section.Id, "primary"),
                Bank = assignment.Bank,
                Program = assignment.Program,
                StartTick = 0,
                PresetKey = assignment.PresetKey,
                PresetName = assignment.PresetName
            });
        }
    }

    private void BuildLayerTracks(ProjectDocument project)
    {
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => item.Enabled).ToArray())
        {
            BuildLayerTracksForRole(project, role);
        }
    }

    private void BuildLayerTracksForRole(ProjectDocument project, CompositionRoleSettings role)
    {
        SongTrack? primary = project.Song.Tracks.FirstOrDefault(item => item.SourceVoiceId == role.Id);
        if (primary is null) return;
        project.Song.Tracks.RemoveAll(item => item.SourceVoiceId.StartsWith(role.Id + "#layer", StringComparison.Ordinal));
        int maximumLayer = project.Orchestration.SectionPlans.SelectMany(item => item.Assignments)
            .Where(item => item.RoleId == role.Id && item.Active)
            .Select(item => item.LayerIndex)
            .DefaultIfEmpty(0)
            .Max();
        if (maximumLayer <= 0) return;

        Dictionary<string, List<NoteEvent>> originalNotes = new(StringComparer.Ordinal);
        foreach (SongClip clip in primary.Clips)
        {
            originalNotes[clip.Id] = clip.Notes.Select(CloneNote).ToList();
        }

        for (int layer = 1; layer <= maximumLayer; layer++)
        {
            InstrumentAssignmentPlan? first = project.Orchestration.SectionPlans
                .SelectMany(item => item.Assignments)
                .FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == layer && item.Active);
            if (first is null) continue;
            SongTrack layerTrack = new()
            {
                Id = SeedDerivation.StableId("track-layer", project.MasterSeed, role.Id, layer.ToString(CultureInfo.InvariantCulture)),
                Name = $"{role.Name} Layer {layer + 1}",
                Role = role.Role,
                MidiChannel = role.IsPercussion ? 9 : first.MidiChannel,
                GeneralMidiProgram = first.Program,
                MidiBank = first.Bank,
                PresetKey = first.PresetKey,
                SoundFontId = first.SoundFontId,
                IsPercussion = role.IsPercussion,
                Volume = first.Volume,
                Pan = first.Pan,
                SourceVoiceId = role.Id + "#layer" + layer,
                GeneratorId = "PlayromaMusic.OrchestrationLayer",
                GenerationRevision = role.Revision
            };
            foreach (SongSection section in project.Song.Sections)
            {
                SongClip? sourceClip = primary.Clips.FirstOrDefault(item => item.StartTick == section.StartTick);
                if (sourceClip is null) continue;
                SongClip layerClip = new()
                {
                    Id = SeedDerivation.StableId("clip-layer", project.MasterSeed, role.Id, layer.ToString(CultureInfo.InvariantCulture), section.Id),
                    Name = $"{role.Name} layer {layer + 1} - {section.Name}",
                    StartTick = sourceClip.StartTick,
                    LengthTicks = sourceClip.LengthTicks
                };
                InstrumentAssignmentPlan? assignment = project.Orchestration.SectionPlans
                    .FirstOrDefault(item => item.SectionId == section.Id)?.Assignments
                    .FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == layer && item.Active);
                if (assignment is not null)
                {
                    layerClip.ProgramChanges.Add(new ProgramChangeEvent
                    {
                        Id = SeedDerivation.StableId("program-layer", project.MasterSeed, role.Id, layer.ToString(CultureInfo.InvariantCulture), section.Id),
                        Bank = assignment.Bank,
                        Program = assignment.Program,
                        StartTick = 0,
                        PresetKey = assignment.PresetKey,
                        PresetName = assignment.PresetName
                    });
                    IReadOnlyList<NoteEvent> source = originalNotes[sourceClip.Id];
                    IEnumerable<NoteEvent> selected = SelectLayerNotes(source, assignment.Strategy, layer, project, section, role);
                    int noteIndex = 0;
                    foreach (NoteEvent note in selected)
                    {
                        NoteEvent clone = CloneNote(note);
                        clone.Id = SeedDerivation.StableId("layer-note", project.MasterSeed, role.Id, layer.ToString(CultureInfo.InvariantCulture), section.Id, noteIndex++.ToString(CultureInfo.InvariantCulture));
                        clone.Pitch = Math.Clamp(clone.Pitch + assignment.OctaveOffset * 12, 0, 127);
                        clone.Velocity = Math.Clamp((int)Math.Round(clone.Velocity * assignment.Volume), 1, 127);
                        layerClip.Notes.Add(clone);
                    }
                }
                layerTrack.Clips.Add(layerClip);
            }
            if (layerTrack.Clips.Any(item => item.Notes.Count > 0)) project.Song.Tracks.Add(layerTrack);
        }

        ApplyPrimaryDistribution(project, role, primary, originalNotes);
    }

    private static IEnumerable<NoteEvent> SelectLayerNotes(
        IReadOnlyList<NoteEvent> notes,
        RoleDistributionStrategy strategy,
        int layer,
        ProjectDocument project,
        SongSection section,
        CompositionRoleSettings role)
    {
        int midpoint = (role.MinimumPitch + role.MaximumPitch) / 2;
        long phraseTicks = Math.Max(1, project.StructuredComposition.BarsPerPhrase * section.LengthTicks /
            Math.Max(1, project.StructuredComposition.FormPlan.FirstOrDefault(item => item.Id == section.Id)?.LengthBars ?? 1));
        return strategy switch
        {
            RoleDistributionStrategy.RegisterSplit => notes.Where(item => layer % 2 == 1 ? item.Pitch >= midpoint : item.Pitch < midpoint),
            RoleDistributionStrategy.Hocket => notes.Where((_, index) => index % (layer + 1) == layer % (layer + 1)),
            RoleDistributionStrategy.PhraseAlternation or RoleDistributionStrategy.CallAndResponse => notes.Where(item => (item.StartTick / phraseTicks) % 2 == layer % 2),
            _ => notes
        };
    }

    private static void ApplyPrimaryDistribution(
        ProjectDocument project,
        CompositionRoleSettings role,
        SongTrack primary,
        IReadOnlyDictionary<string, List<NoteEvent>> originalNotes)
    {
        int midpoint = (role.MinimumPitch + role.MaximumPitch) / 2;
        foreach (SongClip clip in primary.Clips)
        {
            SongSection? section = project.Song.Sections.FirstOrDefault(item => item.StartTick == clip.StartTick);
            if (section is null) continue;
            InstrumentAssignmentPlan? secondary = project.Orchestration.SectionPlans
                .FirstOrDefault(item => item.SectionId == section.Id)?.Assignments
                .FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == 1 && item.Active);
            if (secondary is null) continue;
            long phraseTicks = Math.Max(1, project.StructuredComposition.BarsPerPhrase * section.LengthTicks /
                Math.Max(1, project.StructuredComposition.FormPlan.FirstOrDefault(item => item.Id == section.Id)?.LengthBars ?? 1));
            IEnumerable<NoteEvent> source = originalNotes[clip.Id];
            clip.Notes = secondary.Strategy switch
            {
                RoleDistributionStrategy.RegisterSplit => source.Where(item => item.Pitch < midpoint).Select(CloneNote).ToList(),
                RoleDistributionStrategy.Hocket => source.Where((_, index) => index % 2 == 0).Select(CloneNote).ToList(),
                RoleDistributionStrategy.PhraseAlternation or RoleDistributionStrategy.CallAndResponse => source.Where(item => (item.StartTick / phraseTicks) % 2 == 0).Select(CloneNote).ToList(),
                _ => source.Select(CloneNote).ToList()
            };
        }
    }

    private static NoteEvent CloneNote(NoteEvent source) => new()
    {
        Id = source.Id,
        Pitch = source.Pitch,
        StartTick = source.StartTick,
        DurationTicks = source.DurationTicks,
        Velocity = source.Velocity
    };

    private void ApplyAdvancedPlans(ProjectDocument project)
    {
        StructuredCompositionDocument composition = project.StructuredComposition;
        AdvancedCompositionDocument advanced = project.AdvancedComposition ??= new AdvancedCompositionDocument();
        advanced.Diagnostics.Clear();
        advanced.TensionCurve = advanced.Enabled && advanced.Tension.Enabled
            ? _tensionPlanner.Apply(composition.FormPlan, composition.HarmonyPlan, advanced.Tension).ToList()
            : new List<TensionPoint>();
        advanced.MotifOccurrences = advanced.Enabled && advanced.MotifConstraints.Enabled
            ? _motifPlanner.Apply(composition.FormPlan, composition.Motifs[0], advanced.MotifConstraints).ToList()
            : new List<MotifOccurrencePlan>();
        advanced.ArrangementPlan = advanced.Enabled && advanced.Arrangement.Enabled
            ? _arrangementOptimizer.Optimize(project.MasterSeed, composition.FormPlan, composition.Roles, advanced.Arrangement).ToList()
            : BuildFullArrangement(composition.FormPlan, composition.Roles);
        if (project.Orchestration?.Enabled == true)
        {
            StructuredStyleDefinition style = ResolveStyle(composition);
            _orchestrationPlanner.Plan(project, style.Family);
        }
    }

    private bool TryGenerateWithPlugin(
        ProjectDocument project,
        CompositionRoleSettings role,
        IReadOnlyDictionary<string, SongClip> clips,
        SongTrack track)
    {
        if (!project.AdvancedComposition.Enabled || !project.AdvancedComposition.Extensions.EnableExternalPlugins) return false;
        string requestedId = role.GeneratorId;
        if (string.IsNullOrWhiteSpace(requestedId) && project.AdvancedComposition.Neural.Enabled && role.Role == TrackRole.Lead && !string.IsNullOrWhiteSpace(project.AdvancedComposition.Neural.ModelId))
        {
            requestedId = "neural:" + project.AdvancedComposition.Neural.ModelId;
        }
        if (string.IsNullOrWhiteSpace(requestedId)) return false;
        if (project.AdvancedComposition.Extensions.EnabledPluginIds.Count > 0 &&
            !project.AdvancedComposition.Extensions.EnabledPluginIds.Contains(requestedId, StringComparer.OrdinalIgnoreCase))
        {
            project.AdvancedComposition.Diagnostics.Add($"Generator '{requestedId}' is not enabled for this project.");
            return false;
        }

        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();
        GeneratorRequest request = new()
        {
            GeneratorId = requestedId,
            StyleId = project.StructuredComposition.ActiveStyleId,
            Role = role.Role,
            RoleId = role.Id,
            IsPercussion = role.IsPercussion
        };
        GeneratorContext context = new()
        {
            Project = project,
            Role = role,
            Sections = project.StructuredComposition.FormPlan,
            Harmony = project.StructuredComposition.HarmonyPlan,
            Motifs = project.StructuredComposition.Motifs,
            Seed = SeedDerivation.Derive(project.MasterSeed, "stage6", "plugin", requestedId, role.Id, role.Revision.ToString(CultureInfo.InvariantCulture)),
            TicksPerQuarterNote = project.Song.TicksPerQuarterNote,
            BeatsPerBar = meter.Numerator,
            BeatUnit = meter.Denominator,
            NeuralTemperature = project.AdvancedComposition.Neural.Temperature,
            NeuralMaximumTokens = project.AdvancedComposition.Neural.MaximumTokens,
            CancellationToken = CancellationToken.None
        };
        if (!GeneratorPluginHost.Default.TryGenerate(request, context, out GeneratorResult result, out string pluginId))
        {
            if (!project.AdvancedComposition.Neural.FallBackToBuiltInGenerator && requestedId.StartsWith("neural:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Neural generator '{requestedId}' was requested but no compatible plugin was available.");
            }
            return false;
        }

        track.GeneratorId = pluginId;
        int noteIndex = 0;
        foreach (GeneratedNoteData generated in result.Notes)
        {
            if (!clips.TryGetValue(generated.SectionId, out SongClip? clip)) continue;
            if (!IsRoleActive(project, role.Id, generated.SectionId)) continue;
            clip.Notes.Add(new NoteEvent
            {
                Id = SeedDerivation.StableId("plugin-note", project.MasterSeed, pluginId, role.Id, generated.SectionId, noteIndex++.ToString(CultureInfo.InvariantCulture)),
                Pitch = Math.Clamp(generated.Pitch, 0, 127),
                StartTick = Math.Clamp(generated.StartTick, 0, Math.Max(0, clip.LengthTicks - 1)),
                DurationTicks = Math.Max(1, Math.Min(generated.DurationTicks, clip.LengthTicks - Math.Clamp(generated.StartTick, 0, Math.Max(0, clip.LengthTicks - 1)))),
                Velocity = Math.Clamp(generated.Velocity, 1, 127)
            });
        }
        int controlIndex = 0;
        foreach (GeneratedControlChangeData generated in result.ControlChanges)
        {
            if (!clips.TryGetValue(generated.SectionId, out SongClip? clip)) continue;
            if (!IsRoleActive(project, role.Id, generated.SectionId)) continue;
            clip.ControlChanges.Add(new ControlChangeEvent
            {
                Id = SeedDerivation.StableId("plugin-cc", project.MasterSeed, pluginId, role.Id, generated.SectionId, controlIndex++.ToString(CultureInfo.InvariantCulture)),
                StartTick = Math.Clamp(generated.StartTick, 0, Math.Max(0, clip.LengthTicks - 1)),
                ControllerNumber = Math.Clamp(generated.ControllerNumber, 0, 127),
                Value = Math.Clamp(generated.Value, 0, 127)
            });
        }
        foreach (string diagnostic in result.Diagnostics) project.AdvancedComposition.Diagnostics.Add($"{pluginId}: {diagnostic}");
        return true;
    }

    private static void ApplyArrangementIntensity(ProjectDocument project, CompositionRoleSettings role, SongTrack track)
    {
        foreach (SongClip clip in track.Clips)
        {
            SongSection? songSection = project.Song.Sections.FirstOrDefault(section => section.StartTick == clip.StartTick);
            if (songSection is null) continue;
            ArrangementSectionPlan? plan = project.AdvancedComposition.ArrangementPlan
                .FirstOrDefault(section => section.SectionId == songSection.Id);
            ArrangementRoleState? roleState = plan?.Roles.FirstOrDefault(item => item.RoleId == role.Id);
            if (roleState is null) continue;
            double scale = Math.Clamp(0.45 + roleState.Intensity / 100.0 * 0.70, 0.45, 1.15);
            foreach (NoteEvent note in clip.Notes)
            {
                note.Velocity = Math.Clamp((int)Math.Round(note.Velocity * scale), 1, 127);
            }
        }
    }

    private static bool IsRoleActive(ProjectDocument project, string roleId, string sectionId)
    {
        ArrangementSectionPlan? section = project.AdvancedComposition.ArrangementPlan.FirstOrDefault(item => item.SectionId == sectionId);
        ArrangementRoleState? role = section?.Roles.FirstOrDefault(item => item.RoleId == roleId);
        return role?.Active ?? true;
    }

    private static List<ArrangementSectionPlan> BuildFullArrangement(
        IReadOnlyList<FormSectionPlan> sections,
        IReadOnlyList<CompositionRoleSettings> roles) =>
        sections.Select(section => new ArrangementSectionPlan
        {
            SectionId = section.Id,
            TargetEnergy = section.Energy,
            Fitness = 0,
            Roles = roles.Select(role => new ArrangementRoleState
            {
                RoleId = role.Id,
                Role = role.Role,
                Active = role.Enabled,
                Intensity = Math.Clamp((int)Math.Round(section.Energy * 100), 1, 100)
            }).ToList()
        }).ToList();

    private static void RepairPhraseEnding(
        ProjectDocument project,
        FormSectionPlan section,
        int phraseIndex,
        int candidateIndex,
        long phraseStartTick,
        long phraseLengthTicks,
        CompositionRoleSettings role,
        ScaleDefinition scale,
        List<NoteEvent> notes,
        Timing timing)
    {
        int finalGlobalBar = section.StartBar + (int)((phraseStartTick + Math.Max(0, phraseLengthTicks - 1)) / timing.BarTicks);
        HarmonicEventPlan finalHarmony = HarmonyAt(project, finalGlobalBar);
        int targetPitch = FitPitch(
            scale.MidiPitchForDegree(project.Song.KeyRoot, finalHarmony.ScaleDegree, 4),
            role.MinimumPitch,
            role.MaximumPitch);
        long finalStart = Math.Max(0, phraseLengthTicks - timing.BeatTicks);
        if (notes.Count == 0)
        {
            notes.Add(new NoteEvent
            {
                Id = SeedDerivation.StableId("repair-note", project.MasterSeed, role.Id, section.Id, phraseIndex.ToString(), candidateIndex.ToString(), "1"),
                Pitch = targetPitch,
                StartTick = 0,
                DurationTicks = Math.Max(1, Math.Min(timing.BeatTicks, phraseLengthTicks)),
                Velocity = 72
            });
        }

        int referencePitch = notes.Count > 1 && notes[^1].StartTick >= finalStart ? notes[^2].Pitch : notes[^1].Pitch;
        int finalPitch = FitNear(targetPitch, referencePitch, role.MinimumPitch, role.MaximumPitch);
        if (notes.Count == 1 || notes[^1].StartTick < finalStart)
        {
            notes.Add(new NoteEvent
            {
                Id = SeedDerivation.StableId("repair-note", project.MasterSeed, role.Id, section.Id, phraseIndex.ToString(), candidateIndex.ToString(), "2"),
                Pitch = finalPitch,
                StartTick = finalStart,
                DurationTicks = Math.Max(1, phraseLengthTicks - finalStart),
                Velocity = 78
            });
        }
        else
        {
            NoteEvent last = notes[^1];
            last.Pitch = finalPitch;
            last.DurationTicks = Math.Max(1, Math.Min(last.DurationTicks, phraseLengthTicks - last.StartTick));
        }
    }

    private static NoteEvent NewRoleNote(
        ProjectDocument project,
        CompositionRoleSettings role,
        string sectionId,
        string localId,
        int pitch,
        long start,
        long duration,
        int velocity) =>
        new()
        {
            Id = SeedDerivation.StableId(
                "note",
                project.MasterSeed,
                "stage3",
                role.Id,
                role.Revision.ToString(CultureInfo.InvariantCulture),
                sectionId,
                localId),
            Pitch = Math.Clamp(pitch, 0, 127),
            StartTick = Math.Max(0, start),
            DurationTicks = Math.Max(1, duration),
            Velocity = Math.Clamp(velocity, 1, 127)
        };

    private static HarmonicEventPlan HarmonyAt(ProjectDocument project, int globalBar)
    {
        HarmonicEventPlan? exact = project.StructuredComposition.HarmonyPlan
            .LastOrDefault(harmony => harmony.StartBar <= globalBar && globalBar < harmony.StartBar + harmony.LengthBars);
        return exact ?? project.StructuredComposition.HarmonyPlan[^1];
    }

    private static IReadOnlyList<int> ApplyInversion(IReadOnlyList<int> pitches, int inversion)
    {
        List<int> result = pitches.OrderBy(pitch => pitch).ToList();
        int rotations = result.Count == 0 ? 0 : ((inversion % result.Count) + result.Count) % result.Count;
        for (int index = 0; index < rotations; index++)
        {
            int first = result[0] + 12;
            result.RemoveAt(0);
            result.Add(first);
        }
        return result;
    }

    private static int NearestDegree(int degree, IReadOnlyList<int> candidates, int scaleCount) =>
        candidates
            .SelectMany(candidate => new[] { candidate - scaleCount, candidate, candidate + scaleCount })
            .OrderBy(candidate => Math.Abs(candidate - degree))
            .ThenBy(candidate => candidate)
            .First();

    private static int FitPitch(int pitch, int minimum, int maximum)
    {
        while (pitch < minimum && pitch + 12 <= maximum)
        {
            pitch += 12;
        }
        while (pitch > maximum && pitch - 12 >= minimum)
        {
            pitch -= 12;
        }
        return Math.Clamp(pitch, minimum, maximum);
    }

    private static int FitNear(int pitch, int previous, int minimum, int maximum)
    {
        int best = FitPitch(pitch, minimum, maximum);
        for (int octave = -4; octave <= 4; octave++)
        {
            int candidate = pitch + (octave * 12);
            if (candidate >= minimum && candidate <= maximum && Math.Abs(candidate - previous) < Math.Abs(best - previous))
            {
                best = candidate;
            }
        }
        return best;
    }

    private static Timing GetTiming(ProjectDocument project)
    {
        MeterChange meter = project.Song.MeterMap[0];
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / meter.Denominator;
        return new Timing(beatTicks, beatTicks * meter.Numerator, meter.Numerator);
    }

    private static void OrderTracks(ProjectDocument project)
    {
        Dictionary<string, int> order = project.StructuredComposition.Roles
            .Select((role, index) => new { role.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        project.Song.Tracks = project.Song.Tracks
            .OrderBy(track => order.GetValueOrDefault(track.SourceVoiceId, int.MaxValue))
            .ToList();
    }

    private static string BuildTitle(ulong masterSeed, StructuredStyleDefinition style, PitchClass root, ScaleKind scale)
    {
        DeterministicRandom random = Random(masterSeed, "title", style.Id);
        return $"{TitleAdjectives[random.NextInt(TitleAdjectives.Length)]} {TitleNouns[random.NextInt(TitleNouns.Length)]} in {root.ToDisplayName()} {ScaleLibrary.Get(scale).Name}";
    }

    private static DeterministicRandom Random(ulong seed, params string[] path) =>
        new(SeedDerivation.Derive(seed, new[] { "stage3" }.Concat(path).ToArray()));

    private static StructuredStyleDefinition ResolveStyle(StructuredCompositionDocument composition)
    {
        if (composition.HybridStyle.Enabled)
        {
            return StructuredStyleLibrary.ComposeHybrid(NormalizeBlend(composition.HybridStyle));
        }
        return StructuredStyleLibrary.Get(composition.ActiveStyleId);
    }

    private static StyleBlendSettings NormalizeBlend(StyleBlendSettings settings)
    {
        StyleBlendSettings normalized = settings.Clone();
        normalized.SecondaryWeight = Math.Clamp(normalized.SecondaryWeight, 0, 100);
        if (string.IsNullOrWhiteSpace(normalized.PrimaryPackId)) normalized.PrimaryPackId = DefaultStyleId;
        if (string.IsNullOrWhiteSpace(normalized.SecondaryPackId) ||
            string.Equals(normalized.PrimaryPackId, normalized.SecondaryPackId, StringComparison.OrdinalIgnoreCase))
        {
            normalized.SecondaryPackId = StructuredStyleLibrary.StylePacks.ConcretePacks
                .Select(pack => pack.Id)
                .FirstOrDefault(id => !string.Equals(id, normalized.PrimaryPackId, StringComparison.OrdinalIgnoreCase))
                ?? normalized.PrimaryPackId;
        }
        return normalized;
    }

    private static void EnsureStructured(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.StructuredComposition.Enabled)
        {
            throw new InvalidOperationException("The structured composition workspace is not enabled for this project.");
        }
    }

    private static void Validate(ProjectDocument project)
    {
        ProjectValidationResult validation = ProjectValidator.Validate(project);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Generated project failed validation: " + string.Join("; ", validation.Errors));
        }
    }

    private readonly record struct Timing(long BeatTicks, long BarTicks, int BeatsPerBar);
    private sealed record MelodyCandidate(List<NoteEvent> Notes, PhraseCandidateScore Score);
}
