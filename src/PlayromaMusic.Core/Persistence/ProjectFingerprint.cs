using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Audio;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Core.Persistence;

public static class ProjectFingerprint
{
    public static string Compute(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        StringBuilder canonical = new();
        canonical.Append("schema=").Append(project.SchemaVersion).Append('\n');
        canonical.Append("engine=").Append(project.EngineVersion).Append('\n');
        canonical.Append("seed=").Append(project.MasterSeed).Append('\n');
        canonical.Append("title=").Append(project.Title).Append('\n');
        canonical.Append("ppq=").Append(project.Song.TicksPerQuarterNote).Append('\n');
        canonical.Append("key=").Append((int)project.Song.KeyRoot).Append('\n');
        canonical.Append("scale=").Append(project.Song.Scale).Append('\n');

        foreach (TempoChange tempo in project.Song.TempoMap.OrderBy(item => item.Tick))
        {
            canonical.Append("tempo|").Append(tempo.Tick).Append('|')
                .Append(Format(tempo.BeatsPerMinute)).Append('\n');
        }

        foreach (MeterChange meter in project.Song.MeterMap.OrderBy(item => item.Tick))
        {
            canonical.Append("meter|").Append(meter.Tick).Append('|')
                .Append(meter.Numerator).Append('|').Append(meter.Denominator).Append('\n');
        }

        foreach (SongSection section in project.Song.Sections.OrderBy(item => item.StartTick).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("section|").Append(section.Id).Append('|').Append(section.Name).Append('|')
                .Append(section.StartTick).Append('|').Append(section.LengthTicks).Append('|')
                .Append(Format(section.Energy)).Append('\n');
        }

        AppendAudio(canonical, project.Audio);
        AppendGenerationLab(canonical, project.GenerationLab);
        AppendStructuredComposition(canonical, project.StructuredComposition);
        AppendAdvancedComposition(canonical, project.AdvancedComposition);
        AppendOrchestration(canonical, project.Orchestration);
        AppendReferenceAnalysis(canonical, project.ReferenceAnalysis);

        foreach (SongTrack track in project.Song.Tracks)
        {
            canonical.Append("track|").Append(track.Id).Append('|').Append(track.Name).Append('|')
                .Append(track.Role).Append('|').Append(track.MidiChannel).Append('|')
                .Append(track.GeneralMidiProgram).Append('|').Append(track.MidiBank).Append('|')
                .Append(track.PresetKey).Append('|').Append(track.SoundFontId).Append('|').Append(track.IsPercussion ? 1 : 0).Append('|')
                .Append(track.Muted ? 1 : 0).Append('|').Append(track.Solo ? 1 : 0).Append('|')
                .Append(Format(track.Volume)).Append('|').Append(Format(track.Pan)).Append('|')
                .Append(track.SourceVoiceId).Append('|').Append(track.GeneratorId).Append('|')
                .Append(track.GenerationRevision).Append('\n');

            foreach (SongClip clip in track.Clips.OrderBy(item => item.StartTick).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                canonical.Append("clip|").Append(clip.Id).Append('|').Append(clip.Name).Append('|')
                    .Append(clip.StartTick).Append('|').Append(clip.LengthTicks).Append('\n');

                foreach (NoteEvent note in clip.Notes
                             .OrderBy(item => item.StartTick)
                             .ThenBy(item => item.Pitch)
                             .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    canonical.Append("note|").Append(note.Id).Append('|').Append(note.Pitch).Append('|')
                        .Append(note.StartTick).Append('|').Append(note.DurationTicks).Append('|')
                        .Append(note.Velocity).Append('\n');
                }

                foreach (ControlChangeEvent controlChange in clip.ControlChanges
                             .OrderBy(item => item.StartTick)
                             .ThenBy(item => item.ControllerNumber)
                             .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    canonical.Append("cc|").Append(controlChange.Id).Append('|')
                        .Append(controlChange.ControllerNumber).Append('|').Append(controlChange.Value).Append('|')
                        .Append(controlChange.StartTick).Append('\n');
                }

                foreach (ProgramChangeEvent programChange in clip.ProgramChanges
                             .OrderBy(item => item.StartTick)
                             .ThenBy(item => item.Bank)
                             .ThenBy(item => item.Program)
                             .ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    canonical.Append("program|").Append(programChange.Id).Append('|')
                        .Append(programChange.Bank).Append('|').Append(programChange.Program).Append('|')
                        .Append(programChange.StartTick).Append('|').Append(programChange.PresetKey).Append('|')
                        .Append(programChange.PresetName).Append('\n');
                }
            }
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendAudio(StringBuilder canonical, AudioProjectSettings audio)
    {
        canonical.Append("audio|").Append(audio.PreferredBackend).Append('|')
            .Append(audio.SoundFontPath).Append('|').Append(audio.SampleRate).Append('|')
            .Append(audio.BufferFrames).Append('|').Append(audio.QueuedBufferTarget).Append('|')
            .Append(Format(audio.MasterVolume)).Append('|').Append(audio.LoopEnabled ? 1 : 0).Append('|')
            .Append(Format(audio.RenderTailSeconds)).Append('|')
            .Append(audio.AutoAuditionAfterEdit ? 1 : 0).Append('\n');
    }

    private static void AppendGenerationLab(StringBuilder canonical, GenerationLabDocument lab)
    {
        canonical.Append("lab|").Append(lab.Enabled ? 1 : 0).Append('|')
            .Append(lab.ActivePresetId).Append('|').Append(lab.PreviewSampleCount).Append('\n');

        foreach (AlgorithmVoiceDefinition voice in lab.Voices)
        {
            canonical.Append("voice|").Append(voice.Id).Append('|').Append(voice.Name).Append('|')
                .Append(voice.Enabled ? 1 : 0).Append('|').Append(voice.Locked ? 1 : 0).Append('|')
                .Append(voice.Revision).Append('|').Append(voice.Algorithm).Append('|')
                .Append(voice.ParameterPresetId).Append('|').Append(voice.Role).Append('|')
                .Append(voice.MidiChannel).Append('|').Append(voice.GeneralMidiProgram).Append('|')
                .Append(voice.IsPercussion ? 1 : 0).Append('\n');

            foreach ((string key, double value) in voice.AlgorithmParameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                canonical.Append("algorithm-param|").Append(key).Append('|').Append(Format(value)).Append('\n');
            }

            foreach (ValueTransformNodeDefinition node in voice.Graph.Nodes)
            {
                canonical.Append("node|").Append(node.Id).Append('|').Append(node.Name).Append('|')
                    .Append(node.Kind).Append('|').Append(node.InputNodeId).Append('\n');
                foreach ((string key, double value) in node.Parameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    canonical.Append("node-param|").Append(node.Id).Append('|').Append(key).Append('|')
                        .Append(Format(value)).Append('\n');
                }
            }

            canonical.Append("outputs|").Append(voice.Graph.PitchOutputNodeId).Append('|')
                .Append(voice.Graph.RhythmOutputNodeId).Append('|')
                .Append(voice.Graph.VelocityOutputNodeId).Append('|')
                .Append(voice.Graph.ControlOutputNodeId).Append('\n');

            ScaleMapperSettings pitch = voice.PitchMapping;
            canonical.Append("pitch-map|").Append(pitch.UseProjectScale ? 1 : 0).Append('|')
                .Append(pitch.ScaleOverride).Append('|').Append(pitch.BaseOctave).Append('|')
                .Append(pitch.MinimumDegree).Append('|').Append(pitch.MaximumDegree).Append('|')
                .Append(pitch.MinimumMidiPitch).Append('|').Append(pitch.MaximumMidiPitch).Append('|')
                .Append(string.Join(",", pitch.PercussionPalette)).Append('\n');

            RhythmMapperSettings rhythm = voice.RhythmMapping;
            canonical.Append("rhythm-map|").Append(rhythm.StepsPerBeat).Append('|')
                .Append(Format(rhythm.GateThreshold)).Append('|').Append(rhythm.MinimumDurationSteps).Append('|')
                .Append(rhythm.MaximumDurationSteps).Append('|').Append(Format(rhythm.NoteLengthRatio)).Append('|')
                .Append(Format(rhythm.Swing)).Append('\n');

            VelocityMapperSettings velocity = voice.VelocityMapping;
            canonical.Append("velocity-map|").Append(velocity.MinimumVelocity).Append('|')
                .Append(velocity.MaximumVelocity).Append('|').Append(velocity.AccentEverySteps).Append('|')
                .Append(velocity.AccentAmount).Append('\n');

            ControlChangeMapperSettings control = voice.ControlChangeMapping;
            canonical.Append("cc-map|").Append(control.Enabled ? 1 : 0).Append('|')
                .Append(control.ControllerNumber).Append('|').Append(control.MinimumValue).Append('|')
                .Append(control.MaximumValue).Append('|').Append(control.SampleEverySteps).Append('\n');
        }
    }

    private static void AppendStructuredComposition(StringBuilder canonical, StructuredCompositionDocument composition)
    {
        canonical.Append("structured|").Append(composition.Enabled ? 1 : 0).Append('|')
            .Append(composition.ActiveStyleId).Append('|').Append(composition.FormGrammarId).Append('|')
            .Append(composition.HarmonicGrammarId).Append('|').Append(composition.GenerationRevision).Append('|')
            .Append(composition.CandidateCount).Append('|').Append(composition.BarsPerPhrase).Append('\n');

        foreach (FormDerivationStep step in composition.FormDerivation.OrderBy(item => item.Step))
        {
            canonical.Append("form-derivation|").Append(step.Step).Append('|').Append(step.ExpandedSymbol).Append('|')
                .Append(step.Production).Append('|').Append(string.Join(",", step.ResultingSymbols)).Append('\n');
        }

        foreach (FormSectionPlan section in composition.FormPlan.OrderBy(item => item.StartBar).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("form-section|").Append(section.Id).Append('|').Append(section.Symbol).Append('|')
                .Append(section.Name).Append('|').Append(section.Kind).Append('|').Append(section.VariationIndex).Append('|')
                .Append(section.StartBar).Append('|').Append(section.LengthBars).Append('|')
                .Append(Format(section.Energy)).Append('|').Append(section.MotifTreatment).Append('\n');
        }

        foreach (HarmonicEventPlan harmony in composition.HarmonyPlan.OrderBy(item => item.StartBar).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("harmony|").Append(harmony.Id).Append('|').Append(harmony.SectionId).Append('|')
                .Append(harmony.StartBar).Append('|').Append(harmony.LengthBars).Append('|')
                .Append(harmony.ScaleDegree).Append('|').Append(harmony.ChordToneCount).Append('|')
                .Append(harmony.Inversion).Append('|').Append(harmony.Function).Append('|')
                .Append(harmony.Cadence).Append('|').Append(Format(harmony.Tension)).Append('\n');
        }

        foreach (MotifDefinition motif in composition.Motifs.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("motif|").Append(motif.Id).Append('|').Append(motif.Name).Append('|')
                .Append(motif.Revision).Append('|').Append(string.Join(",", motif.DegreeOffsets)).Append('|')
                .Append(string.Join(",", motif.RhythmUnits)).Append('|').Append(string.Join(",", motif.Accents)).Append('\n');
        }

        foreach (CompositionRoleSettings role in composition.Roles)
        {
            canonical.Append("composition-role|").Append(role.Id).Append('|').Append(role.Name).Append('|')
                .Append(role.Role).Append('|').Append(role.Enabled ? 1 : 0).Append('|').Append(role.Locked ? 1 : 0).Append('|')
                .Append(role.Revision).Append('|').Append(role.MidiChannel).Append('|').Append(role.GeneralMidiProgram).Append('|')
                .Append(role.IsPercussion ? 1 : 0).Append('|').Append(role.RhythmPatternId).Append('|')
                .Append(role.GeneratorId).Append('|').Append(role.MinimumPitch).Append('|').Append(role.MaximumPitch).Append('|').Append(role.Density).Append('\n');
            InstrumentSelectorSettings selector = role.InstrumentSelector ?? new InstrumentSelectorSettings();
            canonical.Append("role-selector|").Append(role.Id).Append('|')
                .Append(string.Join(",", selector.Families.OrderBy(item => item))).Append('|')
                .Append(string.Join(",", selector.RequiredTags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(string.Join(",", selector.PreferredTags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(string.Join(",", selector.AvoidTags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(selector.MinimumPitch).Append('|').Append(selector.MaximumPitch).Append('|')
                .Append(Format(selector.MinimumAttack)).Append('|').Append(Format(selector.MaximumAttack)).Append('|')
                .Append(Format(selector.MinimumSustain)).Append('|').Append(Format(selector.MaximumSustain)).Append('|')
                .Append(Format(selector.MinimumBrightness)).Append('|').Append(Format(selector.MaximumBrightness)).Append('|')
                .Append(Format(selector.MinimumPercussiveness)).Append('|').Append(Format(selector.MaximumPercussiveness)).Append('|')
                .Append(Format(selector.StyleAffinityWeight)).Append('|').Append(Format(selector.NoveltyWeight)).Append('|')
                .Append(Format(selector.RandomWeight)).Append('|').Append(selector.MaximumLayers).Append('|')
                .Append(string.Join(",", selector.AllowedStrategies.OrderBy(item => item))).Append('\n');
        }

        foreach (PhraseCandidateScore score in composition.CandidateScores
                     .OrderBy(item => item.SectionId, StringComparer.Ordinal)
                     .ThenBy(item => item.RoleId, StringComparer.Ordinal)
                     .ThenBy(item => item.PhraseIndex)
                     .ThenBy(item => item.CandidateIndex))
        {
            canonical.Append("candidate|").Append(score.Id).Append('|').Append(score.SectionId).Append('|')
                .Append(score.RoleId).Append('|').Append(score.PhraseIndex).Append('|').Append(score.CandidateIndex).Append('|')
                .Append(score.Selected ? 1 : 0).Append('|').Append(score.PassedHardConstraints ? 1 : 0).Append('|')
                .Append(Format(score.TotalScore)).Append('|').Append(Format(score.ChordToneScore)).Append('|')
                .Append(Format(score.StepwiseMotionScore)).Append('|').Append(Format(score.MotifFidelityScore)).Append('|')
                .Append(Format(score.PhraseClosureScore)).Append('|').Append(Format(score.RhythmBalanceScore)).Append('|')
                .Append(Format(score.RangeScore)).Append('|').Append(Format(score.LeapPenalty)).Append('|')
                .Append(string.Join(";", score.Diagnostics)).Append('\n');
        }
    }

    private static void AppendAdvancedComposition(StringBuilder canonical, AdvancedCompositionDocument advanced)
    {
        canonical.Append("advanced|").Append(advanced.Enabled ? 1 : 0).Append('|')
            .Append(advanced.Voicing.Enabled ? 1 : 0).Append('|').Append(advanced.Voicing.VoiceCount).Append('|')
            .Append(advanced.Voicing.MaximumVoiceLeap).Append('|').Append(advanced.Voicing.MaximumAdjacentSpacing).Append('|')
            .Append(advanced.Voicing.AllowVoiceCrossing ? 1 : 0).Append('|').Append(advanced.Voicing.PreferCommonTones ? 1 : 0).Append('|')
            .Append(advanced.Voicing.CandidateBudget).Append('|')
            .Append(advanced.Arrangement.Enabled ? 1 : 0).Append('|').Append(advanced.Arrangement.PopulationSize).Append('|')
            .Append(advanced.Arrangement.Generations).Append('|').Append(Format(advanced.Arrangement.MutationRate)).Append('|')
            .Append(advanced.Arrangement.EliteCount).Append('|').Append(advanced.Arrangement.PreserveLeadAtCadences ? 1 : 0).Append('|')
            .Append(advanced.Arrangement.RequireRhythmicFoundationAtHighEnergy ? 1 : 0).Append('|')
            .Append(advanced.Tension.Enabled ? 1 : 0).Append('|').Append(Format(advanced.Tension.Smoothing)).Append('|')
            .Append(Format(advanced.Tension.ClimaxPosition)).Append('|').Append(Format(advanced.Tension.ClimaxStrength)).Append('|')
            .Append(Format(advanced.Tension.ResolutionStrength)).Append('|').Append(advanced.Tension.SamplesPerBar).Append('|')
            .Append(advanced.MotifConstraints.Enabled ? 1 : 0).Append('|').Append(advanced.MotifConstraints.MinimumRecurrences).Append('|')
            .Append(advanced.MotifConstraints.MaximumSectionsBetweenRecurrences).Append('|')
            .Append(Format(advanced.MotifConstraints.MinimumContourSimilarity)).Append('|')
            .Append(advanced.MotifConstraints.RequireClimacticReturn ? 1 : 0).Append('|').Append(advanced.MotifConstraints.RequireFinalRecall ? 1 : 0).Append('|')
            .Append(advanced.Extensions.EnableExternalPlugins ? 1 : 0).Append('|').Append(advanced.Extensions.EnableUserScripts ? 1 : 0).Append('|')
            .Append(advanced.Neural.Enabled ? 1 : 0).Append('|').Append(advanced.Neural.ModelId).Append('|')
            .Append(Format(advanced.Neural.Temperature)).Append('|').Append(advanced.Neural.MaximumTokens).Append('|')
            .Append(advanced.Neural.FallBackToBuiltInGenerator ? 1 : 0).Append('\n');

        foreach (string path in advanced.Extensions.PluginSearchPaths.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            canonical.Append("plugin-path|").Append(path).Append('\n');
        foreach (string id in advanced.Extensions.EnabledPluginIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            canonical.Append("plugin-enabled|").Append(id).Append('\n');

        foreach (TensionPoint point in advanced.TensionCurve.OrderBy(item => item.Bar))
            canonical.Append("tension|").Append(point.Bar).Append('|').Append(Format(point.Value)).Append('|').Append(point.Source).Append('\n');
        foreach (ArrangementSectionPlan section in advanced.ArrangementPlan.OrderBy(item => item.SectionId, StringComparer.Ordinal))
        {
            canonical.Append("arrangement|").Append(section.SectionId).Append('|').Append(Format(section.TargetEnergy)).Append('|').Append(Format(section.Fitness)).Append('\n');
            foreach (ArrangementRoleState role in section.Roles.OrderBy(item => item.RoleId, StringComparer.Ordinal))
                canonical.Append("arrangement-role|").Append(section.SectionId).Append('|').Append(role.RoleId).Append('|').Append(role.Role).Append('|').Append(role.Active ? 1 : 0).Append('|').Append(role.Intensity).Append('\n');
        }
        foreach (MotifOccurrencePlan occurrence in advanced.MotifOccurrences.OrderBy(item => item.SectionId, StringComparer.Ordinal))
            canonical.Append("motif-occurrence|").Append(occurrence.SectionId).Append('|').Append(occurrence.MotifId).Append('|').Append(occurrence.FamilyId).Append('|').Append(occurrence.OccurrenceIndex).Append('|').Append(occurrence.Transformation).Append('|').Append(Format(occurrence.TargetSimilarity)).Append('|').Append(occurrence.Required ? 1 : 0).Append('\n');
        foreach (ExpressionScriptDefinition script in advanced.Scripts.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            canonical.Append("script|").Append(script.Id).Append('|').Append(script.Name).Append('|').Append(script.Enabled ? 1 : 0).Append('|').Append(script.TargetRoleId).Append('|')
                .Append(script.PitchExpression).Append('|').Append(script.VelocityExpression).Append('|').Append(script.DurationExpression).Append('|').Append(script.GateExpression).Append('\n');
            foreach ((string key, double value) in script.Parameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                canonical.Append("script-param|").Append(script.Id).Append('|').Append(key).Append('|').Append(Format(value)).Append('\n');
        }
    }

    private static void AppendOrchestration(StringBuilder canonical, ProceduralOrchestrationDocument orchestration)
    {
        canonical.Append("orchestration|").Append(orchestration.Enabled ? 1 : 0).Append('|')
            .Append(orchestration.Catalog.SoundFontId).Append('|').Append(orchestration.Catalog.SourceFingerprint).Append('|')
            .Append(orchestration.GenerationSequence).Append('|').Append(orchestration.Randomization.Mode).Append('|')
            .Append(orchestration.Randomization.Exploration).Append('|').Append(orchestration.Randomization.Surprise).Append('|')
            .Append(orchestration.Randomization.SurpriseBudget).Append('|').Append(orchestration.Randomization.PermitSectionInstrumentChanges ? 1 : 0).Append('|')
            .Append(orchestration.Randomization.PermitLayering ? 1 : 0).Append('\n');
        CompositionGenome genome = orchestration.Genome;
        canonical.Append("genome|").Append(genome.Id).Append('|').Append(genome.EntropySeedHex).Append('|').Append(genome.Mode).Append('|')
            .Append(genome.SurpriseSpent).Append('|').Append(string.Join(",", genome.SourceStyleIds)).Append('|')
            .Append(genome.StructuralFamily).Append('|').Append(genome.HarmonicLanguage).Append('|')
            .Append(genome.RhythmicLanguage).Append('|').Append(genome.OrchestrationLanguage).Append('|')
            .Append(genome.EmotionalModifier).Append('|').Append(genome.PerformanceModifier).Append('|')
            .Append(genome.EnsembleArchetypeId).Append('|').Append(string.Join(",", genome.TraitIds)).Append('\n');
        foreach ((string key, int cost) in genome.SurpriseCosts.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            canonical.Append("surprise-cost|").Append(key).Append('|').Append(cost).Append('\n');
        foreach (InstrumentPresetDescriptor preset in orchestration.Catalog.Presets.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append("preset|").Append(preset.Key).Append('|').Append(preset.SoundFontId).Append('|').Append(preset.Bank).Append('|')
                .Append(preset.Program).Append('|').Append(preset.Name).Append('|').Append(preset.Family).Append('|')
                .Append(string.Join(",", preset.Tags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))).Append('|')
                .Append(preset.SuggestedLowNote).Append('|').Append(preset.SuggestedHighNote).Append('|')
                .Append(Format(preset.Timbre.Attack)).Append('|').Append(Format(preset.Timbre.Sustain)).Append('|')
                .Append(Format(preset.Timbre.Brightness)).Append('|').Append(Format(preset.Timbre.Density)).Append('|')
                .Append(Format(preset.Timbre.Percussiveness)).Append('|').Append(Format(preset.Timbre.PitchStability)).Append('|')
                .Append(preset.IsDrumKit ? 1 : 0).Append('|').Append(preset.IsEnabled ? 1 : 0).Append('|')
                .Append(preset.IsFavorite ? 1 : 0).Append('|').Append(preset.IsExcluded ? 1 : 0).Append('\n');
        }
        foreach (SectionOrchestrationPlan section in orchestration.SectionPlans.OrderBy(item => item.SectionId, StringComparer.Ordinal))
        {
            canonical.Append("orchestration-section|").Append(section.SectionId).Append('|').Append(Format(section.TargetEnergy)).Append('|')
                .Append(Format(section.EnsembleScore)).Append('\n');
            foreach (InstrumentAssignmentPlan assignment in section.Assignments.OrderBy(item => item.RoleId, StringComparer.Ordinal).ThenBy(item => item.LayerIndex))
                canonical.Append("assignment|").Append(section.SectionId).Append('|').Append(assignment.RoleId).Append('|').Append(assignment.LayerIndex).Append('|')
                    .Append(assignment.PresetKey).Append('|').Append(assignment.Bank).Append('|').Append(assignment.Program).Append('|')
                    .Append(assignment.MidiChannel).Append('|').Append(assignment.Active ? 1 : 0).Append('|').Append(assignment.OctaveOffset).Append('|')
                    .Append(Format(assignment.Volume)).Append('|').Append(Format(assignment.Pan)).Append('|').Append(assignment.Strategy).Append('|')
                    .Append(Format(assignment.SuitabilityScore)).Append('\n');
        }
        foreach (PresetUsageRecord usage in orchestration.UsageHistory.OrderBy(item => item.PresetKey, StringComparer.OrdinalIgnoreCase))
            canonical.Append("preset-usage|").Append(usage.PresetKey).Append('|').Append(usage.UseCount).Append('|').Append(usage.AuditionCount).Append('|')
                .Append(usage.PrimaryRoleCount).Append('|').Append(usage.LastUsedSequence).Append('\n');
    }


    private static void AppendReferenceAnalysis(StringBuilder canonical, ReferenceAnalysisDocument reference)
    {
        canonical.Append("reference|" ).Append(reference.Enabled ? 1 : 0).Append('|')
            .Append(reference.AnalysisRevision).Append('|').Append(reference.DescendantRevision).Append('|')
            .Append(reference.ActiveBlueprintId).Append('\n');
        ReferenceCompositionBlueprint blueprint = reference.ActiveBlueprint ?? new ReferenceCompositionBlueprint();
        canonical.Append("reference-blueprint|").Append(blueprint.Id).Append('|').Append(blueprint.Version).Append('|')
            .Append(blueprint.Source.SourceFingerprint).Append('|').Append(blueprint.Source.BarCount).Append('|')
            .Append(Format(blueprint.Source.PrimaryTempo)).Append('|').Append(blueprint.Generation.Mode).Append('|')
            .Append(blueprint.Generation.OriginalityPolicy).Append('|').Append(blueprint.Generation.InstrumentationPolicy).Append('\n');
        foreach (ReferenceSectionProfile section in blueprint.Form.Sections.OrderBy(item => item.StartBar))
            canonical.Append("reference-section|").Append(section.Id).Append('|').Append(section.FamilyId).Append('|').Append(section.Kind).Append('|')
                .Append(section.StartBar).Append('|').Append(section.LengthBars).Append('|').Append(Format(section.Energy)).Append('\n');
        foreach (ReferenceHarmonicEvent harmony in blueprint.Harmony.Events.OrderBy(item => item.StartBar).ThenBy(item => item.StartBeat))
            canonical.Append("reference-harmony|").Append(harmony.StartBar).Append('|').Append(Format(harmony.StartBeat)).Append('|')
                .Append(harmony.RootPitchClass).Append('|').Append(harmony.Quality).Append('|').Append(harmony.ScaleDegree).Append('|')
                .Append(harmony.Function).Append('|').Append(Format(harmony.Tension)).Append('\n');
        foreach (ReferenceMotifPattern motif in blueprint.Motifs.Motifs.OrderBy(item => item.Id, StringComparer.Ordinal))
            canonical.Append("reference-motif|").Append(motif.Id).Append('|').Append(string.Join(',', motif.Intervals)).Append('|')
                .Append(string.Join(',', motif.RhythmUnits)).Append('|').Append(string.Join(',', motif.Contour)).Append('|')
                .Append(Format(motif.Importance)).Append('|').Append(motif.Enabled ? 1 : 0).Append('\n');
        foreach ((ReferenceBlueprintDimension dimension, double value) in blueprint.Generation.Preservation.OrderBy(item => item.Key))
            canonical.Append("reference-preserve|").Append(dimension).Append('|').Append(Format(value)).Append('\n');
        foreach (ReferenceDescendantEvaluation evaluation in reference.RecentDescendants.OrderBy(item => item.CandidateId, StringComparer.Ordinal))
            canonical.Append("reference-candidate|").Append(evaluation.CandidateId).Append('|').Append(evaluation.Seed).Append('|')
                .Append(evaluation.Mode).Append('|').Append(Format(evaluation.QualityScore)).Append('|')
                .Append(Format(evaluation.NoveltyScore)).Append('|').Append(evaluation.PassedOriginality ? 1 : 0).Append('\n');
        canonical.Append("reference-corpus|").Append(reference.Corpus.Id).Append('|').Append(reference.Corpus.BlueprintIds.Count).Append('|')
            .Append(reference.Corpus.Clusters.Count).Append('|').Append(Format(reference.Corpus.MeanTempo)).Append('|')
            .Append(Format(reference.Corpus.MeanConfidence)).Append('\n');
    }

    private static string Format(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);
}
