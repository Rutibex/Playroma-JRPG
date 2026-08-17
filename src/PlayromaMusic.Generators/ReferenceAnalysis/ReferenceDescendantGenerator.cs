using System.Globalization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Generators.Orchestration;
using PlayromaMusic.Generators.Rhythms;
using PlayromaMusic.Generators.Structured;

namespace PlayromaMusic.Generators.ReferenceAnalysis;

public sealed class ReferenceDescendantGenerationResult
{
    public ProjectDocument Project { get; set; } = new();
    public ReferenceDescendantEvaluation Evaluation { get; set; } = new();
    public List<ReferenceDescendantEvaluation> Candidates { get; set; } = new();
}

public sealed class ReferenceDescendantGenerator
{
    private readonly StructuredCompositionService _composer = new();
    private readonly ReferenceSimilarityEvaluator _evaluator = new();

    public ReferenceDescendantGenerationResult Generate(
        ReferenceCompositionBlueprint blueprint,
        ReferenceDescendantRequest request,
        InstrumentCatalogDocument? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(request);
        ReferenceBlueprintEditor.Normalize(blueprint);
        ReferenceGenerationMode mode = request.ModeOverride ?? blueprint.Generation.Mode;
        int candidateCount = Math.Clamp(request.CandidateCount > 0 ? request.CandidateCount : blueprint.Generation.CandidateCount, 1, 64);
        ulong rootSeed = request.Seed == 0 ? SeedDerivation.Derive(0x5245464552454E43UL, blueprint.Id, blueprint.Source.SourceFingerprint, mode.ToString()) : request.Seed;
        List<(ProjectDocument Project, ReferenceDescendantEvaluation Evaluation)> candidates = new();
        for (int index = 0; index < candidateCount; index++)
        {
            ulong seed = SeedDerivation.Derive(rootSeed, "reference-descendant", blueprint.Id, mode.ToString(), index.ToString(CultureInfo.InvariantCulture));
            (ProjectDocument project, ReferenceDescendantEvaluation evaluation) = BuildCandidate(blueprint, request, mode, seed, catalog);
            candidates.Add((project, evaluation));
        }
        (ProjectDocument Project, ReferenceDescendantEvaluation Evaluation) winner = candidates
            .OrderByDescending(item => item.Evaluation.PassedOriginality)
            .ThenByDescending(item => item.Evaluation.QualityScore)
            .ThenByDescending(item => item.Evaluation.NoveltyScore)
            .First();
        winner.Project.ReferenceAnalysis.RecentDescendants = candidates.Select(item => item.Evaluation).OrderByDescending(item => item.QualityScore).Take(64).ToList();
        winner.Project.ReferenceAnalysis.DescendantRevision++;
        return new ReferenceDescendantGenerationResult
        {
            Project = winner.Project,
            Evaluation = winner.Evaluation,
            Candidates = candidates.Select(item => item.Evaluation).OrderByDescending(item => item.QualityScore).ToList()
        };
    }

    public IEnumerable<ReferenceDescendantGenerationResult> GenerateInfiniteRadio(
        ReferenceCompositionBlueprint blueprint,
        ulong stationSeed,
        InstrumentCatalogDocument? catalog = null)
    {
        ulong index = 0;
        while (true)
        {
            ulong seed = SeedDerivation.Derive(stationSeed, "infinite-radio", blueprint.Id, index++.ToString(CultureInfo.InvariantCulture));
            yield return Generate(blueprint, new ReferenceDescendantRequest
            {
                Seed = seed,
                BlueprintId = blueprint.Id,
                ModeOverride = ReferenceGenerationMode.InfiniteRadio,
                CandidateCount = Math.Clamp(blueprint.Generation.CandidateCount, 2, 16)
            }, catalog);
        }
    }

    private (ProjectDocument Project, ReferenceDescendantEvaluation Evaluation) BuildCandidate(
        ReferenceCompositionBlueprint blueprint,
        ReferenceDescendantRequest request,
        ReferenceGenerationMode mode,
        ulong seed,
        InstrumentCatalogDocument? catalog)
    {
        string styleId = ResolveStyleId(blueprint, request.TargetStyleId, mode);
        ProjectDocument project = _composer.CreateProject(seed, styleId);
        project.Orchestration.Catalog = CompositionRandomizer.CloneCatalog(catalog ?? project.Orchestration.Catalog);
        project.ReferenceAnalysis.Enabled = true;
        project.ReferenceAnalysis.ActiveBlueprintId = blueprint.Id;
        project.ReferenceAnalysis.ActiveBlueprint = ReferenceBlueprintSerializer.Clone(blueprint);
        project.ReferenceAnalysis.ActiveBlueprint.Generation.Mode = mode;
        project.ReferenceAnalysis.SavedBlueprints = [ReferenceBlueprintSerializer.Clone(project.ReferenceAnalysis.ActiveBlueprint)];
        project.ReferenceAnalysis.Diagnostics.Add($"Generated from reference blueprint '{blueprint.Name}' in {mode} mode.");
        project.Tags.RemoveAll(tag => tag.StartsWith("stage", StringComparison.OrdinalIgnoreCase));
        project.Tags.AddRange(["stage8", "reference-descendant", mode.ToString().ToLowerInvariant(), "blueprint:" + blueprint.Id]);
        project.ProjectId = SeedDerivation.StableId("project", seed, "stage8", blueprint.Id, mode.ToString());
        project.Title = BuildTitle(blueprint, mode, seed);
        project.Song.Title = project.Title;
        project.MasterSeed = seed;

        DeterministicRandom random = new(SeedDerivation.Derive(seed, "reference-policy"));
        PreservationProfile preservation = PreservationProfile.For(blueprint.Generation, mode);
        ApplyGlobalSettings(project, blueprint, preservation, random);
        ApplyForm(project, blueprint, preservation, random);
        ApplyHarmony(project, blueprint, preservation, random);
        ApplyMotif(project, blueprint, preservation, random, repairPass: 0);
        ApplyGrooves(project, blueprint, preservation, random);
        ApplyArrangement(project, blueprint, preservation, random);
        ApplyTension(project, blueprint, preservation);
        ReferenceInstrumentationPolicy instrumentationPolicy = blueprint.Generation.ResolveInstrumentationPolicy(mode);
        bool replanOrchestration = ApplyInstrumentation(project, blueprint, preservation, instrumentationPolicy);
        project.ReferenceAnalysis.Diagnostics.Add($"Reference instrumentation policy: {instrumentationPolicy}.");
        _composer.RebuildFromCurrentPlans(project, replanOrchestration);
        ApplyPerformance(project, blueprint, preservation, random);

        ReferenceDescendantEvaluation evaluation = _evaluator.Evaluate(blueprint, project, seed, mode);
        int maximumRepairs = Math.Clamp(blueprint.Generation.Originality.MaximumRepairPasses, 0, 12);
        for (int pass = 1; !evaluation.PassedOriginality && pass <= maximumRepairs; pass++)
        {
            RepairCandidate(project, blueprint, preservation, evaluation, random, pass, instrumentationPolicy);
            evaluation = _evaluator.Evaluate(blueprint, project, seed, mode, pass);
        }
        project.ReferenceAnalysis.RecentDescendants = [evaluation];
        project.ReferenceAnalysis.DescendantRevision = 1;
        return (project, evaluation);
    }

    private static string ResolveStyleId(ReferenceCompositionBlueprint blueprint, string requested, ReferenceGenerationMode mode)
    {
        string candidate = !string.IsNullOrWhiteSpace(requested) ? requested : blueprint.Generation.TargetStyleId;
        if (mode == ReferenceGenerationMode.GenreTranslation && string.IsNullOrWhiteSpace(requested))
        {
            string source = blueprint.StyleHints.FirstOrDefault() ?? candidate;
            PlayromaMusic.Core.StylePacks.StyleFamily? sourceFamily = StructuredStyleLibrary.All
                .FirstOrDefault(item => item.Id.Equals(source, StringComparison.OrdinalIgnoreCase))?.Family;
            candidate = StructuredStyleLibrary.All.FirstOrDefault(item =>
                !item.Id.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                (sourceFamily is null || item.Family != sourceFamily.Value))?.Id ?? StructuredCompositionService.DefaultStyleId;
        }
        if (string.IsNullOrWhiteSpace(candidate)) candidate = blueprint.StyleHints.FirstOrDefault() ?? StructuredCompositionService.DefaultStyleId;
        try
        {
            _ = StructuredStyleLibrary.Get(candidate);
            return candidate;
        }
        catch
        {
            return StructuredCompositionService.DefaultStyleId;
        }
    }

    private static void ApplyGlobalSettings(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (!blueprint.Generation.AllowTempoMutation || random.Chance(preserve.Form))
        {
            double source = blueprint.Source.PrimaryTempo;
            double mutation = blueprint.Generation.AllowTempoMutation ? (-0.08 + random.NextDouble() * 0.16) * (1 - preserve.Form) : 0;
            project.Song.TempoMap = [new TempoChange { Tick = 0, BeatsPerMinute = Math.Clamp(source * (1 + mutation), 20, 400) }];
        }
        if (!blueprint.Generation.AllowMeterMutation || preserve.Form >= 0.60)
        {
            project.Song.MeterMap = [new MeterChange
            {
                Tick = 0,
                Numerator = Math.Clamp(blueprint.Source.MeterNumerator, 1, 32),
                Denominator = IsPowerOfTwo(blueprint.Source.MeterDenominator) ? blueprint.Source.MeterDenominator : 4
            }];
        }
        if (!blueprint.Generation.AllowKeyMutation || random.Chance(preserve.Harmony)) project.Song.KeyRoot = blueprint.Harmony.GlobalKeyRoot;
        if (random.Chance(preserve.Harmony)) project.Song.Scale = blueprint.Harmony.GlobalScale;
        if (blueprint.Generation.AllowKeyMutation && preserve.Harmony < 0.85 && random.Chance(0.55))
        {
            int shift = random.Pick(new[] { -5, -3, -2, 2, 3, 5, 7 });
            project.Song.KeyRoot = (PitchClass)Mod12((int)project.Song.KeyRoot + shift);
        }
    }

    private static void ApplyForm(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (blueprint.Form.Sections.Count == 0 || preserve.Form < 0.18) return;
        IReadOnlyList<ReferenceSectionProfile> source = blueprint.Form.Sections;
        List<FormSectionPlan> basePlan = project.StructuredComposition.FormPlan.ToList();
        if (basePlan.Count == 0)
        {
            basePlan.Add(new FormSectionPlan
            {
                Id = SeedDerivation.StableId("section", project.MasterSeed, "stage8-fallback"),
                Symbol = "A",
                Name = "Section 1",
                Kind = FormSectionKind.Verse,
                StartBar = 0,
                LengthBars = Math.Max(1, source[0].LengthBars),
                Energy = Math.Clamp(source[0].Energy, 0.05, 1),
                MotifTreatment = MotifTransformationKind.Original
            });
        }
        int targetCount = preserve.Form >= 0.55 ? source.Count : Math.Clamp((int)Math.Round(source.Count * preserve.Form + basePlan.Count * (1 - preserve.Form)), 1, 24);
        List<FormSectionPlan> result = new();
        int startBar = 0;
        for (int index = 0; index < targetCount; index++)
        {
            ReferenceSectionProfile reference = source[Math.Min(source.Count - 1, (int)Math.Floor(index / (double)Math.Max(1, targetCount) * source.Count))];
            FormSectionPlan fallback = basePlan[Math.Min(basePlan.Count - 1, index % Math.Max(1, basePlan.Count))];
            int referenceLength = reference.LengthBars;
            int baseLength = fallback.LengthBars;
            int length = Math.Clamp((int)Math.Round(referenceLength * preserve.SectionLengths + baseLength * (1 - preserve.SectionLengths)), 1, 32);
            if (preserve.SectionLengths < 0.92 && random.Chance(0.40)) length = Math.Clamp(length + random.Pick(new[] { -2, -1, 1, 2 }), 1, 32);
            FormSectionKind kind = random.Chance(preserve.Form) ? reference.Kind : fallback.Kind;
            result.Add(new FormSectionPlan
            {
                Id = SeedDerivation.StableId("section", project.MasterSeed, "stage8", index.ToString(CultureInfo.InvariantCulture)),
                Symbol = string.IsNullOrWhiteSpace(reference.Label) ? SymbolFor(kind) : reference.Label,
                Name = $"{kind} {index + 1}",
                Kind = kind,
                VariationIndex = reference.VariationIndex + (preserve.Form < 0.85 ? random.NextInt(0, 3) : 0),
                StartBar = startBar,
                LengthBars = length,
                Energy = Math.Clamp(reference.Energy * preserve.Tension + fallback.Energy * (1 - preserve.Tension), 0.05, 1),
                MotifTreatment = TreatmentFor(kind, reference.VariationIndex)
            });
            startBar += length;
        }
        project.StructuredComposition.FormPlan = result;
        project.StructuredComposition.FormDerivation = result.Select((section, index) => new FormDerivationStep
        {
            Step = index,
            ExpandedSymbol = index == 0 ? "SONG" : result[index - 1].Symbol,
            Production = section.Symbol,
            ResultingSymbols = result.Take(index + 1).Select(item => item.Symbol).ToList()
        }).ToList();
    }

    private static void ApplyHarmony(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (blueprint.Harmony.Events.Count == 0 || preserve.Harmony < 0.12) return;
        int totalBars = project.StructuredComposition.FormPlan.Sum(item => item.LengthBars);
        int sourceBeatsPerBar = Math.Max(1, blueprint.Source.MeterNumerator);
        List<HarmonicEventPlan> baseHarmony = project.StructuredComposition.HarmonyPlan.ToList();
        List<HarmonicEventPlan> result = new();
        for (int bar = 0; bar < totalBars;)
        {
            int sourceBar = (int)Math.Round(bar / (double)Math.Max(1, totalBars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
            ReferenceHarmonicEvent reference = blueprint.Harmony.Events.LastOrDefault(item => item.StartBar <= sourceBar) ?? blueprint.Harmony.Events[0];
            HarmonicEventPlan fallback = baseHarmony.LastOrDefault(item => item.StartBar <= bar) ?? baseHarmony.FirstOrDefault() ?? new HarmonicEventPlan();
            int degree = random.Chance(preserve.Harmony) ? reference.ScaleDegree : fallback.ScaleDegree;
            if (preserve.Harmony < 0.95 && random.Chance((1 - preserve.Harmony) * 0.8)) degree = Mod(degree + random.Pick(new[] { -2, -1, 1, 2 }), 7);
            FormSectionPlan section = project.StructuredComposition.FormPlan.Last(item => item.StartBar <= bar);
            int referenceLength = Math.Clamp((int)Math.Round(reference.LengthBeats / sourceBeatsPerBar), 1, Math.Max(1, section.StartBar + section.LengthBars - bar));
            int fallbackLength = Math.Clamp(fallback.LengthBars, 1, Math.Max(1, section.StartBar + section.LengthBars - bar));
            int length = Math.Clamp((int)Math.Round(referenceLength * preserve.HarmonicRhythm + fallbackLength * (1 - preserve.HarmonicRhythm)), 1, Math.Max(1, section.StartBar + section.LengthBars - bar));
            if (preserve.HarmonicRhythm < 0.85 && random.Chance((1 - preserve.HarmonicRhythm) * 0.55))
                length = Math.Clamp(length + random.Pick(new[] { -1, 1 }), 1, Math.Max(1, section.StartBar + section.LengthBars - bar));
            bool cadenceEvent = bar + length >= section.StartBar + section.LengthBars;
            result.Add(new HarmonicEventPlan
            {
                Id = SeedDerivation.StableId("harmony", project.MasterSeed, "stage8", bar.ToString(CultureInfo.InvariantCulture)),
                SectionId = section.Id,
                StartBar = bar,
                LengthBars = length,
                ScaleDegree = degree,
                ChordToneCount = ChordToneCount(reference.Quality),
                Inversion = random.Chance(preserve.Harmony) ? InversionFor(reference, degree) : random.NextInt(0, 3),
                Function = random.Chance(preserve.Harmony) ? reference.Function : fallback.Function,
                Cadence = cadenceEvent && random.Chance(preserve.Harmony) ? (reference.Cadence == CadenceKind.None ? CadenceKind.Authentic : reference.Cadence) : CadenceKind.None,
                Tension = Math.Clamp(reference.Tension * preserve.Tension + fallback.Tension * (1 - preserve.Tension), 0, 1)
            });
            bar += length;
        }
        project.StructuredComposition.HarmonyPlan = result;
    }

    private static void ApplyMotif(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random,
        int repairPass)
    {
        ReferenceMotifPattern? source = blueprint.Motifs.Motifs.Where(item => item.Enabled).OrderByDescending(item => item.Importance * item.Confidence).FirstOrDefault();
        if (source is null || preserve.MotifContour < 0.08) return;
        List<int> degrees = source.ScaleDegrees.Count >= 3 ? source.ScaleDegrees.ToList() : BuildDegreeContour(source.Intervals);
        int mutationCount = Math.Max(repairPass, (int)Math.Round((1 - preserve.MotifContour) * Math.Max(1, degrees.Count / 2.0)));
        for (int mutation = 0; mutation < mutationCount && degrees.Count > 1; mutation++)
        {
            int index = random.NextInt(1, degrees.Count);
            degrees[index] += random.Pick(new[] { -2, -1, 1, 2 });
        }
        List<int> rhythm = source.RhythmUnits.Count > 0 ? source.RhythmUnits.ToList() : blueprint.Motifs.PreferredRhythmUnits.Take(Math.Max(3, degrees.Count)).ToList();
        if (rhythm.Count == 0) rhythm = Enumerable.Repeat(2, degrees.Count).ToList();
        while (rhythm.Count < degrees.Count) rhythm.Add(rhythm[rhythm.Count % Math.Max(1, rhythm.Count)]);
        if (preserve.MotifRhythm < 0.90 || repairPass > 0)
        {
            int changes = Math.Max(repairPass, (int)Math.Round((1 - preserve.MotifRhythm) * rhythm.Count));
            for (int mutation = 0; mutation < changes; mutation++)
            {
                int index = random.NextInt(rhythm.Count);
                rhythm[index] = Math.Clamp(rhythm[index] + random.Pick(new[] { -1, 1, 2 }), 1, 16);
            }
        }
        project.StructuredComposition.Motifs = [new MotifDefinition
        {
            Id = SeedDerivation.StableId("motif", project.MasterSeed, "stage8", repairPass.ToString(CultureInfo.InvariantCulture)),
            Name = "Reference-derived motif",
            Revision = repairPass,
            DegreeOffsets = degrees,
            RhythmUnits = rhythm.Take(degrees.Count).ToList(),
            Accents = Enumerable.Range(0, degrees.Count).Select(index => index == 0 || index % 4 == 0 ? 1 : 0).ToList()
        }];
        project.AdvancedComposition.MotifOccurrences = project.StructuredComposition.FormPlan.Select((section, index) => new MotifOccurrencePlan
        {
            SectionId = section.Id,
            MotifId = project.StructuredComposition.Motifs[0].Id,
            FamilyId = source.Id,
            OccurrenceIndex = index,
            Transformation = section.MotifTreatment,
            TargetSimilarity = Math.Clamp(preserve.MotifContour - repairPass * 0.06, 0.15, 0.95),
            Required = preserve.MotifContour >= 0.55 && (index == 0 || section.Kind == FormSectionKind.Chorus || index == project.StructuredComposition.FormPlan.Count - 1)
        }).ToList();
    }

    private static void ApplyGrooves(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (blueprint.Rhythm.RoleGrooves.Count == 0 || preserve.Groove < 0.10) return;
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            ReferenceRoleGroove? groove = blueprint.Rhythm.RoleGrooves.Where(item => item.Role == role.Role).OrderByDescending(item => item.Density).FirstOrDefault();
            if (groove is null) continue;
            bool[] pattern = groove.RepresentativePattern.Select(value => value != 0).ToArray();
            if (pattern.Length == 0 || !pattern.Any(value => value)) continue;
            for (int index = 0; index < pattern.Length; index++)
            {
                if (random.Chance((1 - preserve.Groove) * 0.16)) pattern[index] = !pattern[index];
            }
            if (!pattern.Any(value => value)) pattern[0] = true;
            string id = $"reference-{blueprint.Id}-{role.Role.ToString().ToLowerInvariant()}";
            RhythmPatternLibrary.Register(id, pattern);
            role.RhythmPatternId = id;
            int referenceDensity = Math.Clamp((int)Math.Round(groove.Density * blueprint.Rhythm.StepsPerBar * 100), 8, 100);
            role.Density = Math.Clamp((int)Math.Round(referenceDensity * preserve.Groove + role.Density * (1 - preserve.Groove)), 5, 100);
        }
    }

    private static void ApplyArrangement(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (blueprint.Arrangement.Sections.Count == 0 || preserve.Arrangement < 0.10) return;
        int totalBars = project.StructuredComposition.FormPlan.Sum(item => item.LengthBars);
        project.AdvancedComposition.ArrangementPlan = project.StructuredComposition.FormPlan.Select(section =>
        {
            int sourceBar = (int)Math.Round(section.StartBar / (double)Math.Max(1, totalBars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
            ReferenceSectionArrangement source = blueprint.Arrangement.Sections.OrderBy(item => Math.Abs(item.StartBar - sourceBar)).First();
            return new ArrangementSectionPlan
            {
                SectionId = section.Id,
                TargetEnergy = Math.Clamp(section.Energy * (1 - preserve.Tension) + NearestTension(blueprint, sourceBar) * preserve.Tension, 0, 1),
                Fitness = preserve.Arrangement,
                Roles = project.StructuredComposition.Roles.Select(role =>
                {
                    ReferenceRoleActivity? activity = source.Roles.Where(item => item.Role == role.Role).OrderByDescending(item => item.Intensity).FirstOrDefault();
                    bool sourceActive = activity?.Active ?? false;
                    bool active = random.Chance(preserve.Arrangement) ? sourceActive : random.Chance(section.Energy * 0.65 + 0.20);
                    if (role.Role == TrackRole.Lead && section.Kind is FormSectionKind.Chorus or FormSectionKind.Solo) active = true;
                    if (role.Role == TrackRole.Drums && section.Energy > 0.65) active = true;
                    return new ArrangementRoleState
                    {
                        RoleId = role.Id,
                        Role = role.Role,
                        Active = active,
                        Intensity = Math.Clamp((int)Math.Round((activity?.Intensity ?? section.Energy) * 100), 0, 100)
                    };
                }).ToList()
            };
        }).ToList();
    }

    private static void ApplyTension(ProjectDocument project, ReferenceCompositionBlueprint blueprint, PreservationProfile preserve)
    {
        if (blueprint.Tension.Points.Count == 0 || preserve.Tension < 0.10) return;
        int totalBars = project.StructuredComposition.FormPlan.Sum(item => item.LengthBars);
        project.AdvancedComposition.TensionCurve = Enumerable.Range(0, totalBars).Select(bar =>
        {
            int sourceBar = (int)Math.Round(bar / (double)Math.Max(1, totalBars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
            double reference = NearestTension(blueprint, sourceBar);
            HarmonicEventPlan? harmony = project.StructuredComposition.HarmonyPlan.FirstOrDefault(item => item.StartBar == bar);
            double generated = harmony?.Tension ?? 0.5;
            return new TensionPoint
            {
                Bar = bar,
                Value = Math.Clamp(reference * preserve.Tension + generated * (1 - preserve.Tension), 0, 1),
                Source = "Playroma Music reference blueprint"
            };
        }).ToList();
        project.AdvancedComposition.Tension.ClimaxPosition = totalBars <= 1 ? 0.5 : Math.Clamp(blueprint.Tension.ClimaxBar / (double)Math.Max(1, blueprint.Form.TotalBars - 1), 0, 1);
        project.AdvancedComposition.Tension.ClimaxStrength = blueprint.Tension.ClimaxStrength;
        project.AdvancedComposition.Tension.ResolutionStrength = blueprint.Tension.ResolutionStrength;
    }

    private static bool ApplyInstrumentation(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        ReferenceInstrumentationPolicy policy)
    {
        if (policy == ReferenceInstrumentationPolicy.Reorchestrate || blueprint.Instrumentation.Tracks.Count == 0)
        {
            return true;
        }

        List<ReferenceRoleBinding> bindings = BindReferenceTracksToRoles(project, blueprint);
        if (bindings.Count == 0)
        {
            project.ReferenceAnalysis.Diagnostics.Add("No source tracks could be matched to structured roles; normal orchestration was used.");
            return true;
        }

        if (policy == ReferenceInstrumentationPolicy.PreferSourceFamilies)
        {
            foreach (ReferenceRoleBinding binding in bindings)
            {
                ApplySourceFamilyPreference(project, binding.Role, binding.Source, preserve.Instrumentation);
            }
            project.Orchestration.Randomization.PermitSectionInstrumentChanges = true;
            return true;
        }

        // Exact bank/program preservation bypasses the evolutionary re-orchestration pass. The
        // generated notes are still new, but every matched role uses the source MIDI's program.
        project.Orchestration.Randomization.PermitSectionInstrumentChanges = blueprint.Instrumentation.UsesSectionProgramChanges;
        project.Orchestration.SectionPlans = BuildReferenceOrchestrationPlans(project, blueprint, bindings);
        SynchronizeReferenceArrangement(project);
        return false;
    }

    private static List<ReferenceRoleBinding> BindReferenceTracksToRoles(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint)
    {
        List<CompositionRoleSettings> roles = project.StructuredComposition.Roles;
        List<ReferenceRoleBinding> result = new();
        HashSet<string> usedRoleIds = new(StringComparer.Ordinal);
        HashSet<int> usedChannels = roles.Where(item => item.IsPercussion).Select(item => item.MidiChannel).ToHashSet();

        ReferenceTrackProfile[] sources = blueprint.Instrumentation.Tracks
            .Where(item => !item.Ignored && item.NoteCount > 0)
            .OrderByDescending(item => item.IsPercussion)
            .ThenByDescending(item => item.RoleConfidence)
            .ThenByDescending(item => item.NoteCount)
            .Take(24)
            .ToArray();

        foreach (ReferenceTrackProfile source in sources)
        {
            CompositionRoleSettings? role = roles.FirstOrDefault(item =>
                item.Role == source.InferredRole && !usedRoleIds.Contains(item.Id));
            if (role is null)
            {
                CompositionRoleSettings? template = roles.FirstOrDefault(item => item.Role == source.InferredRole);
                if (template is null) continue;
                role = CloneRoleForReference(project, template, source, result.Count);
                roles.Add(role);
            }

            role.Enabled = true;
            role.Name = string.IsNullOrWhiteSpace(source.TrackName) ? Humanize(source.InferredRole.ToString()) : source.TrackName;
            role.IsPercussion = source.IsPercussion || source.InferredRole == TrackRole.Drums;
            role.MidiChannel = role.IsPercussion ? 9 : AllocateReferenceChannel(source.MidiChannel, usedChannels);
            role.GeneralMidiProgram = Math.Clamp(source.Program, 0, 127);
            role.MinimumPitch = Math.Clamp(source.MinimumPitch, 0, 127);
            role.MaximumPitch = Math.Clamp(Math.Max(source.MinimumPitch, source.MaximumPitch), role.MinimumPitch, 127);
            if (role.MaximumPitch - role.MinimumPitch < 7)
            {
                role.MinimumPitch = Math.Max(0, role.MinimumPitch - 6);
                role.MaximumPitch = Math.Min(127, role.MaximumPitch + 6);
            }
            usedRoleIds.Add(role.Id);
            if (!role.IsPercussion) usedChannels.Add(role.MidiChannel);
            result.Add(new ReferenceRoleBinding(role, source));
        }

        // Roles that did not exist in the source should not silently add unrelated instruments
        // in faithful modes. Keep only indispensable harmony support when the source analysis had
        // low confidence and no track mapped to it.
        foreach (CompositionRoleSettings role in roles)
        {
            if (usedRoleIds.Contains(role.Id)) continue;
            role.Enabled = false;
        }

        return result;
    }

    private static CompositionRoleSettings CloneRoleForReference(
        ProjectDocument project,
        CompositionRoleSettings template,
        ReferenceTrackProfile source,
        int index) => new()
    {
        Id = SeedDerivation.StableId("reference-role", project.MasterSeed, source.TrackId, index.ToString(CultureInfo.InvariantCulture)),
        Name = string.IsNullOrWhiteSpace(source.TrackName) ? template.Name : source.TrackName,
        Role = source.InferredRole,
        Enabled = true,
        Locked = false,
        Revision = template.Revision + index + 1,
        MidiChannel = template.MidiChannel,
        GeneralMidiProgram = source.Program,
        IsPercussion = source.IsPercussion,
        RhythmPatternId = template.RhythmPatternId,
        GeneratorId = template.GeneratorId,
        MinimumPitch = source.MinimumPitch,
        MaximumPitch = source.MaximumPitch,
        Density = template.Density,
        InstrumentSelector = template.InstrumentSelector?.Clone() ?? PresetCatalogFactory.DefaultSelector(source.InferredRole, source.IsPercussion || source.InferredRole == TrackRole.Drums)
    };

    private static int AllocateReferenceChannel(int preferred, IReadOnlySet<int> used)
    {
        int normalized = Math.Clamp(preferred, 0, 15);
        if (normalized != 9 && !used.Contains(normalized)) return normalized;
        for (int channel = 0; channel < 16; channel++)
        {
            if (channel != 9 && !used.Contains(channel)) return channel;
        }
        return normalized == 9 ? 0 : normalized;
    }

    private static void ApplySourceFamilyPreference(
        ProjectDocument project,
        CompositionRoleSettings role,
        ReferenceTrackProfile source,
        double preservation)
    {
        InstrumentPresetDescriptor descriptor = FindOrCreateSourcePreset(project, source);
        role.InstrumentSelector ??= PresetCatalogFactory.DefaultSelector(role.Role, role.IsPercussion);
        role.GeneralMidiProgram = source.Program;
        role.MinimumPitch = Math.Clamp((int)Math.Round(role.MinimumPitch * (1 - preservation) + source.MinimumPitch * preservation), 0, 127);
        role.MaximumPitch = Math.Clamp((int)Math.Round(role.MaximumPitch * (1 - preservation) + source.MaximumPitch * preservation), role.MinimumPitch, 127);
        role.InstrumentSelector.Families = [descriptor.Family];
        role.InstrumentSelector.PreferredTags = descriptor.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        role.InstrumentSelector.AvoidTags.Clear();
        role.InstrumentSelector.RandomWeight = Math.Min(role.InstrumentSelector.RandomWeight, 0.08);
        role.InstrumentSelector.NoveltyWeight = Math.Min(role.InstrumentSelector.NoveltyWeight, 0.12);
        role.InstrumentSelector.StyleAffinityWeight = Math.Max(role.InstrumentSelector.StyleAffinityWeight, 3.2);
        role.InstrumentSelector.MaximumLayers = Math.Min(role.InstrumentSelector.MaximumLayers, 1);
        role.InstrumentSelector.AllowedStrategies = [RoleDistributionStrategy.SingleInstrument];
    }

    private static List<SectionOrchestrationPlan> BuildReferenceOrchestrationPlans(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        IReadOnlyList<ReferenceRoleBinding> bindings)
    {
        List<SectionOrchestrationPlan> plans = new();
        int generatedBars = Math.Max(1, project.StructuredComposition.FormPlan.Sum(item => item.LengthBars));
        foreach (FormSectionPlan section in project.StructuredComposition.FormPlan.OrderBy(item => item.StartBar))
        {
            int sourceBar = (int)Math.Round(section.StartBar / (double)Math.Max(1, generatedBars - 1) * Math.Max(0, blueprint.Form.TotalBars - 1));
            ReferenceSectionArrangement? sourceArrangement = blueprint.Arrangement.Sections
                .OrderBy(item => Math.Abs(item.StartBar - sourceBar))
                .FirstOrDefault();
            List<InstrumentAssignmentPlan> assignments = new();
            foreach (ReferenceRoleBinding binding in bindings)
            {
                InstrumentPresetDescriptor preset = FindOrCreateSourcePreset(project, binding.Source);
                ReferenceRoleActivity? activity = sourceArrangement?.Roles
                    .Where(item => item.SourceTrackId == binding.Source.TrackId)
                    .OrderByDescending(item => item.Intensity)
                    .FirstOrDefault()
                    ?? sourceArrangement?.Roles
                        .Where(item => item.Role == binding.Source.InferredRole)
                        .OrderByDescending(item => item.Intensity)
                        .FirstOrDefault();
                bool active = activity?.Active ?? true;
                assignments.Add(new InstrumentAssignmentPlan
                {
                    SectionId = section.Id,
                    RoleId = binding.Role.Id,
                    LayerIndex = 0,
                    PresetKey = preset.Key,
                    PresetName = string.IsNullOrWhiteSpace(binding.Source.PresetName) ? preset.Name : binding.Source.PresetName,
                    SoundFontId = preset.SoundFontId,
                    Bank = Math.Clamp(binding.Source.Bank, 0, 16383),
                    Program = Math.Clamp(binding.Source.Program, 0, 127),
                    MidiChannel = binding.Role.IsPercussion ? 9 : binding.Role.MidiChannel,
                    Active = active,
                    OctaveOffset = 0,
                    Volume = Math.Clamp((activity?.Intensity ?? 0.82) + 0.18, 0.25, 1.0),
                    Pan = 0,
                    Strategy = RoleDistributionStrategy.SingleInstrument,
                    SuitabilityScore = 1,
                    Rationale = $"Preserved from source track '{binding.Source.TrackName}' (bank {binding.Source.Bank}, program {binding.Source.Program})."
                });
            }
            plans.Add(new SectionOrchestrationPlan
            {
                SectionId = section.Id,
                TargetEnergy = section.Energy,
                EnsembleScore = 1,
                Assignments = assignments
            });
        }
        return plans;
    }

    private static void SynchronizeReferenceArrangement(ProjectDocument project)
    {
        project.AdvancedComposition.ArrangementPlan = project.StructuredComposition.FormPlan
            .OrderBy(item => item.StartBar)
            .Select(section =>
            {
                SectionOrchestrationPlan? orchestration = project.Orchestration.SectionPlans
                    .FirstOrDefault(item => item.SectionId == section.Id);
                return new ArrangementSectionPlan
                {
                    SectionId = section.Id,
                    TargetEnergy = section.Energy,
                    Fitness = 1,
                    Roles = project.StructuredComposition.Roles.Select(role =>
                    {
                        InstrumentAssignmentPlan? assignment = orchestration?.Assignments
                            .FirstOrDefault(item => item.RoleId == role.Id && item.LayerIndex == 0);
                        return new ArrangementRoleState
                        {
                            RoleId = role.Id,
                            Role = role.Role,
                            Active = role.Enabled && assignment?.Active == true,
                            Intensity = assignment is null ? 0 : Math.Clamp((int)Math.Round(assignment.Volume * 100), 0, 100)
                        };
                    }).ToList()
                };
            }).ToList();
    }

    private static InstrumentPresetDescriptor FindOrCreateSourcePreset(
        ProjectDocument project,
        ReferenceTrackProfile source)
    {
        InstrumentPresetDescriptor? descriptor = project.Orchestration.Catalog.Presets
            .Where(item => item.Bank == source.Bank && item.Program == source.Program && item.IsDrumKit == (source.IsPercussion || source.InferredRole == TrackRole.Drums))
            .OrderByDescending(item => string.Equals(item.Name, source.PresetName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (descriptor is not null) return descriptor;

        descriptor = PresetCatalogFactory.Classify(
            project.Orchestration.Catalog.SoundFontId,
            project.Orchestration.Catalog.SoundFontName,
            source.Bank,
            source.Program,
            source.PresetName,
            source.IsPercussion || source.InferredRole == TrackRole.Drums);
        project.Orchestration.Catalog.Presets.Add(descriptor);
        return descriptor;
    }

    private static void ApplyPerformance(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        DeterministicRandom random)
    {
        if (preserve.Performance <= 0) return;
        MeterChange meter = project.Song.MeterMap.FirstOrDefault() ?? new MeterChange();
        long beatTicks = project.Song.TicksPerQuarterNote * 4L / Math.Max(1, meter.Denominator);
        foreach (SongTrack track in project.Song.Tracks)
        {
            ReferenceRolePerformance roleProfile = blueprint.Performance.Roles.FirstOrDefault(item => item.Role == track.Role) ?? new ReferenceRolePerformance
            {
                Role = track.Role,
                MeanVelocity = blueprint.Performance.MeanVelocity,
                VelocityDeviation = blueprint.Performance.VelocityDeviation,
                MeanDurationRatio = blueprint.Performance.MeanDurationRatio,
                TimingDeviationTicks = blueprint.Performance.TimingDeviationTicks
            };
            foreach (SongClip clip in track.Clips)
            {
                foreach (NoteEvent note in clip.Notes)
                {
                    double targetVelocity = roleProfile.MeanVelocity + random.NextGaussian() * roleProfile.VelocityDeviation;
                    note.Velocity = Math.Clamp((int)Math.Round(note.Velocity * (1 - preserve.Performance) + targetVelocity * preserve.Performance), 1, 127);
                    long targetDuration = Math.Max(1, (long)Math.Round(beatTicks * roleProfile.MeanDurationRatio));
                    note.DurationTicks = Math.Clamp((long)Math.Round(note.DurationTicks * (1 - preserve.Performance) + targetDuration * preserve.Performance), 1, Math.Max(1, clip.LengthTicks - note.StartTick));
                    if (roleProfile.TimingDeviationTicks > 0 && note.StartTick > 0)
                    {
                        long jitter = (long)Math.Round(random.NextGaussian() * roleProfile.TimingDeviationTicks * preserve.Performance);
                        note.StartTick = Math.Clamp(note.StartTick + jitter, 0, Math.Max(0, clip.LengthTicks - note.DurationTicks));
                    }
                }
                clip.Notes = clip.Notes.OrderBy(item => item.StartTick).ThenBy(item => item.Pitch).ToList();
            }
        }
    }

    private void RepairCandidate(
        ProjectDocument project,
        ReferenceCompositionBlueprint blueprint,
        PreservationProfile preserve,
        ReferenceDescendantEvaluation evaluation,
        DeterministicRandom random,
        int pass,
        ReferenceInstrumentationPolicy instrumentationPolicy)
    {
        bool changedHarmony = evaluation.ChordProgressionOverlap > blueprint.Generation.Originality.MaximumChordProgressionOverlap;
        if (changedHarmony)
        {
            foreach (HarmonicEventPlan harmony in project.StructuredComposition.HarmonyPlan.Where((_, index) => index % Math.Max(2, 5 - Math.Min(pass, 3)) == pass % 2))
            {
                harmony.ScaleDegree = Mod(harmony.ScaleDegree + random.Pick(new[] { -3, -2, -1, 1, 2, 3 }), 7);
                harmony.Inversion = random.NextInt(0, Math.Max(1, harmony.ChordToneCount));
            }
        }
        ApplyMotif(project, blueprint, preserve with
        {
            MotifContour = Math.Max(0.05, preserve.MotifContour - pass * 0.10),
            MotifRhythm = Math.Max(0.05, preserve.MotifRhythm - pass * 0.08)
        }, random, pass);
        if (evaluation.RhythmNGramOverlap > blueprint.Generation.Originality.MaximumRhythmNGramOverlap)
        {
            ApplyGrooves(project, blueprint, preserve with { Groove = Math.Max(0.05, preserve.Groove - pass * 0.12) }, random);
        }
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => item.Role is TrackRole.Lead or TrackRole.CounterMelody or TrackRole.Bass)) role.Revision += pass;
        bool replanOrchestration = instrumentationPolicy != ReferenceInstrumentationPolicy.PreserveSourcePrograms &&
            (changedHarmony || pass % 2 == 0);
        _composer.RebuildFromCurrentPlans(project, replanOrchestration);
        ApplyPerformance(project, blueprint, preserve, random);
    }

    private static double NearestTension(ReferenceCompositionBlueprint blueprint, int sourceBar) =>
        blueprint.Tension.Points.Count == 0 ? 0.5 : blueprint.Tension.Points.OrderBy(item => Math.Abs(item.Bar - sourceBar)).First().Tension;

    private static List<int> BuildDegreeContour(IReadOnlyList<int> intervals)
    {
        List<int> result = [0];
        int current = 0;
        foreach (int interval in intervals)
        {
            current += Math.Clamp((int)Math.Round(interval / 2.0), -4, 4);
            result.Add(current);
        }
        return result;
    }

    private static int ChordToneCount(ReferenceChordQuality quality) => quality switch
    {
        ReferenceChordQuality.DominantSeventh or ReferenceChordQuality.MajorSeventh or ReferenceChordQuality.MinorSeventh or
        ReferenceChordQuality.HalfDiminishedSeventh or ReferenceChordQuality.DiminishedSeventh => 4,
        ReferenceChordQuality.Power => 3,
        _ => 3
    };

    private static int InversionFor(ReferenceHarmonicEvent chord, int degree)
    {
        if (chord.InversionBassPitchClass < 0 || chord.PitchClasses.Count == 0) return 0;
        int index = chord.PitchClasses.IndexOf(chord.InversionBassPitchClass);
        return index < 0 ? Mod(degree, Math.Max(1, ChordToneCount(chord.Quality))) : index;
    }

    private static string BuildTitle(ReferenceCompositionBlueprint blueprint, ReferenceGenerationMode mode, ulong seed)
    {
        string source = string.IsNullOrWhiteSpace(blueprint.Source.Title) ? "Reference" : blueprint.Source.Title;
        string suffix = (seed & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);
        return $"{source} - {Humanize(mode.ToString())} {suffix}";
    }

    private static string SymbolFor(FormSectionKind kind) => kind switch
    {
        FormSectionKind.Intro => "I",
        FormSectionKind.Verse => "A",
        FormSectionKind.Chorus => "B",
        FormSectionKind.Bridge => "C",
        FormSectionKind.Breakdown => "D",
        FormSectionKind.Solo => "S",
        FormSectionKind.Outro => "O",
        _ => "A"
    };

    private static MotifTransformationKind TreatmentFor(FormSectionKind kind, int variation) => kind switch
    {
        FormSectionKind.Intro => MotifTransformationKind.Fragment,
        FormSectionKind.Chorus when variation == 0 => MotifTransformationKind.Original,
        FormSectionKind.Chorus => MotifTransformationKind.Transpose,
        FormSectionKind.Bridge => MotifTransformationKind.Invert,
        FormSectionKind.Breakdown => MotifTransformationKind.Augment,
        FormSectionKind.Solo => MotifTransformationKind.Sequence,
        FormSectionKind.Outro => MotifTransformationKind.Fragment,
        _ => variation % 3 == 0 ? MotifTransformationKind.Original : MotifTransformationKind.Rotate
    };

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
    private static int Mod12(int value) => Mod(value, 12);
    private static string Humanize(string value) => string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    private sealed record ReferenceRoleBinding(CompositionRoleSettings Role, ReferenceTrackProfile Source);

    private readonly record struct PreservationProfile(
        double Form,
        double SectionLengths,
        double Harmony,
        double HarmonicRhythm,
        double Groove,
        double MotifContour,
        double MotifRhythm,
        double Arrangement,
        double Instrumentation,
        double Performance,
        double Tension)
    {
        public static PreservationProfile For(ReferenceGenerationPolicy policy, ReferenceGenerationMode mode)
        {
            PreservationProfile configured = new(
                policy.GetPreservation(ReferenceBlueprintDimension.Form),
                policy.GetPreservation(ReferenceBlueprintDimension.SectionLengths),
                policy.GetPreservation(ReferenceBlueprintDimension.Harmony),
                policy.GetPreservation(ReferenceBlueprintDimension.HarmonicRhythm),
                policy.GetPreservation(ReferenceBlueprintDimension.Groove),
                policy.GetPreservation(ReferenceBlueprintDimension.MotifContour),
                policy.GetPreservation(ReferenceBlueprintDimension.MotifRhythm),
                policy.GetPreservation(ReferenceBlueprintDimension.Arrangement),
                policy.GetPreservation(ReferenceBlueprintDimension.Instrumentation),
                policy.GetPreservation(ReferenceBlueprintDimension.Performance),
                policy.GetPreservation(ReferenceBlueprintDimension.Tension));
            return mode switch
            {
                ReferenceGenerationMode.CloseVariation => configured with
                {
                    Form = Math.Max(configured.Form, 0.97),
                    SectionLengths = Math.Max(configured.SectionLengths, 0.94),
                    Harmony = Math.Max(configured.Harmony, 0.88),
                    HarmonicRhythm = Math.Max(configured.HarmonicRhythm, 0.90),
                    Groove = Math.Max(configured.Groove, 0.88),
                    MotifContour = Math.Max(configured.MotifContour, 0.72),
                    MotifRhythm = Math.Max(configured.MotifRhythm, 0.72),
                    Arrangement = Math.Max(configured.Arrangement, 0.94),
                    Instrumentation = Math.Max(configured.Instrumentation, 1.0),
                    Performance = Math.Max(configured.Performance, 0.78),
                    Tension = Math.Max(configured.Tension, 0.94)
                },
                ReferenceGenerationMode.InspiredBy => configured with
                {
                    Form = Math.Max(configured.Form, 0.90),
                    SectionLengths = Math.Max(configured.SectionLengths, 0.86),
                    Harmony = Math.Max(configured.Harmony, 0.65),
                    HarmonicRhythm = Math.Max(configured.HarmonicRhythm, 0.76),
                    Groove = Math.Max(configured.Groove, 0.72),
                    MotifContour = Math.Max(configured.MotifContour, 0.45),
                    MotifRhythm = Math.Max(configured.MotifRhythm, 0.55),
                    Arrangement = Math.Max(configured.Arrangement, 0.86),
                    Instrumentation = Math.Max(configured.Instrumentation, 0.85),
                    Performance = Math.Max(configured.Performance, 0.62),
                    Tension = Math.Max(configured.Tension, 0.86)
                },
                ReferenceGenerationMode.StructuralRemix => configured with
                {
                    Form = Math.Max(configured.Form, 0.92),
                    SectionLengths = Math.Max(configured.SectionLengths, 0.82),
                    Harmony = Math.Min(configured.Harmony, 0.28),
                    Groove = Math.Min(configured.Groove, 0.42),
                    MotifContour = Math.Min(configured.MotifContour, 0.20),
                    Arrangement = Math.Max(configured.Arrangement, 0.78),
                    Instrumentation = Math.Max(configured.Instrumentation, 0.80),
                    Tension = Math.Max(configured.Tension, 0.86)
                },
                ReferenceGenerationMode.GenreTranslation => configured with
                {
                    Form = Math.Max(configured.Form, 0.85),
                    SectionLengths = Math.Max(configured.SectionLengths, 0.74),
                    Harmony = Math.Min(configured.Harmony, 0.44),
                    Groove = Math.Min(configured.Groove, 0.38),
                    MotifContour = Math.Min(configured.MotifContour, 0.34),
                    Arrangement = Math.Max(configured.Arrangement, 0.68),
                    Instrumentation = 0,
                    Performance = Math.Min(configured.Performance, 0.35),
                    Tension = Math.Max(configured.Tension, 0.80)
                },
                ReferenceGenerationMode.RadicalDescendant => configured with
                {
                    Form = Math.Min(configured.Form, 0.38),
                    SectionLengths = Math.Min(configured.SectionLengths, 0.35),
                    Harmony = Math.Min(configured.Harmony, 0.18),
                    HarmonicRhythm = Math.Min(configured.HarmonicRhythm, 0.30),
                    Groove = Math.Min(configured.Groove, 0.25),
                    MotifContour = Math.Min(configured.MotifContour, 0.12),
                    MotifRhythm = Math.Min(configured.MotifRhythm, 0.20),
                    Arrangement = Math.Min(configured.Arrangement, 0.42),
                    Instrumentation = 0,
                    Performance = Math.Min(configured.Performance, 0.25),
                    Tension = Math.Max(configured.Tension, 0.58)
                },
                ReferenceGenerationMode.InfiniteRadio => configured with
                {
                    Form = Math.Clamp(configured.Form, 0.55, 0.88),
                    Harmony = Math.Clamp(configured.Harmony, 0.25, 0.62),
                    MotifContour = Math.Clamp(configured.MotifContour, 0.12, 0.46),
                    Instrumentation = Math.Min(configured.Instrumentation, 0.20)
                },
                _ => configured
            };
        }
    }
}

internal static class DeterministicRandomReferenceExtensions
{
    public static double NextGaussian(this DeterministicRandom random)
    {
        double u1 = Math.Max(1e-12, random.NextDouble());
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
