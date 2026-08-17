using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlayromaMusic.Core.Analysis;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Midi.ReferenceAnalysis;

public sealed class ReferenceMidiAnalysisService
{
    public ReferenceCompositionBlueprint Analyze(string midiPath)
    {
        ProjectDocument project = new MidiImportService().Import(midiPath);
        return Analyze(project, Path.GetFullPath(midiPath));
    }

    public ReferenceCompositionBlueprint Analyze(ProjectDocument project, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        MidiStyleAnalysisReport style = new MidiStyleAnalysisService().Analyze(project);
        List<AbsoluteNote> notes = project.Song.EnumerateNotes().OrderBy(item => item.AbsoluteStartTick).ToList();
        int tpq = Math.Max(1, project.Song.TicksPerQuarterNote);
        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();
        long beatTicks = Math.Max(1, tpq * 4L / Math.Max(1, meter.Denominator));
        long barTicks = Math.Max(1, beatTicks * Math.Max(1, meter.Numerator));
        int bars = checked((int)Math.Max(1, (project.Song.LengthTicks + barTicks - 1) / barTicks));
        string fileName = string.IsNullOrWhiteSpace(sourcePath) ? project.Title : Path.GetFileName(sourcePath);
        string fingerprint = ComputeFingerprint(project, sourcePath);

        ReferenceCompositionBlueprint blueprint = new()
        {
            Id = "reference-" + fingerprint[..16],
            Name = Path.GetFileNameWithoutExtension(fileName) + " Blueprint",
            Source = new ReferenceSourceInfo
            {
                SourcePath = sourcePath,
                SourceFingerprint = fingerprint,
                FileName = fileName,
                Title = project.Title,
                TicksPerQuarterNote = tpq,
                TrackCount = project.Song.Tracks.Count,
                NoteCount = notes.Count,
                BarCount = bars,
                PrimaryTempo = project.Song.TempoMap.FirstOrDefault()?.BeatsPerMinute ?? 120,
                MeterNumerator = meter.Numerator,
                MeterDenominator = meter.Denominator,
                AnalyzedAtUtc = DateTimeOffset.UtcNow
            },
            StyleHints = style.StyleScores.OrderByDescending(pair => pair.Value).Take(4).Select(pair => pair.Key).ToList(),
            Generation = new ReferenceGenerationPolicy
            {
                Mode = ReferenceGenerationMode.InspiredBy,
                OriginalityPolicy = ReferenceOriginalityPolicy.AvoidDirectQuotation,
                TargetStyleId = style.SuggestedStyleId,
                CandidateCount = 12
            }
        };

        BuildForm(project, blueprint, notes, bars, barTicks);
        BuildInstrumentation(project, blueprint, tpq);
        BuildHarmony(project, blueprint, notes, bars, barTicks, beatTicks);
        BuildMotifs(project, blueprint, notes, tpq, barTicks);
        BuildRhythm(project, blueprint, notes, tpq, barTicks, bars);
        BuildArrangement(project, blueprint, notes, barTicks);
        BuildPerformance(project, blueprint, notes, tpq, bars);
        BuildTension(project, blueprint, notes, bars, barTicks);
        BuildSignature(blueprint, notes, tpq);

        double roleConfidence = blueprint.Instrumentation.Tracks.Select(item => item.RoleConfidence).DefaultIfEmpty(0).Average();
        double noteConfidence = Math.Clamp(notes.Count / 128d, 0.25, 1);
        blueprint.Confidence = new ReferenceAnalysisConfidence
        {
            Roles = roleConfidence,
            Form = blueprint.Form.BoundaryConfidence,
            Harmony = blueprint.Harmony.Confidence,
            Motifs = blueprint.Motifs.Confidence,
            Rhythm = blueprint.Rhythm.Confidence,
            Arrangement = blueprint.Arrangement.Confidence,
            Performance = blueprint.Performance.Confidence,
            Overall = Math.Clamp((roleConfidence + blueprint.Form.BoundaryConfidence + blueprint.Harmony.Confidence + blueprint.Motifs.Confidence + blueprint.Rhythm.Confidence + noteConfidence) / 6d, 0, 1)
        };
        blueprint.Observations.AddRange(style.Observations);
        blueprint.Observations.Add($"Analyzed {bars} bars in {meter.Numerator}/{meter.Denominator} at {blueprint.Source.PrimaryTempo:0.#} BPM.");
        ReferenceBlueprintEditor.Normalize(blueprint);
        return blueprint;
    }

    public ReferenceCompositionBlueprint AnalyzeAndAttach(ProjectDocument project, string midiPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ReferenceCompositionBlueprint blueprint = Analyze(midiPath);
        project.ReferenceAnalysis.Enabled = true;
        project.ReferenceAnalysis.AnalysisRevision++;
        project.ReferenceAnalysis.ActiveBlueprintId = blueprint.Id;
        project.ReferenceAnalysis.ActiveBlueprint = ReferenceBlueprintSerializer.Clone(blueprint);
        int existing = project.ReferenceAnalysis.SavedBlueprints.FindIndex(item => item.Id.Equals(blueprint.Id, StringComparison.Ordinal));
        if (existing >= 0) project.ReferenceAnalysis.SavedBlueprints[existing] = ReferenceBlueprintSerializer.Clone(blueprint);
        else project.ReferenceAnalysis.SavedBlueprints.Add(ReferenceBlueprintSerializer.Clone(blueprint));
        project.ReferenceAnalysis.Diagnostics.Add($"Analyzed reference MIDI: {midiPath}");
        return blueprint;
    }

    private static void BuildForm(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int bars, long barTicks)
    {
        List<SongSection> sections = project.Song.Sections.Count > 1
            ? project.Song.Sections
            : CreateAnalysisSections(bars, barTicks);
        int sectionIndex = 0;
        foreach (SongSection section in sections.OrderBy(item => item.StartTick))
        {
            long end = section.EndTick;
            List<AbsoluteNote> sectionNotes = notes.Where(item => item.AbsoluteStartTick >= section.StartTick && item.AbsoluteStartTick < end).ToList();
            string familyId = "family-" + NormalizeFamily(section.Name, sectionIndex);
            ReferenceSectionProfile profile = new()
            {
                Id = string.IsNullOrWhiteSpace(section.Id) ? $"section-{sectionIndex + 1:00}" : section.Id,
                Label = string.IsNullOrWhiteSpace(section.Name) ? $"Section {sectionIndex + 1}" : section.Name,
                FamilyId = familyId,
                Kind = InferSectionKind(section.Name, sectionIndex, sections.Count),
                StartBar = checked((int)(section.StartTick / barTicks)),
                LengthBars = checked((int)Math.Max(1, (section.LengthTicks + barTicks - 1) / barTicks)),
                VariationIndex = sectionIndex,
                Energy = section.Energy > 0 ? section.Energy : EstimateEnergy(sectionNotes),
                Density = sectionNotes.Count / (double)Math.Max(1, section.LengthTicks / Math.Max(1d, project.Song.TicksPerQuarterNote)),
                MeanPitch = sectionNotes.Select(item => item.Note.Pitch).DefaultIfEmpty(60).Average(),
                MeanVelocity = sectionNotes.Select(item => item.Note.Velocity).DefaultIfEmpty(80).Average(),
                BoundaryConfidence = project.Song.Sections.Count > 1 ? 0.92 : 0.62
            };
            blueprint.Form.Sections.Add(profile);
            sectionIndex++;
        }
        blueprint.Form.TotalBars = bars;
        blueprint.Form.BoundaryConfidence = project.Song.Sections.Count > 1 ? 0.92 : 0.62;
        blueprint.Form.RepetitionConfidence = CalculateSectionRepetition(blueprint.Form.Sections);
        blueprint.Form.Families = blueprint.Form.Sections.GroupBy(item => item.FamilyId).Select(group => new ReferenceSectionFamily
        {
            Id = group.Key,
            Label = group.First().Label,
            SectionIds = group.Select(item => item.Id).ToList(),
            InternalSimilarity = group.Count() > 1 ? 0.8 : 0.55
        }).ToList();
    }

    private static List<SongSection> CreateAnalysisSections(int bars, long barTicks)
    {
        int size = bars <= 8 ? 4 : 8;
        List<SongSection> result = [];
        int start = 0;
        int index = 0;
        while (start < bars)
        {
            int length = Math.Min(size, bars - start);
            result.Add(new SongSection
            {
                Id = $"analysis-section-{++index:00}",
                Name = index == 1 ? "Intro" : start + length >= bars ? "Outro" : $"Section {index}",
                StartTick = start * barTicks,
                LengthTicks = length * barTicks,
                Energy = 0.45 + 0.1 * (index % 3)
            });
            start += length;
        }
        return result;
    }

    private static void BuildInstrumentation(ProjectDocument project, ReferenceCompositionBlueprint blueprint, int tpq)
    {
        foreach (SongTrack track in project.Song.Tracks)
        {
            List<AbsoluteNote> notes = project.Song.EnumerateNotes().Where(item => ReferenceEquals(item.Track, track)).ToList();
            long songTicks = Math.Max(tpq, project.Song.LengthTicks);
            double polyphony = CalculateTrackPolyphony(notes);
            double chordRatio = notes.Count == 0 ? 0 : notes.GroupBy(item => item.AbsoluteStartTick).Count(group => group.Count() > 1) / (double)Math.Max(1, notes.GroupBy(item => item.AbsoluteStartTick).Count());
            TrackRole role = InferRole(track, notes, chordRatio);
            blueprint.Instrumentation.Tracks.Add(new ReferenceTrackProfile
            {
                TrackId = track.Id,
                TrackName = track.Name,
                MidiChannel = track.MidiChannel,
                Bank = track.MidiBank,
                Program = track.GeneralMidiProgram,
                PresetName = string.IsNullOrWhiteSpace(track.PresetKey) ? GeneralMidiName(track.GeneralMidiProgram, track.IsPercussion) : track.PresetKey,
                IsPercussion = track.IsPercussion,
                InferredRole = role,
                RoleConfidence = track.IsPercussion || role == track.Role ? 0.92 : 0.72,
                NoteCount = notes.Count,
                AveragePitch = notes.Select(item => item.Note.Pitch).DefaultIfEmpty(60).Average(),
                MinimumPitch = notes.Select(item => item.Note.Pitch).DefaultIfEmpty(0).Min(),
                MaximumPitch = notes.Select(item => item.Note.Pitch).DefaultIfEmpty(127).Max(),
                NotesPerBeat = notes.Count / Math.Max(1d, songTicks / (double)tpq),
                Polyphony = polyphony,
                MonophonicRatio = Math.Clamp(1 - Math.Max(0, polyphony - 1) / 4, 0, 1),
                ChordOnsetRatio = chordRatio,
                Repetition = CalculateTrackRepetition(notes)
            });
        }
        blueprint.Instrumentation.MeanEnsembleSize = project.Song.Tracks.Count(track => !track.Muted);
        blueprint.Instrumentation.UsesSectionProgramChanges = project.Song.Tracks.SelectMany(track => track.Clips).Any(clip => clip.ProgramChanges.Count > 1);
        blueprint.Instrumentation.Confidence = project.Song.Tracks.Count == 0 ? 0 : 0.88;
    }

    private static void BuildHarmony(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int bars, long barTicks, long beatTicks)
    {
        (PitchClass root, ScaleKind scale, double confidence) = InferKey(notes);
        blueprint.Harmony.GlobalKeyRoot = root;
        blueprint.Harmony.GlobalScale = scale;
        blueprint.Harmony.KeyRegions.Add(new ReferenceKeyRegion { StartBar = 0, LengthBars = bars, Root = root, Scale = scale, Confidence = confidence });
        for (int bar = 0; bar < bars; bar++)
        {
            long start = bar * barTicks;
            long end = start + barTicks;
            List<AbsoluteNote> barNotes = notes.Where(item => item.AbsoluteStartTick < end && item.AbsoluteEndTick > start).ToList();
            List<int> pcs = barNotes.GroupBy(item => item.Note.Pitch % 12).OrderByDescending(group => group.Count()).Select(group => group.Key).Take(5).Order().ToList();
            int chordRoot = InferChordRoot(pcs, (int)root);
            ReferenceChordQuality quality = InferChordQuality(pcs, chordRoot);
            int degree = PitchClassUtilities.Normalize(chordRoot - (int)root);
            blueprint.Harmony.Events.Add(new ReferenceHarmonicEvent
            {
                Id = $"harmony-{bar + 1:000}",
                StartBar = bar,
                StartBeat = 0,
                LengthBeats = project.Song.MeterMap.FirstOrDefault()?.Numerator ?? 4,
                RootPitchClass = chordRoot,
                Quality = quality,
                ScaleDegree = degree,
                Function = InferFunction(degree),
                Cadence = bar == bars - 1 ? CadenceKind.Authentic : CadenceKind.None,
                InversionBassPitchClass = barNotes.OrderBy(item => item.Note.Pitch).Select(item => item.Note.Pitch % 12).DefaultIfEmpty(-1).First(),
                PitchClasses = pcs,
                RomanNumeral = RomanNumeral(degree, quality),
                Tension = quality is ReferenceChordQuality.Diminished or ReferenceChordQuality.Augmented or ReferenceChordQuality.Cluster ? 0.8 : degree is 1 or 6 or 10 ? 0.6 : 0.35,
                Confidence = barNotes.Count == 0 ? 0.2 : Math.Clamp(0.45 + pcs.Count * 0.08, 0, 0.9)
            });
        }
        blueprint.Harmony.CadenceBars = blueprint.Form.Sections.Select(section => section.StartBar + section.LengthBars - 1).Distinct().Where(bar => bar >= 0).ToList();
        blueprint.Harmony.MeanChangesPerBar = 1;
        ScaleDefinition definition = ScaleLibrary.Get(scale);
        blueprint.Harmony.Chromaticism = notes.Count == 0 ? 0 : notes.Count(item => !definition.ContainsPitchClass(root, item.Note.Pitch % 12)) / (double)notes.Count;
        blueprint.Harmony.Confidence = confidence;
    }

    private static void BuildMotifs(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int tpq, long barTicks)
    {
        SongTrack? sourceTrack = project.Song.Tracks.FirstOrDefault(track => track.Role is TrackRole.Lead or TrackRole.CounterMelody)
            ?? project.Song.Tracks.OrderByDescending(track => track.NoteCount).FirstOrDefault();
        if (sourceTrack is null) { blueprint.Motifs.Confidence = 0.1; return; }
        List<AbsoluteNote> sourceNotes = notes.Where(item => ReferenceEquals(item.Track, sourceTrack)).OrderBy(item => item.AbsoluteStartTick).Take(16).ToList();
        if (sourceNotes.Count < 2) { blueprint.Motifs.Confidence = 0.2; return; }
        List<int> intervals = sourceNotes.Zip(sourceNotes.Skip(1), (left, right) => Math.Clamp(right.Note.Pitch - left.Note.Pitch, -24, 24)).ToList();
        List<int> rhythms = sourceNotes.Select(item => checked((int)Math.Max(1, Math.Round(item.Note.DurationTicks / (double)Math.Max(1, tpq / 4))))).ToList();
        List<int> contour = intervals.Select(interval => Math.Sign(interval)).ToList();
        ReferenceMotifPattern motif = new()
        {
            Id = "motif-01",
            Name = sourceTrack.Name + " Motif",
            SourceTrackId = sourceTrack.Id,
            SourceRole = sourceTrack.Role,
            Intervals = intervals,
            RhythmUnits = rhythms,
            Contour = contour,
            ScaleDegrees = sourceNotes.Select(item => PitchClassUtilities.Normalize(item.Note.Pitch - (int)project.Song.KeyRoot)).ToList(),
            Importance = 0.85,
            Confidence = Math.Clamp(sourceNotes.Count / 12d, 0.4, 0.95),
            Occurrences =
            [
                new ReferenceMotifOccurrence
                {
                    StartBar = checked((int)(sourceNotes[0].AbsoluteStartTick / barTicks)),
                    StartBeat = sourceNotes[0].AbsoluteStartTick % barTicks / (double)tpq,
                    Similarity = 1
                }
            ]
        };
        blueprint.Motifs.Motifs.Add(motif);
        blueprint.Motifs.PreferredIntervals = intervals.GroupBy(value => value).OrderByDescending(group => group.Count()).Take(8).Select(group => group.Key).ToList();
        blueprint.Motifs.PreferredRhythmUnits = rhythms.GroupBy(value => value).OrderByDescending(group => group.Count()).Take(8).Select(group => group.Key).ToList();
        blueprint.Motifs.MeanPhraseLengthBeats = Math.Max(1, (sourceNotes[^1].AbsoluteEndTick - sourceNotes[0].AbsoluteStartTick) / (double)tpq);
        blueprint.Motifs.Confidence = motif.Confidence;
    }

    private static void BuildRhythm(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int tpq, long barTicks, int bars)
    {
        blueprint.Rhythm.StepsPerBar = 16;
        double beatCount = Math.Max(1, project.Song.LengthTicks / (double)tpq);
        blueprint.Rhythm.MeanNotesPerBeat = notes.Count / beatCount;
        blueprint.Rhythm.Syncopation = notes.Count == 0 ? 0 : notes.Count(item => IsSyncopated(item.AbsoluteStartTick, tpq)) / (double)notes.Count;
        blueprint.Rhythm.CommonDurationUnits = notes.Select(item => checked((int)Math.Max(1, Math.Round(item.Note.DurationTicks / (double)Math.Max(1, tpq / 4)))))
            .GroupBy(value => value).OrderByDescending(group => group.Count()).Take(8).Select(group => group.Key).ToList();
        foreach (ReferenceTrackProfile profile in blueprint.Instrumentation.Tracks)
        {
            SongTrack? track = project.Song.Tracks.FirstOrDefault(item => item.Id == profile.TrackId);
            if (track is null) continue;
            List<AbsoluteNote> trackNotes = notes.Where(item => ReferenceEquals(item.Track, track)).ToList();
            double[] onset = new double[16];
            double[] accents = new double[16];
            int[] counts = new int[16];
            foreach (AbsoluteNote note in trackNotes)
            {
                int step = checked((int)Math.Clamp((note.AbsoluteStartTick % barTicks) * 16 / Math.Max(1, barTicks), 0, 15));
                counts[step]++;
                accents[step] += note.Note.Velocity / 127d;
            }
            int denominator = Math.Max(1, bars);
            for (int index = 0; index < 16; index++)
            {
                onset[index] = Math.Clamp(counts[index] / (double)denominator, 0, 1);
                accents[index] = counts[index] == 0 ? 0 : accents[index] / counts[index];
            }
            blueprint.Rhythm.RoleGrooves.Add(new ReferenceRoleGroove
            {
                Role = profile.InferredRole,
                SourceTrackId = profile.TrackId,
                OnsetProbabilities = onset.ToList(),
                AccentProfile = accents.ToList(),
                RepresentativePattern = counts.Select(count => count > 0 ? 1 : 0).ToList(),
                Density = trackNotes.Count / Math.Max(1d, bars * 16),
                Syncopation = trackNotes.Count == 0 ? 0 : trackNotes.Count(item => IsSyncopated(item.AbsoluteStartTick, tpq)) / (double)trackNotes.Count
            });
        }
        blueprint.Rhythm.FillBars = Enumerable.Range(0, bars).Where(bar =>
        {
            int count = notes.Count(item => item.AbsoluteStartTick >= bar * barTicks && item.AbsoluteStartTick < (bar + 1) * barTicks);
            double average = notes.Count / (double)Math.Max(1, bars);
            return count > average * 1.5;
        }).ToList();
        blueprint.Rhythm.Confidence = notes.Count == 0 ? 0.1 : Math.Clamp(0.55 + Math.Log10(notes.Count + 1) * 0.12, 0, 0.95);
    }

    private static void BuildArrangement(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, long barTicks)
    {
        foreach (ReferenceSectionProfile section in blueprint.Form.Sections)
        {
            long start = section.StartBar * barTicks;
            long end = (section.StartBar + section.LengthBars) * barTicks;
            List<AbsoluteNote> sectionNotes = notes.Where(item => item.AbsoluteStartTick < end && item.AbsoluteEndTick > start).ToList();
            ReferenceSectionArrangement arrangement = new()
            {
                SectionId = section.Id,
                StartBar = section.StartBar,
                Density = section.Density,
                RegisterCenter = sectionNotes.Select(item => item.Note.Pitch).DefaultIfEmpty(60).Average(),
                RegisterSpan = sectionNotes.Count == 0 ? 0 : sectionNotes.Max(item => item.Note.Pitch) - sectionNotes.Min(item => item.Note.Pitch)
            };
            foreach (ReferenceTrackProfile track in blueprint.Instrumentation.Tracks)
            {
                List<AbsoluteNote> active = sectionNotes.Where(item => item.Track.Id == track.TrackId).ToList();
                arrangement.Roles.Add(new ReferenceRoleActivity
                {
                    Role = track.InferredRole,
                    SourceTrackId = track.TrackId,
                    Active = active.Count > 0,
                    Intensity = active.Count == 0 ? 0 : Math.Clamp(active.Count / Math.Max(1d, section.LengthBars * 8), 0, 1),
                    RegisterLow = active.Select(item => item.Note.Pitch).DefaultIfEmpty(track.MinimumPitch).Min(),
                    RegisterHigh = active.Select(item => item.Note.Pitch).DefaultIfEmpty(track.MaximumPitch).Max()
                });
            }
            blueprint.Arrangement.Sections.Add(arrangement);
        }
        blueprint.Arrangement.MeanActiveRoles = blueprint.Arrangement.Sections.Select(section => section.Roles.Count(role => role.Active)).DefaultIfEmpty(0).Average();
        List<double> densities = blueprint.Arrangement.Sections.Select(section => section.Density).ToList();
        blueprint.Arrangement.SectionContrast = densities.Count < 2 ? 0 : Math.Clamp(StandardDeviation(densities) / Math.Max(0.1, densities.Average()), 0, 1);
        blueprint.Arrangement.Confidence = blueprint.Arrangement.Sections.Count == 0 ? 0.2 : 0.82;
    }

    private static void BuildPerformance(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int tpq, int bars)
    {
        List<double> velocities = notes.Select(item => (double)item.Note.Velocity).ToList();
        blueprint.Performance.MeanVelocity = velocities.DefaultIfEmpty(80).Average();
        blueprint.Performance.VelocityDeviation = StandardDeviation(velocities);
        blueprint.Performance.MeanDurationRatio = notes.Select(item => item.Note.DurationTicks / (double)tpq).DefaultIfEmpty(0.82).Average();
        blueprint.Performance.TimingDeviationTicks = notes.Select(item => DistanceToGrid(item.AbsoluteStartTick, Math.Max(1, tpq / 4))).DefaultIfEmpty(0).Average();
        blueprint.Performance.LegatoRatio = notes.Count == 0 ? 0 : notes.Count(item => item.Note.DurationTicks >= tpq) / (double)notes.Count;
        blueprint.Performance.StaccatoRatio = notes.Count == 0 ? 0 : notes.Count(item => item.Note.DurationTicks <= tpq / 4) / (double)notes.Count;
        List<AbsoluteControlChange> controls = project.Song.EnumerateControlChanges().ToList();
        blueprint.Performance.SustainPedalUsage = controls.Count == 0 ? 0 : controls.Count(item => item.ControlChange.ControllerNumber == 64 && item.ControlChange.Value >= 64) / (double)Math.Max(1, controls.Count);
        blueprint.Performance.Controllers = controls.GroupBy(item => item.ControlChange.ControllerNumber).Select(group => new ReferenceControllerProfile
        {
            ControllerNumber = group.Key,
            MeanValue = group.Average(item => item.ControlChange.Value),
            ChangeRatePerBar = group.Count() / (double)Math.Max(1, bars)
        }).ToList();
        foreach (ReferenceTrackProfile profile in blueprint.Instrumentation.Tracks)
        {
            List<AbsoluteNote> roleNotes = notes.Where(item => item.Track.Id == profile.TrackId).ToList();
            List<double> roleVelocities = roleNotes.Select(item => (double)item.Note.Velocity).ToList();
            blueprint.Performance.Roles.Add(new ReferenceRolePerformance
            {
                Role = profile.InferredRole,
                MeanVelocity = roleVelocities.DefaultIfEmpty(80).Average(),
                VelocityDeviation = StandardDeviation(roleVelocities),
                MeanDurationRatio = roleNotes.Select(item => item.Note.DurationTicks / (double)tpq).DefaultIfEmpty(0.82).Average(),
                TimingDeviationTicks = roleNotes.Select(item => DistanceToGrid(item.AbsoluteStartTick, Math.Max(1, tpq / 4))).DefaultIfEmpty(0).Average()
            });
        }
        blueprint.Performance.Confidence = notes.Count == 0 ? 0.1 : 0.84;
    }

    private static void BuildTension(ProjectDocument project, ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int bars, long barTicks)
    {
        double max = 0;
        int climax = 0;
        for (int bar = 0; bar < bars; bar++)
        {
            List<AbsoluteNote> barNotes = notes.Where(item => item.AbsoluteStartTick >= bar * barTicks && item.AbsoluteStartTick < (bar + 1) * barTicks).ToList();
            double density = Math.Clamp(barNotes.Count / 24d, 0, 1);
            double register = Math.Clamp((barNotes.Select(item => item.Note.Pitch).DefaultIfEmpty(60).Average() - 42) / 48d, 0, 1);
            double velocity = Math.Clamp(barNotes.Select(item => item.Note.Velocity).DefaultIfEmpty(64).Average() / 127d, 0, 1);
            ReferenceHarmonicEvent? harmony = blueprint.Harmony.Events.FirstOrDefault(item => item.StartBar == bar);
            double harmonic = harmony?.Tension ?? 0.3;
            double tension = Math.Clamp(density * 0.32 + register * 0.18 + velocity * 0.25 + harmonic * 0.25, 0, 1);
            blueprint.Tension.Points.Add(new ReferenceTensionPoint
            {
                Bar = bar,
                Tension = tension,
                DensityContribution = density,
                RegisterContribution = register,
                VelocityContribution = velocity,
                HarmonicContribution = harmonic
            });
            if (tension > max) { max = tension; climax = bar; }
        }
        blueprint.Tension.ClimaxBar = climax;
        blueprint.Tension.ClimaxStrength = max;
        blueprint.Tension.ResolutionStrength = blueprint.Tension.Points.Count < 2 ? 0 : Math.Clamp(max - blueprint.Tension.Points[^1].Tension, 0, 1);
        blueprint.Tension.Confidence = notes.Count == 0 ? 0.2 : 0.78;
    }

    private static void BuildSignature(ReferenceCompositionBlueprint blueprint, List<AbsoluteNote> notes, int tpq)
    {
        int quantum = Math.Max(1, tpq / 8);
        blueprint.OriginalitySignature.QuantizationTicks = quantum;
        blueprint.OriginalitySignature.ExactNoteTokens = notes.Take(2048).Select(item => $"{item.Note.Pitch}:{item.AbsoluteStartTick / quantum}:{Math.Max(1, item.Note.DurationTicks / quantum)}").Distinct().ToList();
        List<int> pitches = notes.Select(item => item.Note.Pitch).ToList();
        List<int> intervals = pitches.Zip(pitches.Skip(1), (left, right) => right - left).ToList();
        blueprint.OriginalitySignature.IntervalNGrams = NGrams(intervals, 4).Take(1024).ToList();
        List<int> rhythms = notes.Select(item => checked((int)Math.Max(1, item.Note.DurationTicks / quantum))).ToList();
        blueprint.OriginalitySignature.RhythmNGrams = NGrams(rhythms, 4).Take(1024).ToList();
        blueprint.OriginalitySignature.ChordTokens = blueprint.Harmony.Events.Select(item => $"{item.RootPitchClass}:{item.Quality}:{item.LengthBeats:0.##}").ToList();
        blueprint.OriginalitySignature.MotifContourTokens = blueprint.Motifs.Motifs.Select(item => string.Join(',', item.Contour)).ToList();
    }

    private static IEnumerable<string> NGrams(List<int> values, int size)
    {
        for (int index = 0; index <= values.Count - size; index++) yield return string.Join(',', values.Skip(index).Take(size));
    }

    private static (PitchClass Root, ScaleKind Scale, double Confidence) InferKey(List<AbsoluteNote> notes)
    {
        if (notes.Count == 0) return (PitchClass.C, ScaleKind.Major, 0.2);
        int[] counts = new int[12];
        foreach (AbsoluteNote note in notes) counts[note.Note.Pitch % 12] += Math.Max(1, note.Note.Velocity / 16);
        (PitchClass Root, ScaleKind Scale, double Score) best = (PitchClass.C, ScaleKind.Major, double.MinValue);
        foreach (PitchClass root in Enum.GetValues<PitchClass>())
        {
            foreach (ScaleKind scale in new[] { ScaleKind.Major, ScaleKind.NaturalMinor, ScaleKind.Dorian, ScaleKind.Mixolydian, ScaleKind.HarmonicMinor })
            {
                ScaleDefinition definition = ScaleLibrary.Get(scale);
                double score = 0;
                for (int pc = 0; pc < 12; pc++) score += counts[pc] * (definition.ContainsPitchClass(root, pc) ? 1 : -0.65);
                if (score > best.Score) best = (root, scale, score);
            }
        }
        int total = counts.Sum();
        double confidence = total == 0 ? 0.2 : Math.Clamp(0.45 + best.Score / Math.Max(1, total) * 0.35, 0.25, 0.95);
        return (best.Root, best.Scale, confidence);
    }

    private static int InferChordRoot(List<int> pitchClasses, int fallback)
    {
        if (pitchClasses.Count == 0) return fallback;
        int bestRoot = pitchClasses[0];
        int bestScore = int.MinValue;
        foreach (int root in pitchClasses)
        {
            int score = 0;
            foreach (int pc in pitchClasses)
            {
                int interval = PitchClassUtilities.Normalize(pc - root);
                score += interval switch { 0 => 5, 3 or 4 => 4, 7 => 4, 10 or 11 => 2, _ => 0 };
            }
            if (score > bestScore) { bestScore = score; bestRoot = root; }
        }
        return bestRoot;
    }

    private static ReferenceChordQuality InferChordQuality(List<int> pcs, int root)
    {
        HashSet<int> intervals = pcs.Select(pc => PitchClassUtilities.Normalize(pc - root)).ToHashSet();
        if (intervals.Contains(4) && intervals.Contains(7) && intervals.Contains(11)) return ReferenceChordQuality.MajorSeventh;
        if (intervals.Contains(4) && intervals.Contains(7) && intervals.Contains(10)) return ReferenceChordQuality.DominantSeventh;
        if (intervals.Contains(3) && intervals.Contains(7) && intervals.Contains(10)) return ReferenceChordQuality.MinorSeventh;
        if (intervals.Contains(4) && intervals.Contains(7)) return ReferenceChordQuality.Major;
        if (intervals.Contains(3) && intervals.Contains(7)) return ReferenceChordQuality.Minor;
        if (intervals.Contains(3) && intervals.Contains(6)) return ReferenceChordQuality.Diminished;
        if (intervals.Contains(4) && intervals.Contains(8)) return ReferenceChordQuality.Augmented;
        if (intervals.Contains(5) && intervals.Contains(7)) return ReferenceChordQuality.SuspendedFourth;
        if (intervals.Contains(2) && intervals.Contains(7)) return ReferenceChordQuality.SuspendedSecond;
        if (intervals.Contains(7)) return ReferenceChordQuality.Power;
        return pcs.Count >= 4 ? ReferenceChordQuality.Cluster : ReferenceChordQuality.Unknown;
    }

    private static HarmonicFunction InferFunction(int degree) => degree switch
    {
        0 or 3 or 8 => HarmonicFunction.Tonic,
        2 or 5 or 9 => HarmonicFunction.Predominant,
        7 or 11 => HarmonicFunction.Dominant,
        _ => HarmonicFunction.Modal
    };

    private static string RomanNumeral(int degree, ReferenceChordQuality quality)
    {
        string[] names = ["I", "bII", "II", "bIII", "III", "IV", "#IV", "V", "bVI", "VI", "bVII", "VII"];
        string value = names[PitchClassUtilities.Normalize(degree)];
        return quality is ReferenceChordQuality.Minor or ReferenceChordQuality.MinorSeventh or ReferenceChordQuality.Diminished
            ? value.ToLowerInvariant()
            : value;
    }

    private static TrackRole InferRole(SongTrack track, List<AbsoluteNote> notes, double chordRatio)
    {
        if (track.IsPercussion || track.MidiChannel == 9) return TrackRole.Drums;
        double pitch = notes.Select(item => item.Note.Pitch).DefaultIfEmpty(60).Average();
        if (pitch < 48 || track.GeneralMidiProgram is >= 32 and <= 39) return TrackRole.Bass;
        if (chordRatio > 0.35) return TrackRole.Chords;
        if (track.GeneralMidiProgram is >= 88 and <= 103) return TrackRole.Texture;
        if (pitch > 70) return TrackRole.Lead;
        if (track.Role != default) return track.Role;
        return TrackRole.Harmony;
    }

    private static string GeneralMidiName(int program, bool percussion) => percussion ? "Percussion" : $"GM {Math.Clamp(program, 0, 127) + 1:000}";
    private static bool IsSyncopated(long tick, int tpq)
    {
        double position = tick / (double)Math.Max(1, tpq);
        double fraction = position - Math.Floor(position);
        return fraction is > 0.20 and < 0.80 && Math.Abs(fraction - 0.5) > 0.08;
    }
    private static double DistanceToGrid(long tick, int grid)
    {
        long remainder = tick % grid;
        return Math.Min(remainder, grid - remainder);
    }
    private static double CalculateTrackPolyphony(List<AbsoluteNote> notes)
    {
        if (notes.Count == 0) return 0;
        return notes.GroupBy(item => item.AbsoluteStartTick).Average(group => group.Count());
    }
    private static double CalculateTrackRepetition(List<AbsoluteNote> notes)
    {
        if (notes.Count < 8) return 0;
        List<string> tokens = [];
        for (int index = 0; index <= notes.Count - 4; index++) tokens.Add(string.Join(',', notes.Skip(index).Take(4).Select(item => item.Note.Pitch % 12)));
        return Math.Clamp(1 - tokens.Distinct().Count() / (double)Math.Max(1, tokens.Count), 0, 1);
    }
    private static double EstimateEnergy(List<AbsoluteNote> notes) => notes.Count == 0 ? 0.35 : Math.Clamp(notes.Average(item => item.Note.Velocity) / 127d * 0.65 + Math.Min(1, notes.Count / 64d) * 0.35, 0, 1);
    private static double CalculateSectionRepetition(List<ReferenceSectionProfile> sections)
    {
        if (sections.Count < 2) return 0.35;
        int repeats = sections.GroupBy(item => item.FamilyId).Sum(group => Math.Max(0, group.Count() - 1));
        return Math.Clamp(0.4 + repeats / (double)sections.Count * 0.5, 0, 1);
    }
    private static string NormalizeFamily(string name, int index)
    {
        string normalized = new string((name ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return normalized.Length == 0 ? index.ToString("00") : normalized.Replace("intro", "a").Replace("outro", "z");
    }
    private static FormSectionKind InferSectionKind(string name, int index, int count)
    {
        string lower = (name ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("intro")) return FormSectionKind.Intro;
        if (lower.Contains("chorus") || lower.Contains("refrain")) return FormSectionKind.Chorus;
        if (lower.Contains("bridge")) return FormSectionKind.Bridge;
        if (lower.Contains("break")) return FormSectionKind.Breakdown;
        if (lower.Contains("solo")) return FormSectionKind.Solo;
        if (lower.Contains("outro") || index == count - 1) return FormSectionKind.Outro;
        return FormSectionKind.Verse;
    }
    private static double StandardDeviation(IEnumerable<double> source)
    {
        List<double> values = source.ToList();
        if (values.Count < 2) return 0;
        double mean = values.Average();
        return Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Count);
    }
    private static string ComputeFingerprint(ProjectDocument project, string sourcePath)
    {
        string material = ProjectSerializer.ToJson(project, false) + "|" + sourcePath;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}

public sealed class ReferenceCorpusAnalysisResult
{
    public ReferenceCorpusProfile Corpus { get; set; } = new();
    public List<ReferenceCompositionBlueprint> Blueprints { get; set; } = [];
    public List<string> Diagnostics { get; set; } = [];
}

public sealed class ReferenceCorpusAnalysisService
{
    public ReferenceCorpusAnalysisResult Analyze(ReferenceCorpusAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReferenceMidiAnalysisService analyzer = new();
        ReferenceCorpusAnalysisResult result = new();
        foreach (string path in request.MidiPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ReferenceCompositionBlueprint blueprint = analyzer.Analyze(path);
                if (!request.IgnoreLowConfidenceBlueprints || blueprint.Confidence.Overall >= request.MinimumConfidence)
                {
                    result.Blueprints.Add(blueprint);
                }
                else result.Diagnostics.Add($"Skipped low-confidence reference: {path}");
            }
            catch (Exception exception)
            {
                result.Diagnostics.Add($"Could not analyze '{path}': {exception.Message}");
            }
        }
        result.Corpus = AnalyzeBlueprints(request.Name, result.Blueprints, request.ClusterCount);
        return result;
    }

    public ReferenceCorpusProfile AnalyzeBlueprints(string name, IReadOnlyList<ReferenceCompositionBlueprint> blueprints, int clusterCount)
    {
        ArgumentNullException.ThrowIfNull(blueprints);
        ReferenceCorpusProfile corpus = new()
        {
            Id = Slug(name) + "-" + blueprints.Count,
            Name = string.IsNullOrWhiteSpace(name) ? "Reference Corpus" : name,
            BlueprintIds = blueprints.Select(item => item.Id).Distinct().ToList(),
            MeanTempo = blueprints.Select(item => item.Source.PrimaryTempo).DefaultIfEmpty(120).Average(),
            MeanBars = blueprints.Select(item => (double)item.Source.BarCount).DefaultIfEmpty(0).Average(),
            MeanConfidence = blueprints.Select(item => item.Confidence.Overall).DefaultIfEmpty(0).Average()
        };
        corpus.StyleWeights = NormalizeWeights(blueprints.SelectMany(item => item.StyleHints.Select((style, index) => new KeyValuePair<string, double>(style, 1d / (index + 1)))));
        corpus.FormWeights = NormalizeWeights(blueprints.SelectMany(item => item.Form.Sections.Select(section => new KeyValuePair<string, double>(section.Kind.ToString(), section.LengthBars))));
        corpus.RoleWeights = NormalizeWeights(blueprints.SelectMany(item => item.Instrumentation.Tracks.Select(track => new KeyValuePair<string, double>(track.InferredRole.ToString(), Math.Max(1, track.NoteCount)))));
        corpus.HarmonicTransitionWeights = NormalizeWeights(blueprints.SelectMany(BlueprintTransitions));
        corpus.RhythmCellWeights = NormalizeWeights(blueprints.SelectMany(item => item.Rhythm.RoleGrooves.Select(groove => new KeyValuePair<string, double>(string.Join("", groove.RepresentativePattern), Math.Max(0.1, groove.Density)))));

        int desired = Math.Clamp(clusterCount, 1, Math.Max(1, blueprints.Count));
        IEnumerable<IGrouping<string, ReferenceCompositionBlueprint>> groups = blueprints.GroupBy(item => item.StyleHints.FirstOrDefault() ?? "unclassified", StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (IGrouping<string, ReferenceCompositionBlueprint> group in groups.Take(desired))
        {
            List<ReferenceCompositionBlueprint> members = group.ToList();
            corpus.Clusters.Add(new ReferenceCorpusStyleCluster
            {
                Id = $"cluster-{++index:00}",
                Name = group.Key + " Cluster",
                BlueprintIds = members.Select(item => item.Id).ToList(),
                SuggestedStyleId = group.Key,
                Cohesion = Math.Clamp(members.Select(item => item.Confidence.Overall).DefaultIfEmpty(0.5).Average(), 0, 1)
            });
        }
        if (corpus.Clusters.Count == 0 && blueprints.Count > 0)
        {
            corpus.Clusters.Add(new ReferenceCorpusStyleCluster
            {
                Id = "cluster-01",
                Name = "Reference Cluster",
                BlueprintIds = blueprints.Select(item => item.Id).ToList(),
                SuggestedStyleId = blueprints[0].StyleHints.FirstOrDefault() ?? "pop-modern",
                Cohesion = corpus.MeanConfidence
            });
        }
        return corpus;
    }

    public void Attach(ProjectDocument project, ReferenceCorpusAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);
        project.ReferenceAnalysis.Enabled = true;
        project.ReferenceAnalysis.Corpus = result.Corpus;
        foreach (ReferenceCompositionBlueprint blueprint in result.Blueprints)
        {
            int existing = project.ReferenceAnalysis.SavedBlueprints.FindIndex(item => item.Id == blueprint.Id);
            if (existing >= 0) project.ReferenceAnalysis.SavedBlueprints[existing] = ReferenceBlueprintSerializer.Clone(blueprint);
            else project.ReferenceAnalysis.SavedBlueprints.Add(ReferenceBlueprintSerializer.Clone(blueprint));
        }
        project.ReferenceAnalysis.Diagnostics.AddRange(result.Diagnostics);
        project.ReferenceAnalysis.AnalysisRevision++;
    }

    private static IEnumerable<KeyValuePair<string, double>> BlueprintTransitions(ReferenceCompositionBlueprint blueprint)
    {
        List<ReferenceHarmonicEvent> events = blueprint.Harmony.Events.OrderBy(item => item.StartBar).ThenBy(item => item.StartBeat).ToList();
        for (int index = 0; index + 1 < events.Count; index++)
        {
            yield return new KeyValuePair<string, double>($"{events[index].RomanNumeral}>{events[index + 1].RomanNumeral}", 1);
        }
    }
    private static Dictionary<string, double> NormalizeWeights(IEnumerable<KeyValuePair<string, double>> values)
    {
        Dictionary<string, double> totals = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, double value) in values)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            totals[key] = totals.GetValueOrDefault(key) + Math.Max(0, value);
        }
        double sum = totals.Values.Sum();
        if (sum > 0)
        {
            foreach (string key in totals.Keys.ToList()) totals[key] /= sum;
        }
        return totals;
    }
    private static string Slug(string text)
    {
        string result = new string((text ?? string.Empty).ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        while (result.Contains("--", StringComparison.Ordinal)) result = result.Replace("--", "-", StringComparison.Ordinal);
        return result.Trim('-').Length == 0 ? "reference-corpus" : result.Trim('-');
    }
}

public static class ReferenceCorpusStyleExporter
{
    public static string ExportStylePackJson(ReferenceCorpusProfile corpus, IReadOnlyList<ReferenceCompositionBlueprint> blueprints, string styleId)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(blueprints);
        string id = Slug(styleId);
        int minimumTempo = checked((int)Math.Clamp(Math.Round(blueprints.Select(item => item.Source.PrimaryTempo).DefaultIfEmpty(corpus.MeanTempo).Min() * 0.9), 30, 300));
        int maximumTempo = checked((int)Math.Clamp(Math.Round(blueprints.Select(item => item.Source.PrimaryTempo).DefaultIfEmpty(corpus.MeanTempo).Max() * 1.1), minimumTempo, 360));
        ReferenceCompositionBlueprint? representative = blueprints.OrderByDescending(item => item.Confidence.Overall).FirstOrDefault();
        List<string> production = representative?.Form.Sections.Select(SectionToken).ToList() ?? ["I", "A", "B", "A", "O"];
        if (production.Count == 0) production = ["I", "A", "B", "O"];
        List<object> roles = BuildRoles(blueprints);
        if (roles.Count == 0)
        {
            roles.Add(RoleObject("melody", "Reference Lead", "lead", 0, 0, false, 55, 91, 68));
            roles.Add(RoleObject("bass", "Reference Bass", "bass", 2, 32, false, 28, 55, 60));
            roles.Add(RoleObject("drums", "Reference Drums", "drums", 9, 0, true, 35, 81, 64));
        }
        Dictionary<string, object> progressions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string kind in new[] { "intro", "verse", "chorus", "bridge", "breakdown", "solo", "outro" })
        {
            List<int> degrees = representative?.Harmony.Events.Take(8).Select(item => Math.Clamp(item.ScaleDegree, 0, 11)).ToList() ?? [0, 5, 3, 4];
            if (degrees.Count == 0) degrees = [0, 5, 3, 4];
            progressions[kind] = new[] { degrees };
        }
        var document = new
        {
            schemaVersion = 1,
            id,
            name = string.IsNullOrWhiteSpace(corpus.Name) ? "Analyzed Reference Style" : corpus.Name,
            description = $"Personal style pack learned from {blueprints.Count} analyzed MIDI reference files.",
            version = "1.0.0",
            family = "hybrid",
            @abstract = false,
            tempo = new { minimum = minimumTempo, maximum = maximumTempo },
            meter = new
            {
                beatsPerBar = representative?.Source.MeterNumerator ?? 4,
                beatUnit = representative?.Source.MeterDenominator ?? 4
            },
            candidateCount = 16,
            scales = new[] { ScaleName(representative?.Harmony.GlobalScale ?? ScaleKind.Major), "naturalMinor", "dorian", "mixolydian" }.Distinct().ToArray(),
            forms = new
            {
                grammarId = id + "-form-v1",
                productions = new[] { production },
                tokens = BuildTokens(production)
            },
            harmony = new
            {
                grammarId = id + "-harmony-v1",
                progressions
            },
            rhythms = BuildRhythms(blueprints),
            roles,
            constraints = new
            {
                minimumNotesPerPhrase = 2,
                maximumNotesPerPhrase = 96,
                maximumMelodicLeap = 16,
                maximumRepeatedPitch = 5,
                minimumChordToneRatio = 0.2,
                requireCadentialFinalTone = false,
                preferredMinimumRange = 4,
                preferredMaximumRange = 28,
                maximumDensityRatio = 1.0
            }
        };
        JsonSerializerOptions options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(document, options);
    }

    private static List<object> BuildRoles(IReadOnlyList<ReferenceCompositionBlueprint> blueprints)
    {
        List<ReferenceTrackProfile> profiles = blueprints.SelectMany(item => item.Instrumentation.Tracks).Where(item => !item.Ignored).ToList();
        List<object> roles = [];
        int channel = 0;
        foreach (IGrouping<TrackRole, ReferenceTrackProfile> group in profiles.GroupBy(item => item.InferredRole).OrderBy(group => group.Key))
        {
            ReferenceTrackProfile representative = group.OrderByDescending(item => item.NoteCount).First();
            bool percussion = group.Key == TrackRole.Drums || representative.IsPercussion;
            int midiChannel = percussion ? 9 : NextChannel(ref channel);
            roles.Add(RoleObject(
                group.Key.ToString().ToLowerInvariant(),
                "Reference " + group.Key,
                group.Key.ToString().ToLowerInvariant(),
                midiChannel,
                representative.Program,
                percussion,
                Math.Clamp(group.Min(item => item.MinimumPitch), 0, 127),
                Math.Clamp(group.Max(item => item.MaximumPitch), 0, 127),
                checked((int)Math.Clamp(Math.Round(group.Average(item => item.NotesPerBeat) * 22), 20, 100))));
        }
        return roles;
    }
    private static object RoleObject(string id, string name, string role, int channel, int program, bool percussion, int min, int max, int density) => new
    {
        id,
        name,
        role,
        enabled = true,
        generalMidiProgram = Math.Clamp(program, 0, 127),
        isPercussion = percussion,
        rhythmPatternId = "reference-groove",
        minimumPitch = min,
        maximumPitch = max,
        density,
        midiChannel = channel
    };
    private static object[] BuildTokens(List<string> production) => production.Distinct(StringComparer.OrdinalIgnoreCase).Select((symbol, index) => (object)new
    {
        symbol,
        name = symbol switch { "I" => "Intro", "O" => "Outro", "B" => "Contrast", _ => "Reference Section " + symbol },
        kind = symbol switch { "I" => "intro", "O" => "outro", "B" => "chorus", _ => "verse" },
        lengthBars = 4,
        energy = Math.Clamp(0.35 + index * 0.08, 0.3, 0.9),
        treatment = index == 0 ? "original" : "transpose"
    }).ToArray();
    private static object[] BuildRhythms(IReadOnlyList<ReferenceCompositionBlueprint> blueprints)
    {
        List<int> pattern = blueprints.SelectMany(item => item.Rhythm.RoleGrooves).OrderByDescending(item => item.Density).FirstOrDefault()?.RepresentativePattern ?? [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
        if (pattern.Count == 0) pattern = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
        return [new { id = "reference-groove", steps = pattern }];
    }
    private static string SectionToken(ReferenceSectionProfile section) => section.Kind switch
    {
        FormSectionKind.Intro => "I",
        FormSectionKind.Outro => "O",
        FormSectionKind.Chorus => "B",
        FormSectionKind.Bridge => "C",
        FormSectionKind.Breakdown => "D",
        FormSectionKind.Solo => "S",
        _ => "A"
    };
    private static int NextChannel(ref int channel)
    {
        while (channel == 9) channel++;
        int result = channel % 16;
        channel++;
        return result;
    }
    private static string ScaleName(ScaleKind scale)
    {
        string name = scale.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
    private static string Slug(string text)
    {
        string value = new string((text ?? string.Empty).ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
        while (value.Contains("--", StringComparison.Ordinal)) value = value.Replace("--", "-", StringComparison.Ordinal);
        value = value.Trim('-');
        return value.Length == 0 ? "reference-style" : value;
    }
}
