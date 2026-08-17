using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Audio;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;

namespace PlayromaMusic.Core.Validation;

public sealed class ProjectValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

public static class ProjectValidator
{
    private static readonly HashSet<string> SourceNodeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "source.primary",
        "source.secondary"
    };

    public static ProjectValidationResult Validate(ProjectDocument? project)
    {
        ProjectValidationResult result = new();
        if (project is null)
        {
            result.Errors.Add("Project is null.");
            return result;
        }

        if (project.SchemaVersion != BuildInfo.ProjectSchemaVersion)
        {
            result.Errors.Add($"Unsupported schema version {project.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(project.ProjectId))
        {
            result.Errors.Add("Project ID is empty.");
        }

        SongDocument song = project.Song;
        if (song.TicksPerQuarterNote is < 24 or > 32767)
        {
            result.Errors.Add("Ticks per quarter note is outside the supported range.");
        }

        if (song.TempoMap.Count == 0 || song.TempoMap[0].Tick != 0)
        {
            result.Errors.Add("Tempo map must begin at tick zero.");
        }

        foreach (TempoChange tempo in song.TempoMap)
        {
            if (tempo.Tick < 0 || tempo.BeatsPerMinute is < 1 or > 1000)
            {
                result.Errors.Add($"Invalid tempo change at tick {tempo.Tick}.");
            }
        }

        if (song.MeterMap.Count == 0 || song.MeterMap[0].Tick != 0)
        {
            result.Errors.Add("Meter map must begin at tick zero.");
        }

        foreach (MeterChange meter in song.MeterMap)
        {
            if (meter.Tick < 0 || meter.Numerator <= 0 || meter.Denominator <= 0 ||
                (meter.Denominator & (meter.Denominator - 1)) != 0)
            {
                result.Errors.Add($"Invalid meter change at tick {meter.Tick}.");
            }
        }

        ValidateUniqueIds(song, result);
        ValidateSections(song, result);
        ValidateTracks(song, result);
        ValidateAudio(project.Audio, result);
        ValidateGenerationLab(project, result);
        ValidateStructuredComposition(project, result);
        ValidateAdvancedComposition(project, result);
        ValidateOrchestration(project, result);
        ValidateReferenceAnalysis(project, result);
        return result;
    }

    private static void ValidateUniqueIds(SongDocument song, ProjectValidationResult result)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (SongSection section in song.Sections)
        {
            AddId(section.Id, "section", ids, result);
        }

        foreach (SongTrack track in song.Tracks)
        {
            AddId(track.Id, "track", ids, result);
            foreach (SongClip clip in track.Clips)
            {
                AddId(clip.Id, "clip", ids, result);
                foreach (NoteEvent note in clip.Notes)
                {
                    AddId(note.Id, "note", ids, result);
                }

                foreach (ControlChangeEvent controlChange in clip.ControlChanges)
                {
                    AddId(controlChange.Id, "control change", ids, result);
                }

                foreach (ProgramChangeEvent programChange in clip.ProgramChanges)
                {
                    AddId(programChange.Id, "program change", ids, result);
                }
            }
        }
    }

    private static void AddId(string id, string kind, HashSet<string> ids, ProjectValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            result.Errors.Add($"A {kind} has an empty ID.");
        }
        else if (!ids.Add(id))
        {
            result.Errors.Add($"Duplicate ID: {id}.");
        }
    }

    private static void ValidateSections(SongDocument song, ProjectValidationResult result)
    {
        long previousEnd = 0;
        foreach (SongSection section in song.Sections.OrderBy(item => item.StartTick))
        {
            if (section.StartTick < 0 || section.LengthTicks <= 0)
            {
                result.Errors.Add($"Section {section.Name} has an invalid range.");
            }

            if (section.StartTick < previousEnd)
            {
                result.Errors.Add($"Section {section.Name} overlaps the preceding section.");
            }

            previousEnd = Math.Max(previousEnd, section.EndTick);
        }
    }

    private static void ValidateTracks(SongDocument song, ProjectValidationResult result)
    {
        HashSet<int> melodicChannels = new();
        foreach (SongTrack track in song.Tracks)
        {
            if (track.MidiChannel is < 0 or > 15)
            {
                result.Errors.Add($"Track {track.Name} has invalid MIDI channel {track.MidiChannel}.");
            }

            if (track.GeneralMidiProgram is < 0 or > 127 || track.MidiBank is < 0 or > 16383)
            {
                result.Errors.Add($"Track {track.Name} has invalid MIDI bank/program {track.MidiBank}:{track.GeneralMidiProgram}.");
            }

            if (!double.IsFinite(track.Volume) || track.Volume is < 0 or > 1.5 ||
                !double.IsFinite(track.Pan) || track.Pan is < -1 or > 1)
            {
                result.Errors.Add($"Track {track.Name} has invalid mixer settings.");
            }

            if (track.IsPercussion && track.MidiChannel != 9)
            {
                result.Warnings.Add($"Percussion track {track.Name} is not on zero-based MIDI channel 9.");
            }
            else if (!track.IsPercussion && !melodicChannels.Add(track.MidiChannel))
            {
                result.Warnings.Add($"Melodic MIDI channel {track.MidiChannel} is shared.");
            }

            foreach (SongClip clip in track.Clips)
            {
                if (clip.StartTick < 0 || clip.LengthTicks <= 0)
                {
                    result.Errors.Add($"Clip {clip.Name} has an invalid range.");
                }

                foreach (NoteEvent note in clip.Notes)
                {
                    if (note.Pitch is < 0 or > 127)
                    {
                        result.Errors.Add($"Note {note.Id} has invalid pitch {note.Pitch}.");
                    }

                    if (note.Velocity is < 1 or > 127)
                    {
                        result.Errors.Add($"Note {note.Id} has invalid velocity {note.Velocity}.");
                    }

                    if (note.StartTick < 0 || note.DurationTicks <= 0 || note.EndTick > clip.LengthTicks)
                    {
                        result.Errors.Add($"Note {note.Id} lies outside clip {clip.Name}.");
                    }
                }

                foreach (ControlChangeEvent controlChange in clip.ControlChanges)
                {
                    if (controlChange.ControllerNumber is < 0 or > 127)
                    {
                        result.Errors.Add($"Control change {controlChange.Id} has invalid controller {controlChange.ControllerNumber}.");
                    }

                    if (controlChange.Value is < 0 or > 127)
                    {
                        result.Errors.Add($"Control change {controlChange.Id} has invalid value {controlChange.Value}.");
                    }

                    if (controlChange.StartTick < 0 || controlChange.StartTick >= clip.LengthTicks)
                    {
                        result.Errors.Add($"Control change {controlChange.Id} lies outside clip {clip.Name}.");
                    }
                }

                foreach (ProgramChangeEvent programChange in clip.ProgramChanges)
                {
                    if (programChange.Bank is < 0 or > 16383 || programChange.Program is < 0 or > 127 ||
                        programChange.StartTick < 0 || programChange.StartTick >= clip.LengthTicks)
                    {
                        result.Errors.Add($"Program change {programChange.Id} is invalid or lies outside clip {clip.Name}.");
                    }
                }
            }
        }
    }

    private static void ValidateAudio(AudioProjectSettings audio, ProjectValidationResult result)
    {
        if (audio.SampleRate is < 8000 or > 48000)
        {
            result.Errors.Add("Audio sample rate must be between 8000 and 48000 Hz.");
        }
        if (audio.BufferFrames is < 128 or > 16384 || (audio.BufferFrames & (audio.BufferFrames - 1)) != 0)
        {
            result.Errors.Add("Audio buffer frames must be a power of two between 128 and 16384.");
        }
        if (audio.QueuedBufferTarget is < 2 or > 12)
        {
            result.Errors.Add("Queued audio buffer target must be between 2 and 12.");
        }
        if (!double.IsFinite(audio.MasterVolume) || audio.MasterVolume is < 0 or > 1.5)
        {
            result.Errors.Add("Master volume must be between 0 and 1.5.");
        }
        if (!double.IsFinite(audio.RenderTailSeconds) || audio.RenderTailSeconds is < 0 or > 30)
        {
            result.Errors.Add("Audio render tail must be between 0 and 30 seconds.");
        }
    }

    private static void ValidateGenerationLab(ProjectDocument project, ProjectValidationResult result)
    {
        GenerationLabDocument lab = project.GenerationLab;
        if (!lab.Enabled)
        {
            return;
        }

        if (lab.PreviewSampleCount is < 16 or > 8192)
        {
            result.Errors.Add("Generation laboratory preview sample count must be between 16 and 8192.");
        }

        if (lab.Voices.Count == 0)
        {
            result.Errors.Add("An enabled generation laboratory must contain at least one voice.");
            return;
        }

        HashSet<string> voiceIds = new(StringComparer.Ordinal);
        foreach (AlgorithmVoiceDefinition voice in lab.Voices)
        {
            if (string.IsNullOrWhiteSpace(voice.Id) || !voiceIds.Add(voice.Id))
            {
                result.Errors.Add($"Generation voice has an empty or duplicate ID: {voice.Id}.");
            }

            if (voice.MidiChannel is < 0 or > 15 || voice.GeneralMidiProgram is < 0 or > 127)
            {
                result.Errors.Add($"Voice {voice.Name} has invalid MIDI settings.");
            }

            if (voice.IsPercussion && voice.MidiChannel != 9)
            {
                result.Warnings.Add($"Percussion voice {voice.Name} is not on channel 10.");
            }

            foreach ((string key, double value) in voice.AlgorithmParameters)
            {
                if (string.IsNullOrWhiteSpace(key) || !double.IsFinite(value))
                {
                    result.Errors.Add($"Voice {voice.Name} has an invalid algorithm parameter.");
                }
            }

            ValidateGraph(voice, result);
            ValidateMappings(voice, result);
        }

        foreach (SongTrack track in project.Song.Tracks.Where(track => !string.IsNullOrWhiteSpace(track.SourceVoiceId)))
        {
            if (!voiceIds.Contains(track.SourceVoiceId))
            {
                result.Errors.Add($"Track {track.Name} references unknown generation voice {track.SourceVoiceId}.");
            }
        }
    }

    private static void ValidateStructuredComposition(ProjectDocument project, ProjectValidationResult result)
    {
        StructuredCompositionDocument composition = project.StructuredComposition;
        if (!composition.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(composition.ActiveStyleId) ||
            string.IsNullOrWhiteSpace(composition.FormGrammarId) ||
            string.IsNullOrWhiteSpace(composition.HarmonicGrammarId))
        {
            result.Errors.Add("Structured composition grammar identifiers cannot be empty.");
        }

        if (composition.CandidateCount is < 1 or > 64 || composition.BarsPerPhrase is < 1 or > 16)
        {
            result.Errors.Add("Structured composition candidate or phrase settings are outside supported ranges.");
        }

        composition.HybridStyle ??= new StyleBlendSettings();
        composition.ResolvedStylePackIds ??= new List<string>();
        if (composition.HybridStyle.Enabled)
        {
            if (string.IsNullOrWhiteSpace(composition.HybridStyle.PrimaryPackId) ||
                string.IsNullOrWhiteSpace(composition.HybridStyle.SecondaryPackId))
            {
                result.Errors.Add("An enabled hybrid style must identify both primary and secondary packs.");
            }
            if (composition.HybridStyle.SecondaryWeight is < 0 or > 100)
            {
                result.Errors.Add("Hybrid secondary weight must be between 0 and 100.");
            }
        }
        if (composition.ResolvedStylePackIds.Any(string.IsNullOrWhiteSpace))
        {
            result.Errors.Add("Resolved style-pack identifiers cannot contain empty values.");
        }

        if (composition.FormPlan.Count == 0)
        {
            result.Errors.Add("An enabled structured composition must contain a form plan.");
            return;
        }

        HashSet<string> sectionIds = new(StringComparer.Ordinal);
        int expectedBar = 0;
        foreach (FormSectionPlan section in composition.FormPlan.OrderBy(section => section.StartBar))
        {
            if (string.IsNullOrWhiteSpace(section.Id) || !sectionIds.Add(section.Id))
            {
                result.Errors.Add($"Structured form has an empty or duplicate section ID: {section.Id}.");
            }
            if (section.StartBar != expectedBar || section.LengthBars <= 0)
            {
                result.Errors.Add($"Structured section {section.Name} is not contiguous or has invalid length.");
            }
            if (!double.IsFinite(section.Energy) || section.Energy is < 0 or > 1)
            {
                result.Errors.Add($"Structured section {section.Name} has invalid energy.");
            }
            expectedBar = section.StartBar + section.LengthBars;
        }

        HashSet<string> harmonyIds = new(StringComparer.Ordinal);
        foreach (HarmonicEventPlan harmony in composition.HarmonyPlan)
        {
            if (string.IsNullOrWhiteSpace(harmony.Id) || !harmonyIds.Add(harmony.Id))
            {
                result.Errors.Add($"Harmony event has an empty or duplicate ID: {harmony.Id}.");
            }
            if (!sectionIds.Contains(harmony.SectionId))
            {
                result.Errors.Add($"Harmony event {harmony.Id} references unknown section {harmony.SectionId}.");
            }
            if (harmony.StartBar < 0 || harmony.LengthBars <= 0 || harmony.ScaleDegree is < 0 or > 6 ||
                harmony.ChordToneCount is < 3 or > 4 || harmony.Inversion is < 0 or > 8 ||
                !double.IsFinite(harmony.Tension) || harmony.Tension is < 0 or > 1)
            {
                result.Errors.Add($"Harmony event {harmony.Id} has invalid settings.");
            }
        }
        foreach (string sectionId in sectionIds)
        {
            if (!composition.HarmonyPlan.Any(harmony => harmony.SectionId == sectionId))
            {
                result.Errors.Add($"Structured section {sectionId} has no harmony events.");
            }
        }

        if (composition.Motifs.Count == 0)
        {
            result.Errors.Add("Structured composition must contain at least one motif.");
        }
        foreach (MotifDefinition motif in composition.Motifs)
        {
            if (string.IsNullOrWhiteSpace(motif.Id) || motif.DegreeOffsets.Count < 2 || motif.RhythmUnits.Count < 2 ||
                motif.RhythmUnits.Any(value => value <= 0))
            {
                result.Errors.Add($"Motif {motif.Name} is incomplete or invalid.");
            }
        }

        HashSet<string> roleIds = new(StringComparer.Ordinal);
        foreach (CompositionRoleSettings role in composition.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Id) || !roleIds.Add(role.Id))
            {
                result.Errors.Add($"Composition role has an empty or duplicate ID: {role.Id}.");
            }
            if (role.MidiChannel is < 0 or > 15 || role.GeneralMidiProgram is < 0 or > 127 ||
                role.MinimumPitch is < 0 or > 127 || role.MaximumPitch is < 0 or > 127 ||
                role.MinimumPitch > role.MaximumPitch || role.Density is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(role.RhythmPatternId) || role.InstrumentSelector is null ||
                role.InstrumentSelector.MinimumPitch is < 0 or > 127 || role.InstrumentSelector.MaximumPitch is < 0 or > 127 ||
                role.InstrumentSelector.MinimumPitch > role.InstrumentSelector.MaximumPitch ||
                role.InstrumentSelector.MaximumLayers is < 1 or > 4 ||
                !ValidSelector(role.InstrumentSelector))
            {
                result.Errors.Add($"Composition role {role.Name} has invalid settings.");
            }
        }

        foreach (SongTrack track in project.Song.Tracks.Where(track => !string.IsNullOrWhiteSpace(track.SourceVoiceId)))
        {
            int layerSeparator = track.SourceVoiceId.IndexOf('#');
            string baseRoleId = layerSeparator >= 0 ? track.SourceVoiceId[..layerSeparator] : track.SourceVoiceId;
            if (!roleIds.Contains(baseRoleId))
            {
                result.Errors.Add($"Track {track.Name} references unknown composition role {track.SourceVoiceId}.");
            }
        }

        foreach (PhraseCandidateScore score in composition.CandidateScores)
        {
            if (string.IsNullOrWhiteSpace(score.Id) || !sectionIds.Contains(score.SectionId) ||
                !roleIds.Contains(score.RoleId) || score.PhraseIndex < 0 || score.CandidateIndex < 0 ||
                !double.IsFinite(score.TotalScore))
            {
                result.Errors.Add($"Phrase candidate score {score.Id} is invalid.");
            }
        }

        foreach (IGrouping<string, PhraseCandidateScore> group in composition.CandidateScores.GroupBy(
                     score => $"{score.SectionId}|{score.RoleId}|{score.PhraseIndex}",
                     StringComparer.Ordinal))
        {
            if (group.Count(score => score.Selected) != 1)
            {
                result.Errors.Add($"Candidate group {group.Key} must contain exactly one selected candidate.");
            }
        }
    }

    private static void ValidateAdvancedComposition(ProjectDocument project, ProjectValidationResult result)
    {
        AdvancedCompositionDocument advanced = project.AdvancedComposition ??= new AdvancedCompositionDocument();
        if (!advanced.Enabled) return;

        if (advanced.Voicing.VoiceCount is < 2 or > 6 ||
            advanced.Voicing.MaximumVoiceLeap is < 1 or > 36 ||
            advanced.Voicing.MaximumAdjacentSpacing is < 3 or > 36 ||
            advanced.Voicing.CandidateBudget is < 64 or > 1_000_000)
        {
            result.Errors.Add("Advanced voicing settings are outside supported ranges.");
        }
        if (advanced.Arrangement.PopulationSize is < 6 or > 128 ||
            advanced.Arrangement.Generations is < 1 or > 200 ||
            !double.IsFinite(advanced.Arrangement.MutationRate) || advanced.Arrangement.MutationRate is < 0 or > 0.8 ||
            advanced.Arrangement.EliteCount < 1 || advanced.Arrangement.EliteCount >= advanced.Arrangement.PopulationSize)
        {
            result.Errors.Add("Evolutionary arrangement settings are outside supported ranges.");
        }
        if (!double.IsFinite(advanced.Tension.Smoothing) || advanced.Tension.Smoothing is < 0 or > 0.98 ||
            !double.IsFinite(advanced.Tension.ClimaxPosition) || advanced.Tension.ClimaxPosition is < 0 or > 1 ||
            !double.IsFinite(advanced.Tension.ClimaxStrength) || advanced.Tension.ClimaxStrength is < 0 or > 1 ||
            !double.IsFinite(advanced.Tension.ResolutionStrength) || advanced.Tension.ResolutionStrength is < 0 or > 1 ||
            advanced.Tension.SamplesPerBar is < 1 or > 16)
        {
            result.Errors.Add("Adaptive tension settings are outside supported ranges.");
        }
        if (advanced.MotifConstraints.MinimumRecurrences is < 1 or > 64 ||
            advanced.MotifConstraints.MaximumSectionsBetweenRecurrences is < 1 or > 32 ||
            !double.IsFinite(advanced.MotifConstraints.MinimumContourSimilarity) || advanced.MotifConstraints.MinimumContourSimilarity is < 0 or > 1)
        {
            result.Errors.Add("Long-range motif settings are outside supported ranges.");
        }
        if (!double.IsFinite(advanced.Neural.Temperature) || advanced.Neural.Temperature is < 0.05 or > 3 ||
            advanced.Neural.MaximumTokens is < 8 or > 32768)
        {
            result.Errors.Add("Neural adapter settings are outside supported ranges.");
        }
        if (advanced.Neural.Enabled && string.IsNullOrWhiteSpace(advanced.Neural.ModelId))
        {
            result.Warnings.Add("Neural generation is enabled without a model ID; built-in generation will remain active.");
        }

        ValidateUniqueTextValues(advanced.Extensions.PluginSearchPaths, "plugin search path", result);
        ValidateUniqueTextValues(advanced.Extensions.EnabledPluginIds, "enabled plugin ID", result);

        HashSet<string> scriptIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExpressionScriptDefinition script in advanced.Scripts)
        {
            if (string.IsNullOrWhiteSpace(script.Id) || !scriptIds.Add(script.Id) ||
                string.IsNullOrWhiteSpace(script.TargetRoleId) ||
                string.IsNullOrWhiteSpace(script.PitchExpression) || string.IsNullOrWhiteSpace(script.VelocityExpression) ||
                string.IsNullOrWhiteSpace(script.DurationExpression) || string.IsNullOrWhiteSpace(script.GateExpression) ||
                script.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || !double.IsFinite(pair.Value)))
            {
                result.Errors.Add($"Expression script '{script.Name}' is invalid.");
            }
        }

        HashSet<int> tensionBars = new();
        foreach (TensionPoint point in advanced.TensionCurve)
        {
            if (point.Bar < 0 || !tensionBars.Add(point.Bar) || !double.IsFinite(point.Value) || point.Value is < 0 or > 1)
            {
                result.Errors.Add("Adaptive tension curve contains an invalid or duplicate point.");
                break;
            }
        }

        if (!project.StructuredComposition.Enabled) return;
        HashSet<string> sectionIds = project.StructuredComposition.FormPlan.Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> roleIds = project.StructuredComposition.Roles.Select(role => role.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ExpressionScriptDefinition script in advanced.Scripts.Where(script => script.Enabled))
        {
            if (script.TargetRoleId != "*" && !roleIds.Contains(script.TargetRoleId))
            {
                result.Errors.Add($"Expression script '{script.Name}' targets unknown role '{script.TargetRoleId}'.");
            }
        }

        HashSet<string> arrangementSectionIds = new(StringComparer.Ordinal);
        foreach (ArrangementSectionPlan section in advanced.ArrangementPlan)
        {
            HashSet<string> arrangementRoleIds = new(StringComparer.Ordinal);
            if (!sectionIds.Contains(section.SectionId) || !arrangementSectionIds.Add(section.SectionId) ||
                !double.IsFinite(section.TargetEnergy) || section.TargetEnergy is < 0 or > 1 ||
                !double.IsFinite(section.Fitness) ||
                section.Roles.Any(role => !roleIds.Contains(role.RoleId) || !arrangementRoleIds.Add(role.RoleId) ||
                                          role.Intensity is < 0 or > 100))
            {
                result.Errors.Add($"Arrangement plan for section '{section.SectionId}' is invalid or duplicated.");
            }
        }

        if (advanced.MotifConstraints.Enabled && advanced.MotifConstraints.MinimumRecurrences > sectionIds.Count)
        {
            int effectiveMinimum = Math.Max(1, sectionIds.Count);
            result.Warnings.Add(
                $"Long-range motif minimum recurrences ({advanced.MotifConstraints.MinimumRecurrences}) exceed the current form's {sectionIds.Count} section(s); " +
                $"generation will use an effective minimum of {effectiveMinimum} for this form.");
        }

        HashSet<string> motifSectionIds = new(StringComparer.Ordinal);
        foreach (MotifOccurrencePlan occurrence in advanced.MotifOccurrences)
        {
            if (!sectionIds.Contains(occurrence.SectionId) || !motifSectionIds.Add(occurrence.SectionId) ||
                string.IsNullOrWhiteSpace(occurrence.MotifId) || string.IsNullOrWhiteSpace(occurrence.FamilyId) ||
                occurrence.OccurrenceIndex < 0 ||
                !double.IsFinite(occurrence.TargetSimilarity) || occurrence.TargetSimilarity is < 0 or > 1)
            {
                result.Errors.Add($"Long-range motif occurrence for section '{occurrence.SectionId}' is invalid or duplicated.");
            }
        }
    }

    private static void ValidateOrchestration(ProjectDocument project, ProjectValidationResult result)
    {
        ProceduralOrchestrationDocument orchestration = project.Orchestration ??= new ProceduralOrchestrationDocument();
        if (!orchestration.Enabled) return;

        TotalRandomizationSettings randomization = orchestration.Randomization;
        if (randomization.Surprise is < 0 or > 100 || randomization.SurpriseBudget is < 0 or > 200 ||
            randomization.MaximumStyleSources is < 1 or > 5 ||
            randomization.MinimumEnsembleSize is < 1 or > 16 ||
            randomization.MaximumEnsembleSize < randomization.MinimumEnsembleSize || randomization.MaximumEnsembleSize > 16)
        {
            result.Errors.Add("Orchestration randomization settings are outside supported ranges.");
        }

        InstrumentCatalogDocument catalog = orchestration.Catalog;
        HashSet<string> presetKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstrumentPresetDescriptor preset in catalog.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Key) || !presetKeys.Add(preset.Key) ||
                preset.Bank is < 0 or > 16383 || preset.Program is < 0 or > 127 ||
                preset.SuggestedLowNote is < 0 or > 127 || preset.SuggestedHighNote is < 0 or > 127 ||
                preset.SuggestedLowNote > preset.SuggestedHighNote ||
                !Finite01(preset.Timbre.Attack) || !Finite01(preset.Timbre.Sustain) ||
                !Finite01(preset.Timbre.Brightness) || !Finite01(preset.Timbre.Density) ||
                !Finite01(preset.Timbre.Percussiveness) || !Finite01(preset.Timbre.PitchStability) ||
                !Finite01(preset.Timbre.Confidence))
            {
                result.Errors.Add($"Instrument preset '{preset.Name}' is invalid or duplicated.");
            }
        }
        if (catalog.Presets.Count == 0)
        {
            result.Warnings.Add("Orchestration is enabled without an instrument catalog; the GM fallback will be rebuilt at generation time.");
        }

        HashSet<string> usageKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (PresetUsageRecord usage in orchestration.UsageHistory)
        {
            if (string.IsNullOrWhiteSpace(usage.PresetKey) || !usageKeys.Add(usage.PresetKey) ||
                usage.UseCount < 0 || usage.AuditionCount < 0 || usage.PrimaryRoleCount < 0 || usage.LastUsedSequence < 0)
            {
                result.Errors.Add("Instrument usage history contains an invalid or duplicate record.");
            }
        }

        HashSet<string> sectionIds = project.Song.Sections.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> roleIds = project.StructuredComposition.Roles.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> planSections = new(StringComparer.Ordinal);
        foreach (SectionOrchestrationPlan section in orchestration.SectionPlans)
        {
            if (!sectionIds.Contains(section.SectionId) || !planSections.Add(section.SectionId) ||
                !double.IsFinite(section.TargetEnergy) || section.TargetEnergy is < 0 or > 1 ||
                !double.IsFinite(section.EnsembleScore))
            {
                result.Errors.Add($"Orchestration plan for section '{section.SectionId}' is invalid or duplicated.");
                continue;
            }
            HashSet<string> assignmentIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (InstrumentAssignmentPlan assignment in section.Assignments)
            {
                string assignmentKey = $"{assignment.RoleId}|{assignment.LayerIndex}";
                if (!roleIds.Contains(assignment.RoleId) || !assignmentIds.Add(assignmentKey) ||
                    assignment.LayerIndex is < 0 or > 3 || assignment.Bank is < 0 or > 16383 ||
                    assignment.Program is < 0 or > 127 || assignment.MidiChannel is < 0 or > 15 ||
                    string.IsNullOrWhiteSpace(assignment.PresetKey) || (presetKeys.Count > 0 && !presetKeys.Contains(assignment.PresetKey)) ||
                    !double.IsFinite(assignment.Volume) || assignment.Volume is < 0 or > 1.5 ||
                    !double.IsFinite(assignment.Pan) || assignment.Pan is < -1 or > 1 ||
                    !double.IsFinite(assignment.SuitabilityScore))
                {
                    result.Errors.Add($"Orchestration assignment '{assignmentKey}' in section '{section.SectionId}' is invalid.");
                }
            }
        }
    }


    private static void ValidateReferenceAnalysis(ProjectDocument project, ProjectValidationResult result)
    {
        ReferenceAnalysisDocument reference = project.ReferenceAnalysis;
        if (reference is null)
        {
            result.Errors.Add("Reference analysis document is null.");
            return;
        }
        if (!reference.Enabled) return;
        ReferenceCompositionBlueprint blueprint = reference.ActiveBlueprint;
        if (blueprint is null || string.IsNullOrWhiteSpace(blueprint.Id) ||
            string.IsNullOrWhiteSpace(reference.ActiveBlueprintId) ||
            !reference.ActiveBlueprintId.Equals(blueprint.Id, StringComparison.Ordinal))
        {
            result.Errors.Add("An enabled reference analysis must have a matching active blueprint ID.");
            return;
        }
        if (blueprint.Source.TicksPerQuarterNote is < 24 or > 32767 || blueprint.Source.BarCount < 1 ||
            !double.IsFinite(blueprint.Source.PrimaryTempo) || blueprint.Source.PrimaryTempo is < 1 or > 1000 ||
            blueprint.Source.MeterNumerator < 1 || blueprint.Source.MeterDenominator < 1 ||
            (blueprint.Source.MeterDenominator & (blueprint.Source.MeterDenominator - 1)) != 0)
        {
            result.Errors.Add("Reference blueprint source timing metadata is invalid.");
        }
        HashSet<string> sectionIds = new(StringComparer.Ordinal);
        int expectedStart = 0;
        foreach (ReferenceSectionProfile section in blueprint.Form.Sections.OrderBy(item => item.StartBar))
        {
            if (string.IsNullOrWhiteSpace(section.Id) || !sectionIds.Add(section.Id) || section.StartBar != expectedStart ||
                section.LengthBars is < 1 or > 64 || !Finite01(section.Energy) || !double.IsFinite(section.Density) || section.Density < 0 ||
                !double.IsFinite(section.MeanPitch) || section.MeanPitch is < 0 or > 127 ||
                !double.IsFinite(section.MeanVelocity) || section.MeanVelocity is < 0 or > 127 || !Finite01(section.BoundaryConfidence))
            {
                result.Errors.Add($"Reference section '{section.Id}' is invalid, duplicated, or non-contiguous.");
            }
            expectedStart = section.StartBar + section.LengthBars;
        }
        if (blueprint.Form.Sections.Count == 0 || blueprint.Form.TotalBars != expectedStart)
        {
            result.Errors.Add("Reference form must contain contiguous sections whose lengths equal total bars.");
        }
        HashSet<string> trackIds = new(StringComparer.Ordinal);
        foreach (ReferenceTrackProfile track in blueprint.Instrumentation.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.TrackId) || !trackIds.Add(track.TrackId) || track.MidiChannel is < 0 or > 15 ||
                track.Bank is < 0 or > 16383 || track.Program is < 0 or > 127 || !Finite01(track.RoleConfidence) ||
                track.NoteCount < 0 || !double.IsFinite(track.AveragePitch) || track.AveragePitch is < 0 or > 127 ||
                track.MinimumPitch is < 0 or > 127 || track.MaximumPitch is < 0 or > 127 || track.MinimumPitch > track.MaximumPitch ||
                !double.IsFinite(track.NotesPerBeat) || track.NotesPerBeat < 0 || !double.IsFinite(track.Polyphony) || track.Polyphony < 0 ||
                !Finite01(track.MonophonicRatio) || !Finite01(track.ChordOnsetRatio) || !Finite01(track.Repetition))
            {
                result.Errors.Add($"Reference track profile '{track.TrackName}' is invalid or duplicated.");
            }
        }
        HashSet<string> chordIds = new(StringComparer.Ordinal);
        foreach (ReferenceHarmonicEvent harmony in blueprint.Harmony.Events)
        {
            if (string.IsNullOrWhiteSpace(harmony.Id) || !chordIds.Add(harmony.Id) || harmony.StartBar < 0 || harmony.StartBar >= blueprint.Form.TotalBars ||
                !double.IsFinite(harmony.StartBeat) || harmony.StartBeat < 0 || !double.IsFinite(harmony.LengthBeats) || harmony.LengthBeats <= 0 ||
                harmony.RootPitchClass is < 0 or > 11 || harmony.ScaleDegree is < 0 or > 6 ||
                harmony.InversionBassPitchClass is < -1 or > 11 || harmony.PitchClasses.Any(value => value is < 0 or > 11) ||
                !Finite01(harmony.Tension) || !Finite01(harmony.Confidence))
            {
                result.Errors.Add($"Reference harmonic event '{harmony.Id}' is invalid or duplicated.");
            }
        }
        HashSet<string> motifIds = new(StringComparer.Ordinal);
        foreach (ReferenceMotifPattern motif in blueprint.Motifs.Motifs)
        {
            if (string.IsNullOrWhiteSpace(motif.Id) || !motifIds.Add(motif.Id) || motif.Intervals.Count > 64 || motif.RhythmUnits.Count > 64 ||
                motif.RhythmUnits.Any(value => value is < 1 or > 128) || motif.Contour.Any(value => value is < -1 or > 1) ||
                !Finite01(motif.Importance) || !Finite01(motif.Confidence))
            {
                result.Errors.Add($"Reference motif '{motif.Id}' is invalid or duplicated.");
            }
        }
        if (blueprint.Rhythm.StepsPerBar is < 4 or > 64 || !double.IsFinite(blueprint.Rhythm.SwingRatio) || blueprint.Rhythm.SwingRatio is < 0.4 or > 0.8 ||
            !Finite01(blueprint.Rhythm.Syncopation) || !double.IsFinite(blueprint.Rhythm.MeanNotesPerBeat) || blueprint.Rhythm.MeanNotesPerBeat < 0)
        {
            result.Errors.Add("Reference rhythm profile is invalid.");
        }
        foreach (ReferenceRoleGroove groove in blueprint.Rhythm.RoleGrooves)
        {
            if (groove.OnsetProbabilities.Count != groove.AccentProfile.Count || groove.OnsetProbabilities.Any(value => !Finite01(value)) ||
                groove.AccentProfile.Any(value => !Finite01(value)) || groove.RepresentativePattern.Any(value => value is not 0 and not 1) ||
                !double.IsFinite(groove.Density) || groove.Density < 0 || !Finite01(groove.Syncopation))
            {
                result.Errors.Add($"Reference groove for role '{groove.Role}' is invalid.");
            }
        }
        if (!Enum.IsDefined(blueprint.Generation.Mode) ||
            !Enum.IsDefined(blueprint.Generation.OriginalityPolicy) ||
            !Enum.IsDefined(blueprint.Generation.InstrumentationPolicy))
        {
            result.Errors.Add("Reference generation mode, originality policy, or instrumentation policy is invalid.");
        }
        foreach ((ReferenceBlueprintDimension dimension, double value) in blueprint.Generation.Preservation)
        {
            if (!Enum.IsDefined(dimension) || !Finite01(value)) result.Errors.Add($"Reference preservation value for '{dimension}' is invalid.");
        }
        if (Enum.GetValues<ReferenceBlueprintDimension>().Any(dimension => !blueprint.Generation.Preservation.ContainsKey(dimension)))
        {
            result.Errors.Add("Reference generation policy is missing one or more preservation dimensions.");
        }
        ReferenceOriginalitySettings originality = blueprint.Generation.Originality;
        if (!Finite01(originality.MaximumExactNoteOverlap) || !Finite01(originality.MaximumIntervalNGramOverlap) ||
            !Finite01(originality.MaximumRhythmNGramOverlap) || !Finite01(originality.MaximumMotifContourSimilarity) ||
            !Finite01(originality.MaximumChordProgressionOverlap) || originality.IntervalNGramSize is < 2 or > 16 ||
            originality.RhythmNGramSize is < 2 or > 16 || originality.MaximumRepairPasses is < 0 or > 12 ||
            blueprint.Generation.CandidateCount is < 1 or > 64)
        {
            result.Errors.Add("Reference originality or candidate settings are invalid.");
        }
        if (!Finite01(blueprint.Confidence.Overall) || !Finite01(blueprint.Confidence.Roles) || !Finite01(blueprint.Confidence.Form) ||
            !Finite01(blueprint.Confidence.Harmony) || !Finite01(blueprint.Confidence.Motifs) || !Finite01(blueprint.Confidence.Rhythm) ||
            !Finite01(blueprint.Confidence.Arrangement) || !Finite01(blueprint.Confidence.Performance))
        {
            result.Errors.Add("Reference analysis confidence values must be finite values between zero and one.");
        }
        foreach (ReferenceDescendantEvaluation evaluation in reference.RecentDescendants)
        {
            double[] metrics = [evaluation.StructuralMatch, evaluation.HarmonicMatch, evaluation.GrooveMatch, evaluation.ArrangementMatch,
                evaluation.TensionMatch, evaluation.ExactNoteOverlap, evaluation.IntervalNGramOverlap, evaluation.RhythmNGramOverlap,
                evaluation.MotifContourSimilarity, evaluation.ChordProgressionOverlap, evaluation.NoveltyScore, evaluation.QualityScore];
            if (string.IsNullOrWhiteSpace(evaluation.CandidateId) || metrics.Any(value => !Finite01(value)) || evaluation.RepairPasses is < 0 or > 12)
            {
                result.Errors.Add($"Reference descendant evaluation '{evaluation.CandidateId}' is invalid.");
            }
        }
    }

    private static bool Finite01(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool ValidSelector(InstrumentSelectorSettings selector) =>
        selector.MinimumPitch is >= 0 and <= 127 && selector.MaximumPitch is >= 0 and <= 127 &&
        selector.MinimumPitch <= selector.MaximumPitch && selector.MaximumLayers is >= 1 and <= 4 &&
        FiniteRange(selector.MinimumAttack, selector.MaximumAttack) &&
        FiniteRange(selector.MinimumSustain, selector.MaximumSustain) &&
        FiniteRange(selector.MinimumBrightness, selector.MaximumBrightness) &&
        FiniteRange(selector.MinimumPercussiveness, selector.MaximumPercussiveness) &&
        double.IsFinite(selector.StyleAffinityWeight) && selector.StyleAffinityWeight is >= 0 and <= 10 &&
        double.IsFinite(selector.NoveltyWeight) && selector.NoveltyWeight is >= 0 and <= 10 &&
        double.IsFinite(selector.RandomWeight) && selector.RandomWeight is >= 0 and <= 10 &&
        selector.RequiredTags.All(item => !string.IsNullOrWhiteSpace(item)) &&
        selector.PreferredTags.All(item => !string.IsNullOrWhiteSpace(item)) &&
        selector.AvoidTags.All(item => !string.IsNullOrWhiteSpace(item));

    private static bool FiniteRange(double minimum, double maximum) =>
        Finite01(minimum) && Finite01(maximum) && minimum <= maximum;

    private static void ValidateUniqueTextValues(IEnumerable<string> values, string kind, ProjectValidationResult result)
    {
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !unique.Add(value.Trim()))
            {
                result.Errors.Add($"An advanced-composition {kind} is empty or duplicated.");
            }
        }
    }

    private static void ValidateGraph(AlgorithmVoiceDefinition voice, ProjectValidationResult result)
    {
        HashSet<string> available = new(SourceNodeIds, StringComparer.OrdinalIgnoreCase);
        foreach (ValueTransformNodeDefinition node in voice.Graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || available.Contains(node.Id))
            {
                result.Errors.Add($"Voice {voice.Name} has an empty or duplicate graph node ID: {node.Id}.");
                continue;
            }

            if (!available.Contains(node.InputNodeId))
            {
                result.Errors.Add($"Graph node {node.Id} in voice {voice.Name} references unavailable input {node.InputNodeId}.");
            }

            available.Add(node.Id);
            foreach (double value in node.Parameters.Values)
            {
                if (!double.IsFinite(value))
                {
                    result.Errors.Add($"Graph node {node.Id} in voice {voice.Name} has a non-finite parameter.");
                }
            }
        }

        foreach (string output in new[]
                 {
                     voice.Graph.PitchOutputNodeId,
                     voice.Graph.RhythmOutputNodeId,
                     voice.Graph.VelocityOutputNodeId,
                     voice.Graph.ControlOutputNodeId
                 })
        {
            if (!available.Contains(output))
            {
                result.Errors.Add($"Voice {voice.Name} graph output references unknown node {output}.");
            }
        }
    }

    private static void ValidateMappings(AlgorithmVoiceDefinition voice, ProjectValidationResult result)
    {
        ScaleMapperSettings pitch = voice.PitchMapping;
        if (pitch.MinimumDegree > pitch.MaximumDegree ||
            pitch.MinimumMidiPitch is < 0 or > 127 ||
            pitch.MaximumMidiPitch is < 0 or > 127 ||
            pitch.MinimumMidiPitch > pitch.MaximumMidiPitch)
        {
            result.Errors.Add($"Voice {voice.Name} has invalid pitch mapping bounds.");
        }

        if (voice.IsPercussion && (pitch.PercussionPalette.Count == 0 || pitch.PercussionPalette.Any(value => value is < 0 or > 127)))
        {
            result.Errors.Add($"Percussion voice {voice.Name} has an invalid note palette.");
        }

        RhythmMapperSettings rhythm = voice.RhythmMapping;
        if (rhythm.StepsPerBeat is < 1 or > 32 ||
            rhythm.GateThreshold is < 0 or > 1 ||
            rhythm.MinimumDurationSteps < 1 ||
            rhythm.MaximumDurationSteps < rhythm.MinimumDurationSteps ||
            rhythm.NoteLengthRatio is <= 0 or > 1.5 ||
            rhythm.Swing is < 0 or > 0.95)
        {
            result.Errors.Add($"Voice {voice.Name} has invalid rhythm mapping settings.");
        }

        VelocityMapperSettings velocity = voice.VelocityMapping;
        if (velocity.MinimumVelocity is < 1 or > 127 ||
            velocity.MaximumVelocity is < 1 or > 127 ||
            velocity.MinimumVelocity > velocity.MaximumVelocity ||
            velocity.AccentEverySteps < 0)
        {
            result.Errors.Add($"Voice {voice.Name} has invalid velocity mapping settings.");
        }

        ControlChangeMapperSettings control = voice.ControlChangeMapping;
        if (control.ControllerNumber is < 0 or > 127 ||
            control.MinimumValue is < 0 or > 127 ||
            control.MaximumValue is < 0 or > 127 ||
            control.MinimumValue > control.MaximumValue ||
            control.SampleEverySteps < 1)
        {
            result.Errors.Add($"Voice {voice.Name} has invalid control-change mapping settings.");
        }
    }
}
