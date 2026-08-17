using System.Globalization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;

namespace PlayromaMusic.Generators.Orchestration;

public sealed class ProceduralOrchestrationPlanner
{
    private const int CandidatePopulation = 36;
    private const int Generations = 14;

    public IReadOnlyList<SectionOrchestrationPlan> Plan(ProjectDocument project, StyleFamily family)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProceduralOrchestrationDocument document = project.Orchestration ??= new ProceduralOrchestrationDocument();
        document.Diagnostics.Clear();
        document.GenerationSequence++;
        if (document.Catalog.Presets.Count == 0)
        {
            document.Catalog = PresetCatalogFactory.CreateGeneralMidiFallback();
            document.Diagnostics.Add("No scanned SoundFont catalog was available; the complete General MIDI fallback catalog was used.");
        }

        EnsureRoleSelectors(project.StructuredComposition.Roles);
        InstrumentPresetDescriptor[] available = document.Catalog.Presets
            .Where(item => item.IsEnabled && !item.IsExcluded)
            .ToArray();
        if (available.Length == 0)
        {
            document.Catalog = PresetCatalogFactory.CreateGeneralMidiFallback();
            available = document.Catalog.Presets.ToArray();
            document.Diagnostics.Add("Every scanned preset was excluded; the General MIDI fallback catalog was restored.");
        }

        EnsembleArchetypeDefinition archetype = ResolveArchetype(project, family);
        document.Genome.EnsembleArchetypeId = archetype.Id;
        Dictionary<string, PresetUsageRecord> usage = document.UsageHistory
            .Where(item => !string.IsNullOrWhiteSpace(item.PresetKey))
            .GroupBy(item => item.PresetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastUsedSequence).First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> channels = AllocateLayerChannels(project.StructuredComposition.Roles);
        List<SectionOrchestrationPlan> plans = new();
        SectionOrchestrationPlan? previous = null;

        foreach (FormSectionPlan section in project.StructuredComposition.FormPlan.OrderBy(item => item.StartBar))
        {
            DeterministicRandom random = new(SeedDerivation.Derive(
                project.MasterSeed,
                "stage7",
                "orchestration",
                document.GenerationSequence.ToString(CultureInfo.InvariantCulture),
                section.Id));
            List<RoleLayerRequest> requests = BuildRequests(project, section, archetype, random);
            if (!document.Randomization.PermitSectionInstrumentChanges && previous is not null)
            {
                requests = requests.Select(request => request with
                {
                    FixedPresetKey = previous.Assignments.FirstOrDefault(item =>
                        item.RoleId == request.Role.Id && item.LayerIndex == request.LayerIndex)?.PresetKey ?? string.Empty
                }).ToList();
            }

            OrchestrationCandidate winner = SearchEnsemble(
                project,
                section,
                requests,
                available,
                usage,
                channels,
                previous,
                family,
                random);
            SectionOrchestrationPlan plan = new()
            {
                SectionId = section.Id,
                TargetEnergy = section.Energy,
                EnsembleScore = winner.Score,
                Assignments = winner.Assignments.Select(item => item.Clone()).ToList()
            };
            plans.Add(plan);
            previous = plan;
        }

        document.SectionPlans = plans;
        UpdateUsage(document, plans);
        document.LastCoverage = InstrumentCoverageAnalyzer.Analyze(document.Catalog, plans, document.UsageHistory);
        document.Diagnostics.Add($"Archetype '{archetype.Name}' resolved {plans.Sum(item => item.Assignments.Count)} section-role assignments across {available.Length} enabled presets.");
        document.Diagnostics.Add($"Current project uses {document.LastCoverage.UsedPresetCount} unique presets; catalog coverage is {document.LastCoverage.CoverageRatio:P1}.");
        return plans;
    }

    public static void EnsureRoleSelectors(IEnumerable<CompositionRoleSettings> roles)
    {
        foreach (CompositionRoleSettings role in roles)
        {
            if (role.InstrumentSelector is null || role.InstrumentSelector.Families.Count == 0)
            {
                role.InstrumentSelector = PresetCatalogFactory.DefaultSelector(role.Role, role.IsPercussion);
            }
            role.InstrumentSelector.MinimumPitch = Math.Max(role.InstrumentSelector.MinimumPitch, role.MinimumPitch);
            role.InstrumentSelector.MaximumPitch = Math.Min(role.InstrumentSelector.MaximumPitch, role.MaximumPitch);
        }
    }

    private static EnsembleArchetypeDefinition ResolveArchetype(ProjectDocument project, StyleFamily family)
    {
        ProceduralOrchestrationDocument document = project.Orchestration;
        if (!string.IsNullOrWhiteSpace(document.Genome.EnsembleArchetypeId))
        {
            EnsembleArchetypeDefinition? selected = EnsembleArchetypeLibrary.All.FirstOrDefault(item =>
                item.Id.Equals(document.Genome.EnsembleArchetypeId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) return selected;
        }

        DeterministicRandom random = new(SeedDerivation.Derive(project.MasterSeed, "stage7", "ensemble-archetype"));
        IReadOnlyList<EnsembleArchetypeDefinition> candidates = family switch
        {
            StyleFamily.Jazz => EnsembleArchetypeLibrary.All.Where(item => item.Id is "piano-rhythm-section" or "organ-trio" or "chamber-quartet" or "woodwind-choir" or "balanced-band").ToArray(),
            StyleFamily.Dance => EnsembleArchetypeLibrary.All.Where(item => item.Id is "synth-trio" or "percussion-forward" or "two-bass-machine" or "balanced-band").ToArray(),
            StyleFamily.Ambient => EnsembleArchetypeLibrary.All.Where(item => item.Id is "ambient-solo" or "cinematic-layers" or "chamber-quartet" or "unusual-colour-box").ToArray(),
            StyleFamily.Algorithmic => EnsembleArchetypeLibrary.All.Where(item => item.Id is "algorithmic-hocket" or "mallet-circle" or "two-bass-machine" or "unusual-colour-box").ToArray(),
            StyleFamily.Jrpg => EnsembleArchetypeLibrary.All.Where(item => item.Id is "cinematic-layers" or "chamber-quartet" or "woodwind-choir" or "balanced-band" or "percussion-forward").ToArray(),
            StyleFamily.RetroConsole => EnsembleArchetypeLibrary.All.Where(item => item.Id is "synth-trio" or "algorithmic-hocket" or "mallet-circle" or "power-trio" or "two-bass-machine").ToArray(),
            StyleFamily.Game => EnsembleArchetypeLibrary.All.Where(item => item.Id is "balanced-band" or "power-trio" or "cinematic-layers" or "synth-trio" or "ambient-solo" or "percussion-forward").ToArray(),
            _ => EnsembleArchetypeLibrary.All.Where(item => item.Id is "balanced-band" or "power-trio" or "piano-rhythm-section" or "cinematic-layers").ToArray()
        };
        return random.Pick(candidates);
    }

    private static List<RoleLayerRequest> BuildRequests(
        ProjectDocument project,
        FormSectionPlan section,
        EnsembleArchetypeDefinition archetype,
        DeterministicRandom random)
    {
        ProceduralOrchestrationDocument orchestration = project.Orchestration;
        int targetSize = Math.Clamp(
            (int)Math.Round(orchestration.Randomization.MinimumEnsembleSize +
                            section.Energy * (orchestration.Randomization.MaximumEnsembleSize - orchestration.Randomization.MinimumEnsembleSize)),
            archetype.MinimumRoles,
            archetype.MaximumRoles);
        List<RoleLayerRequest> requests = new();
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => item.Enabled))
        {
            ArrangementRoleState? arrangement = project.AdvancedComposition.ArrangementPlan
                .FirstOrDefault(item => item.SectionId == section.Id)?.Roles
                .FirstOrDefault(item => item.RoleId == role.Id);
            bool active = arrangement?.Active ?? true;
            EnsembleSlotDefinition? slot = archetype.Slots.FirstOrDefault(item => item.Role == role.Role);
            if (!active && slot?.Required != true) continue;
            if (slot is null && requests.Count >= targetSize) continue;

            int maxLayers = Math.Min(role.InstrumentSelector.MaximumLayers, slot?.MaximumLayers ?? 1);
            int minLayers = Math.Max(1, slot?.MinimumLayers ?? 1);
            int layers = minLayers;
            if (orchestration.Randomization.PermitLayering && maxLayers > minLayers)
            {
                double layerChance = Math.Clamp(section.Energy * 0.58 + orchestration.Randomization.Surprise / 250.0, 0.0, 0.85);
                while (layers < maxLayers && requests.Count + layers < targetSize && random.Chance(layerChance)) layers++;
            }
            for (int layer = 0; layer < layers; layer++)
            {
                InstrumentSelectorSettings selector = MergeSelectors(role.InstrumentSelector, slot?.Selector);
                requests.Add(new RoleLayerRequest(role, layer, selector, string.Empty));
            }
        }

        foreach (EnsembleSlotDefinition required in archetype.Slots.Where(item => item.Required))
        {
            if (requests.Any(item => item.Role.Role == required.Role)) continue;
            CompositionRoleSettings? role = project.StructuredComposition.Roles.FirstOrDefault(item => item.Role == required.Role);
            if (role is null) continue;
            requests.Add(new RoleLayerRequest(role, 0, MergeSelectors(role.InstrumentSelector, required.Selector), string.Empty));
        }
        if (requests.Count == 0)
        {
            CompositionRoleSettings? fallback = project.StructuredComposition.Roles.FirstOrDefault(item => item.Enabled)
                ?? project.StructuredComposition.Roles.FirstOrDefault();
            if (fallback is not null)
            {
                requests.Add(new RoleLayerRequest(
                    fallback,
                    0,
                    fallback.InstrumentSelector?.Clone() ?? PresetCatalogFactory.DefaultSelector(fallback.Role, fallback.IsPercussion),
                    string.Empty));
            }
        }
        return requests.Take(Math.Max(1, targetSize)).ToList();
    }

    private OrchestrationCandidate SearchEnsemble(
        ProjectDocument project,
        FormSectionPlan section,
        IReadOnlyList<RoleLayerRequest> requests,
        IReadOnlyList<InstrumentPresetDescriptor> presets,
        IReadOnlyDictionary<string, PresetUsageRecord> usage,
        IReadOnlyDictionary<string, int> channels,
        SectionOrchestrationPlan? previous,
        StyleFamily family,
        DeterministicRandom random)
    {
        List<OrchestrationCandidate> population = new();
        for (int index = 0; index < CandidatePopulation; index++)
        {
            List<InstrumentAssignmentPlan> assignments = requests.Select(request =>
                CreateAssignment(project, section, request, presets, usage, channels, family, random, index)).ToList();
            population.Add(new OrchestrationCandidate(assignments, ScoreEnsemble(project, section, assignments, presets, previous, family)));
        }

        for (int generation = 0; generation < Generations; generation++)
        {
            population = population.OrderByDescending(item => item.Score).Take(10).Select(item => item.Clone()).ToList();
            while (population.Count < CandidatePopulation)
            {
                OrchestrationCandidate parent = random.Pick(population.Take(Math.Min(6, population.Count)).ToArray()).Clone();
                if (parent.Assignments.Count > 0)
                {
                    int mutationIndex = random.NextInt(parent.Assignments.Count);
                    RoleLayerRequest request = requests[mutationIndex];
                    parent.Assignments[mutationIndex] = CreateAssignment(project, section, request, presets, usage, channels, family, random, generation + CandidatePopulation);
                }
                parent.Score = ScoreEnsemble(project, section, parent.Assignments, presets, previous, family);
                population.Add(parent);
            }
        }

        OrchestrationCandidate[] band = population.OrderByDescending(item => item.Score).Take(4).ToArray();
        double roll = random.NextDouble();
        return band[roll < 0.62 ? 0 : roll < 0.84 ? Math.Min(1, band.Length - 1) : roll < 0.96 ? Math.Min(2, band.Length - 1) : band.Length - 1];
    }

    private static InstrumentAssignmentPlan CreateAssignment(
        ProjectDocument project,
        FormSectionPlan section,
        RoleLayerRequest request,
        IReadOnlyList<InstrumentPresetDescriptor> presets,
        IReadOnlyDictionary<string, PresetUsageRecord> usage,
        IReadOnlyDictionary<string, int> channels,
        StyleFamily family,
        DeterministicRandom random,
        int salt)
    {
        IEnumerable<InstrumentPresetDescriptor> filtered = presets.Where(preset => HardCompatible(preset, request));
        if (!string.IsNullOrWhiteSpace(request.FixedPresetKey))
        {
            InstrumentPresetDescriptor? fixedPreset = presets.FirstOrDefault(item => item.Key.Equals(request.FixedPresetKey, StringComparison.OrdinalIgnoreCase));
            if (fixedPreset is not null) filtered = new[] { fixedPreset };
        }
        InstrumentPresetDescriptor[] candidates = filtered.ToArray();
        if (candidates.Length == 0)
        {
            candidates = presets.Where(item => item.IsDrumKit == request.Role.IsPercussion).ToArray();
        }
        if (candidates.Length == 0) candidates = presets.ToArray();

        List<(InstrumentPresetDescriptor Preset, double Score)> ranked = candidates
            .Select(item => (item, ScorePreset(project, section, request, item, usage, family)))
            .OrderByDescending(item => item.Item2)
            .Take(Math.Min(32, candidates.Length))
            .ToList();
        double temperature = project.Orchestration.Randomization.Mode switch
        {
            OrchestrationRandomizationMode.Coherent => 0.12,
            OrchestrationRandomizationMode.Adventurous => 0.28,
            OrchestrationRandomizationMode.Experimental => 0.48,
            OrchestrationRandomizationMode.Chaos => 0.82,
            _ => 0.34
        };
        int band = Math.Max(1, Math.Min(ranked.Count, 1 + (int)Math.Round(temperature * Math.Min(20, ranked.Count - 1))));
        int chosenIndex = salt == 0 ? 0 : random.NextInt(band);
        (InstrumentPresetDescriptor preset, double score) = ranked[chosenIndex];
        RoleDistributionStrategy strategy = PickStrategy(project, request, random);
        int octaveOffset = strategy == RoleDistributionStrategy.OctaveDoubling && request.LayerIndex > 0
            ? (random.Chance(0.72) ? 1 : -1)
            : 0;
        int channel = channels.GetValueOrDefault($"{request.Role.Id}|{request.LayerIndex}", request.Role.MidiChannel);
        double pan = request.LayerIndex == 0 ? 0 : request.LayerIndex % 2 == 1 ? -0.28 : 0.28;
        return new InstrumentAssignmentPlan
        {
            SectionId = section.Id,
            RoleId = request.Role.Id,
            LayerIndex = request.LayerIndex,
            PresetKey = preset.Key,
            PresetName = preset.Name,
            SoundFontId = preset.SoundFontId,
            Bank = preset.Bank,
            Program = preset.Program,
            MidiChannel = request.Role.IsPercussion ? 9 : channel,
            Active = true,
            OctaveOffset = octaveOffset,
            Volume = request.LayerIndex == 0 ? 1.0 : 0.72,
            Pan = pan,
            Strategy = strategy,
            SuitabilityScore = score,
            Rationale = BuildRationale(request, preset, score)
        };
    }

    private static bool HardCompatible(InstrumentPresetDescriptor preset, RoleLayerRequest request)
    {
        InstrumentSelectorSettings selector = request.Selector;
        if (preset.IsDrumKit != request.Role.IsPercussion) return false;
        if (selector.Families.Count > 0 && !selector.Families.Contains(preset.Family)) return false;
        if (selector.RequiredTags.Any(tag => !preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))) return false;
        if (preset.SuggestedHighNote < request.Role.MinimumPitch || preset.SuggestedLowNote > request.Role.MaximumPitch) return false;
        if (preset.Timbre.Attack < selector.MinimumAttack || preset.Timbre.Attack > selector.MaximumAttack) return false;
        if (preset.Timbre.Sustain < selector.MinimumSustain || preset.Timbre.Sustain > selector.MaximumSustain) return false;
        if (preset.Timbre.Brightness < selector.MinimumBrightness || preset.Timbre.Brightness > selector.MaximumBrightness) return false;
        if (preset.Timbre.Percussiveness < selector.MinimumPercussiveness || preset.Timbre.Percussiveness > selector.MaximumPercussiveness) return false;
        return true;
    }

    private static double ScorePreset(
        ProjectDocument project,
        FormSectionPlan section,
        RoleLayerRequest request,
        InstrumentPresetDescriptor preset,
        IReadOnlyDictionary<string, PresetUsageRecord> usage,
        StyleFamily family)
    {
        InstrumentSelectorSettings selector = request.Selector;
        double score = selector.Families.Count == 0 || selector.Families.Contains(preset.Family) ? 42 : -18;
        score += selector.RequiredTags.Count(tag => preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) * 12;
        score += selector.PreferredTags.Count(tag => preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) * 7;
        score -= selector.AvoidTags.Count(tag => preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) * 11;
        int overlap = Math.Max(0, Math.Min(request.Role.MaximumPitch, preset.SuggestedHighNote) - Math.Max(request.Role.MinimumPitch, preset.SuggestedLowNote));
        int roleSpan = Math.Max(1, request.Role.MaximumPitch - request.Role.MinimumPitch);
        score += 18 * overlap / (double)roleSpan;
        double targetBrightness = Math.Clamp(section.Energy * 0.72 + (request.Role.Role == TrackRole.Texture ? 0.15 : 0.2), 0, 1);
        score += 9 * (1 - Math.Abs(preset.Timbre.Brightness - targetBrightness));
        score += 5 * TimbreRoleAffinity(request.Role.Role, preset.Timbre);
        IEnumerable<StyleTraitDefinition> activeTraits = project.Orchestration.Genome.TraitIds
            .Select(id => StyleTraitLibrary.All.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .OfType<StyleTraitDefinition>();
        foreach (StyleTraitDefinition trait in activeTraits)
        {
            score += trait.PreferredTags.Count(tag => preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) * 2.5;
            score -= trait.AvoidTags.Count(tag => preset.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) * 3.5;
        }
        score += FamilyAffinity(family, preset.Family) * selector.StyleAffinityWeight;
        PresetUsageRecord? record = usage.GetValueOrDefault(preset.Key);
        int uses = record?.UseCount ?? 0;
        long age = project.Orchestration.GenerationSequence - (record?.LastUsedSequence ?? 0);
        double novelty = project.Orchestration.Randomization.Exploration switch
        {
            PresetExplorationMode.Familiar => uses > 0 ? 8 : -2,
            PresetExplorationMode.Balanced => Math.Min(8, age * 0.4) - uses * 0.35,
            PresetExplorationMode.Underused => 16 / (1 + uses) + Math.Min(10, age * 0.6),
            PresetExplorationMode.NeverUsed => uses == 0 ? 28 : -18,
            PresetExplorationMode.SoundFontExpedition => uses == 0 ? 34 : -uses * 5,
            _ => 0
        };
        if (project.Orchestration.Randomization.FavorCompleteSoundFontCoverage)
        {
            novelty += uses == 0 ? 20 : -Math.Min(20, uses * 2.5);
        }
        score += novelty * selector.NoveltyWeight;

        if (selector.RandomWeight > 0)
        {
            DeterministicRandom jitter = new(SeedDerivation.Derive(
                project.MasterSeed,
                "stage7",
                "preset-jitter",
                project.Orchestration.GenerationSequence.ToString(CultureInfo.InvariantCulture),
                section.Id,
                request.Role.Id,
                request.LayerIndex.ToString(CultureInfo.InvariantCulture),
                preset.Key));
            double surpriseScale = 0.2 + project.Orchestration.Randomization.Surprise / 100.0;
            score += (jitter.NextDouble() - 0.5) * 18 * selector.RandomWeight * surpriseScale;
        }
        if (preset.IsFavorite) score += 12;
        if (preset.WasAudioProfiled) score += preset.Timbre.Confidence * 4;
        return score;
    }

    private static double ScoreEnsemble(
        ProjectDocument project,
        FormSectionPlan section,
        IReadOnlyList<InstrumentAssignmentPlan> assignments,
        IReadOnlyList<InstrumentPresetDescriptor> presets,
        SectionOrchestrationPlan? previous,
        StyleFamily family)
    {
        if (assignments.Count == 0) return double.NegativeInfinity;
        Dictionary<string, InstrumentPresetDescriptor> byKey = presets
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<InstrumentPresetDescriptor> selected = assignments.Select(item => byKey.GetValueOrDefault(item.PresetKey)).Where(item => item is not null).Cast<InstrumentPresetDescriptor>().ToList();
        double score = assignments.Average(item => item.SuitabilityScore);
        int uniqueFamilies = selected.Select(item => item.Family).Distinct().Count();
        int uniquePresets = selected.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        score += uniqueFamilies * 2.8 + uniquePresets * 1.8;
        score -= (assignments.Count - uniquePresets) * (project.Orchestration.Randomization.Mode == OrchestrationRandomizationMode.Coherent ? 2 : 6);
        double brightness = selected.Count == 0 ? 0.5 : selected.Average(item => item.Timbre.Brightness);
        double density = selected.Count == 0 ? 0.5 : selected.Average(item => item.Timbre.Density);
        double attacks = selected.Count == 0 ? 0.5 : selected.Average(item => item.Timbre.Attack);
        double sustain = selected.Count == 0 ? 0.5 : selected.Average(item => item.Timbre.Sustain);
        score += 12 * (1 - Math.Abs(brightness - Math.Clamp(0.25 + section.Energy * 0.58, 0, 1)));
        score += 8 * (1 - Math.Abs(density - Math.Clamp(0.2 + section.Energy * 0.65, 0, 1)));
        if (attacks > 0.78 && sustain < 0.25) score -= 8;
        if (sustain > 0.8 && attacks < 0.25 && assignments.All(item => item.RoleId != project.StructuredComposition.Roles.FirstOrDefault(role => role.Role == TrackRole.Drums)?.Id)) score -= 5;
        score -= RegisterCollisionPenalty(assignments, project.StructuredComposition.Roles);
        if (previous is not null)
        {
            int changed = assignments.Count(item => previous.Assignments.All(old => old.RoleId != item.RoleId || old.LayerIndex != item.LayerIndex || old.PresetKey != item.PresetKey));
            double desiredChange = project.Orchestration.Randomization.PermitSectionInstrumentChanges
                ? 0.15 + project.Orchestration.Randomization.Surprise / 160.0
                : 0;
            score += 10 * (1 - Math.Abs(changed / (double)Math.Max(1, assignments.Count) - desiredChange));

            double maximumDistance = InstrumentSubstitutionGraph.MaximumDistanceForSurprise(project.Orchestration.Randomization.Surprise);
            foreach (InstrumentAssignmentPlan assignment in assignments)
            {
                InstrumentAssignmentPlan? old = previous.Assignments.FirstOrDefault(item =>
                    item.RoleId == assignment.RoleId && item.LayerIndex == assignment.LayerIndex);
                if (old is null || old.PresetKey.Equals(assignment.PresetKey, StringComparison.OrdinalIgnoreCase)) continue;
                double distance = InstrumentSubstitutionGraph.Distance(
                    byKey.GetValueOrDefault(old.PresetKey),
                    byKey.GetValueOrDefault(assignment.PresetKey));
                if (distance > maximumDistance)
                {
                    score -= (distance - maximumDistance) * 24;
                }
                else
                {
                    score += Math.Max(0, 2.5 - Math.Abs(distance - desiredChange) * 3);
                }
            }
        }
        score += selected.Sum(item => FamilyAffinity(family, item.Family)) / Math.Max(1, selected.Count);
        return score;
    }

    private static double RegisterCollisionPenalty(IReadOnlyList<InstrumentAssignmentPlan> assignments, IReadOnlyList<CompositionRoleSettings> roles)
    {
        double penalty = 0;
        for (int a = 0; a < assignments.Count; a++)
        {
            CompositionRoleSettings? roleA = roles.FirstOrDefault(item => item.Id == assignments[a].RoleId);
            if (roleA is null) continue;
            for (int b = a + 1; b < assignments.Count; b++)
            {
                CompositionRoleSettings? roleB = roles.FirstOrDefault(item => item.Id == assignments[b].RoleId);
                if (roleB is null) continue;
                int overlap = Math.Max(0, Math.Min(roleA.MaximumPitch, roleB.MaximumPitch) - Math.Max(roleA.MinimumPitch, roleB.MinimumPitch));
                int smallerSpan = Math.Max(1, Math.Min(roleA.MaximumPitch - roleA.MinimumPitch, roleB.MaximumPitch - roleB.MinimumPitch));
                if (overlap / (double)smallerSpan > 0.7 && roleA.Role != roleB.Role) penalty += 2.4;
            }
        }
        return penalty;
    }

    private static RoleDistributionStrategy PickStrategy(ProjectDocument project, RoleLayerRequest request, DeterministicRandom random)
    {
        IReadOnlyList<RoleDistributionStrategy> allowed = request.Selector.AllowedStrategies.Count == 0
            ? new[] { RoleDistributionStrategy.SingleInstrument }
            : request.Selector.AllowedStrategies;
        if (request.LayerIndex == 0 && allowed.Contains(RoleDistributionStrategy.SingleInstrument) && random.Chance(0.58))
            return RoleDistributionStrategy.SingleInstrument;
        return random.Pick(allowed);
    }

    private static string BuildRationale(RoleLayerRequest request, InstrumentPresetDescriptor preset, double score)
    {
        string tags = string.Join(", ", preset.Tags.Take(4));
        return $"{preset.Family} preset matched {request.Role.Role} layer {request.LayerIndex + 1}; score {score:0.0}; tags {tags}.";
    }

    private static InstrumentSelectorSettings MergeSelectors(InstrumentSelectorSettings primary, InstrumentSelectorSettings? overlay)
    {
        InstrumentSelectorSettings result = primary.Clone();
        if (overlay is null) return result;
        if (overlay.Families.Count > 0)
        {
            List<InstrumentFamily> intersection = result.Families.Intersect(overlay.Families).ToList();
            result.Families = intersection.Count > 0 ? intersection : overlay.Families.ToList();
        }
        result.RequiredTags = result.RequiredTags.Union(overlay.RequiredTags, StringComparer.OrdinalIgnoreCase).ToList();
        result.PreferredTags = result.PreferredTags.Union(overlay.PreferredTags, StringComparer.OrdinalIgnoreCase).ToList();
        result.AvoidTags = result.AvoidTags.Union(overlay.AvoidTags, StringComparer.OrdinalIgnoreCase).ToList();
        result.MinimumAttack = Math.Max(result.MinimumAttack, overlay.MinimumAttack);
        result.MaximumAttack = Math.Min(result.MaximumAttack, overlay.MaximumAttack);
        result.MinimumSustain = Math.Max(result.MinimumSustain, overlay.MinimumSustain);
        result.MaximumSustain = Math.Min(result.MaximumSustain, overlay.MaximumSustain);
        result.MinimumBrightness = Math.Max(result.MinimumBrightness, overlay.MinimumBrightness);
        result.MaximumBrightness = Math.Min(result.MaximumBrightness, overlay.MaximumBrightness);
        result.MinimumPercussiveness = Math.Max(result.MinimumPercussiveness, overlay.MinimumPercussiveness);
        result.MaximumPercussiveness = Math.Min(result.MaximumPercussiveness, overlay.MaximumPercussiveness);
        result.MaximumLayers = Math.Min(result.MaximumLayers, overlay.MaximumLayers);
        result.AllowedStrategies = result.AllowedStrategies.Intersect(overlay.AllowedStrategies).DefaultIfEmpty(RoleDistributionStrategy.SingleInstrument).ToList();
        return result;
    }

    private static Dictionary<string, int> AllocateLayerChannels(IReadOnlyList<CompositionRoleSettings> roles)
    {
        HashSet<int> used = roles.Select(item => item.MidiChannel).Append(9).ToHashSet();
        Queue<int> free = new(Enumerable.Range(0, 16).Where(item => item != 9 && !used.Contains(item)));
        Dictionary<string, int> result = new(StringComparer.Ordinal);
        foreach (CompositionRoleSettings role in roles)
        {
            result[$"{role.Id}|0"] = role.IsPercussion ? 9 : role.MidiChannel;
            for (int layer = 1; layer < Math.Clamp(role.InstrumentSelector?.MaximumLayers ?? 1, 1, 4); layer++)
            {
                result[$"{role.Id}|{layer}"] = role.IsPercussion ? 9 : free.Count > 0 ? free.Dequeue() : role.MidiChannel;
            }
        }
        return result;
    }

    private static void UpdateUsage(ProceduralOrchestrationDocument document, IEnumerable<SectionOrchestrationPlan> plans)
    {
        Dictionary<string, PresetUsageRecord> byKey = document.UsageHistory
            .Where(item => !string.IsNullOrWhiteSpace(item.PresetKey))
            .GroupBy(item => item.PresetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastUsedSequence).First(), StringComparer.OrdinalIgnoreCase);
        foreach (InstrumentAssignmentPlan assignment in plans.SelectMany(item => item.Assignments).Where(item => item.Active))
        {
            if (!byKey.TryGetValue(assignment.PresetKey, out PresetUsageRecord? record))
            {
                record = new PresetUsageRecord { PresetKey = assignment.PresetKey };
                byKey[assignment.PresetKey] = record;
            }
            record.UseCount++;
            if (assignment.LayerIndex == 0) record.PrimaryRoleCount++;
            record.LastUsedSequence = document.GenerationSequence;
        }
        document.UsageHistory = byKey.Values.OrderBy(item => item.PresetKey, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static double TimbreRoleAffinity(TrackRole role, TimbreProfile profile) => role switch
    {
        TrackRole.Bass => (1 - profile.Brightness) * 0.5 + profile.Sustain * 0.5,
        TrackRole.Drums => profile.Percussiveness,
        TrackRole.Arpeggio => profile.Attack * 0.6 + profile.Percussiveness * 0.4,
        TrackRole.Texture => profile.Sustain * 0.6 + (1 - profile.Attack) * 0.4,
        TrackRole.Chords or TrackRole.Harmony => profile.Sustain * 0.45 + profile.Density * 0.35 + (1 - profile.Percussiveness) * 0.2,
        _ => profile.PitchStability * 0.45 + profile.Attack * 0.25 + profile.Sustain * 0.3
    };

    private static double FamilyAffinity(StyleFamily style, InstrumentFamily family) => style switch
    {
        StyleFamily.Pop when family is InstrumentFamily.Piano or InstrumentFamily.Guitar or InstrumentFamily.Bass or InstrumentFamily.SynthPad or InstrumentFamily.SynthLead or InstrumentFamily.Drums => 6,
        StyleFamily.Jazz when family is InstrumentFamily.Piano or InstrumentFamily.Organ or InstrumentFamily.Bass or InstrumentFamily.Reed or InstrumentFamily.Brass or InstrumentFamily.Drums => 7,
        StyleFamily.Dance when family is InstrumentFamily.SynthLead or InstrumentFamily.SynthPad or InstrumentFamily.Bass or InstrumentFamily.Percussive or InstrumentFamily.Drums => 8,
        StyleFamily.Ambient when family is InstrumentFamily.SynthPad or InstrumentFamily.Ensemble or InstrumentFamily.Strings or InstrumentFamily.SynthEffects or InstrumentFamily.Pipe => 8,
        StyleFamily.Algorithmic when family is InstrumentFamily.ChromaticPercussion or InstrumentFamily.SynthEffects or InstrumentFamily.Ethnic or InstrumentFamily.Percussive or InstrumentFamily.SoundEffects => 7,
        StyleFamily.Jrpg when family is InstrumentFamily.Strings or InstrumentFamily.Ensemble or InstrumentFamily.Brass or InstrumentFamily.Reed or InstrumentFamily.Pipe or InstrumentFamily.Piano or InstrumentFamily.ChromaticPercussion or InstrumentFamily.Ethnic or InstrumentFamily.Drums => 8,
        StyleFamily.RetroConsole when family is InstrumentFamily.SynthLead or InstrumentFamily.ChromaticPercussion or InstrumentFamily.SynthPad or InstrumentFamily.Bass or InstrumentFamily.Percussive or InstrumentFamily.Drums => 8,
        StyleFamily.Game when family is InstrumentFamily.Guitar or InstrumentFamily.Bass or InstrumentFamily.SynthLead or InstrumentFamily.SynthPad or InstrumentFamily.SynthEffects or InstrumentFamily.Strings or InstrumentFamily.Ensemble or InstrumentFamily.Drums or InstrumentFamily.SoundEffects => 6,
        _ => 1
    };

    private sealed record RoleLayerRequest(
        CompositionRoleSettings Role,
        int LayerIndex,
        InstrumentSelectorSettings Selector,
        string FixedPresetKey);

    private sealed class OrchestrationCandidate
    {
        public OrchestrationCandidate(List<InstrumentAssignmentPlan> assignments, double score)
        {
            Assignments = assignments;
            Score = score;
        }

        public List<InstrumentAssignmentPlan> Assignments { get; }
        public double Score { get; set; }
        public OrchestrationCandidate Clone() => new(Assignments.Select(item => item.Clone()).ToList(), Score);
    }
}

internal static class InstrumentAssignmentCloneExtensions
{
    public static InstrumentAssignmentPlan Clone(this InstrumentAssignmentPlan source) => new()
    {
        SectionId = source.SectionId,
        RoleId = source.RoleId,
        LayerIndex = source.LayerIndex,
        PresetKey = source.PresetKey,
        PresetName = source.PresetName,
        SoundFontId = source.SoundFontId,
        Bank = source.Bank,
        Program = source.Program,
        MidiChannel = source.MidiChannel,
        Active = source.Active,
        OctaveOffset = source.OctaveOffset,
        Volume = source.Volume,
        Pan = source.Pan,
        Strategy = source.Strategy,
        SuitabilityScore = source.SuitabilityScore,
        Rationale = source.Rationale
    };
}

public static class InstrumentCoverageAnalyzer
{
    public static InstrumentCoverageStatistics Analyze(
        InstrumentCatalogDocument catalog,
        IEnumerable<SectionOrchestrationPlan> plans,
        IEnumerable<PresetUsageRecord>? history = null)
    {
        InstrumentAssignmentPlan[] assignments = plans.SelectMany(item => item.Assignments).Where(item => item.Active).ToArray();
        HashSet<string> used = assignments.Select(item => item.PresetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InstrumentPresetDescriptor> presets = catalog.Presets
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        InstrumentCoverageStatistics statistics = new()
        {
            AvailablePresetCount = catalog.Presets.Count(item => item.IsEnabled && !item.IsExcluded),
            UsedPresetCount = used.Count,
            CoverageRatio = used.Count / (double)Math.Max(1, catalog.Presets.Count(item => item.IsEnabled && !item.IsExcluded)),
            MeanUniquePresetsPerSong = used.Count,
            DuplicatePresetRate = assignments.Length == 0 ? 0 : 1 - used.Count / (double)assignments.Length,
            MeanRoleSuitability = assignments.Length == 0 ? 0 : assignments.Average(item => item.SuitabilityScore),
            MeanSectionContrast = MeanContrast(plans.ToArray()),
            MeanTimbralDiversity = TimbralDiversity(used.Select(key => presets.GetValueOrDefault(key)).Where(item => item is not null).Cast<InstrumentPresetDescriptor>().ToArray()),
            RegisterCollisionRate = RegisterCollisionRate(plans),
            NeverUsedPresetKeys = catalog.Presets.Where(item => item.IsEnabled && !item.IsExcluded && !used.Contains(item.Key)).Select(item => item.Key).ToList()
        };
        foreach (InstrumentPresetDescriptor preset in used.Select(key => presets.GetValueOrDefault(key)).Where(item => item is not null).Cast<InstrumentPresetDescriptor>())
        {
            statistics.FamilyUsage[preset.Family] = statistics.FamilyUsage.GetValueOrDefault(preset.Family) + 1;
        }
        return statistics;
    }

    private static double MeanContrast(IReadOnlyList<SectionOrchestrationPlan> plans)
    {
        if (plans.Count < 2) return 0;
        double sum = 0;
        for (int index = 1; index < plans.Count; index++)
        {
            HashSet<string> previous = plans[index - 1].Assignments.Select(item => item.PresetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> current = plans[index].Assignments.Select(item => item.PresetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int union = previous.Union(current).Count();
            int intersection = previous.Intersect(current).Count();
            sum += union == 0 ? 0 : 1 - intersection / (double)union;
        }
        return sum / (plans.Count - 1);
    }

    private static double TimbralDiversity(IReadOnlyList<InstrumentPresetDescriptor> presets)
    {
        if (presets.Count < 2) return presets.Count;
        double sum = 0;
        int pairs = 0;
        for (int a = 0; a < presets.Count; a++)
        for (int b = a + 1; b < presets.Count; b++)
        {
            TimbreProfile x = presets[a].Timbre;
            TimbreProfile y = presets[b].Timbre;
            sum += Math.Sqrt(
                Math.Pow(x.Attack - y.Attack, 2) +
                Math.Pow(x.Sustain - y.Sustain, 2) +
                Math.Pow(x.Brightness - y.Brightness, 2) +
                Math.Pow(x.Density - y.Density, 2) +
                Math.Pow(x.Percussiveness - y.Percussiveness, 2)) / Math.Sqrt(5);
            pairs++;
        }
        return pairs == 0 ? 0 : sum / pairs;
    }

    private static double RegisterCollisionRate(IEnumerable<SectionOrchestrationPlan> plans)
    {
        int assignments = 0;
        int collisions = 0;
        foreach (SectionOrchestrationPlan section in plans)
        {
            InstrumentAssignmentPlan[] active = section.Assignments.Where(item => item.Active).ToArray();
            assignments += active.Length;
            collisions += active
                .Where(item => item.MidiChannel != 9)
                .GroupBy(item => item.MidiChannel)
                .Sum(group => Math.Max(0, group.Count() - 1));
        }
        return assignments == 0 ? 0 : collisions / (double)assignments;
    }
}
