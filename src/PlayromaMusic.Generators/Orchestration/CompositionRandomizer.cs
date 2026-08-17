using System.Globalization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Generators.Structured;

namespace PlayromaMusic.Generators.Orchestration;

public sealed class CompositionRandomizer
{
    private static readonly string[] StructuralFamilies =
    {
        "verse-chorus", "through-composed", "theme-and-variations", "aaba", "blues-cycle", "rondo", "loop-build", "ambient-evolution", "algorithmic-process"
    };
    private static readonly string[] HarmonicLanguages =
    {
        "diatonic", "modal", "blues", "extended-jazz", "chromatic-mediant", "drone", "quartal", "minimalist", "atonal-colour", "set-based"
    };
    private static readonly string[] RhythmicLanguages =
    {
        "straight", "swing", "shuffle", "syncopated", "euclidean", "breakbeat", "polymetric", "rubato", "sparse", "cellular"
    };
    private static readonly string[] OrchestrationLanguages =
    {
        "acoustic", "electronic", "chamber", "orchestral", "lo-fi", "percussive", "vocal-like", "metallic", "organic", "hybrid"
    };
    private static readonly string[] EmotionalModifiers =
    {
        "bright", "dark", "triumphant", "anxious", "nostalgic", "playful", "mysterious", "serene", "aggressive", "melancholic"
    };
    private static readonly string[] PerformanceModifiers =
    {
        "tight", "loose", "humanized", "mechanical", "sparse", "dense", "intimate", "grand", "dry", "flowing"
    };

    private readonly StructuredCompositionService _composer;

    public CompositionRandomizer(StructuredCompositionService? composer = null)
    {
        _composer = composer ?? new StructuredCompositionService();
    }

    public ProjectDocument CreateNewUniverse(
        TotalRandomizationSettings? settings = null,
        InstrumentCatalogDocument? catalog = null,
        ulong? deterministicSeed = null)
    {
        TotalRandomizationSettings normalized = NormalizeSettings(settings ?? new TotalRandomizationSettings());
        EntropySeed entropy = deterministicSeed.HasValue
            ? new EntropySeed(deterministicSeed.Value, deterministicSeed.Value.ToString("X16", CultureInfo.InvariantCulture))
            : normalized.UseCryptographicSeedForNewUniverses
                ? CryptographicSeedGenerator.Create()
                : CreatePseudoRandomEntropy();
        ulong seed = entropy.Seed;
        DeterministicRandom random = new(SeedDerivation.Derive(seed, "stage7", "new-universe"));
        IReadOnlyList<StructuredStyleDefinition> styles = StructuredStyleLibrary.All.Where(item => item.Family != StyleFamily.Legacy || item.Id == "tonal-laboratory").ToArray();
        StructuredStyleDefinition primary = random.Pick(styles);
        ProjectDocument project;
        List<string> sourceStyles = new() { primary.Id };
        List<(StructuredStyleDefinition Style, double Weight)> additionalInfluences = new();
        if (normalized.AllowStyleHybridization && normalized.MaximumStyleSources > 1 && random.Chance(HybridChance(normalized.Mode)))
        {
            StructuredStyleDefinition secondary = random.Pick(styles.Where(item => item.Id != primary.Id).ToArray());
            int weight = normalized.Mode switch
            {
                OrchestrationRandomizationMode.Coherent => random.NextInt(20, 41),
                OrchestrationRandomizationMode.Adventurous => random.NextInt(25, 61),
                OrchestrationRandomizationMode.Experimental => random.NextInt(15, 86),
                OrchestrationRandomizationMode.Chaos => random.NextInt(1, 100),
                _ => random.NextInt(25, 56)
            };
            StyleBlendSettings blend = new()
            {
                Enabled = true,
                PrimaryPackId = primary.Id,
                SecondaryPackId = secondary.Id,
                SecondaryWeight = weight,
                BlendForms = random.Chance(0.72),
                BlendHarmony = random.Chance(0.86),
                BlendRhythms = random.Chance(0.9),
                BlendInstruments = true,
                BlendScoring = random.Chance(0.8)
            };
            try
            {
                project = _composer.CreateHybridProject(seed, blend);
                sourceStyles.Add(secondary.Id);
                if (normalized.MaximumStyleSources >= 3 && random.Chance(HybridChance(normalized.Mode) * 0.55))
                {
                    StructuredStyleDefinition tertiary = random.Pick(styles.Where(item => item.Id != primary.Id && item.Id != secondary.Id).ToArray());
                    sourceStyles.Add(tertiary.Id);
                    additionalInfluences.Add((tertiary, 0.12 + random.NextDouble() * 0.28));
                }
            }
            catch (KeyNotFoundException)
            {
                project = _composer.CreateProject(seed, primary.Id);
            }
            catch (InvalidOperationException)
            {
                project = _composer.CreateProject(seed, primary.Id);
            }
        }
        else
        {
            project = _composer.CreateProject(seed, primary.Id);
        }

        project.Orchestration.Randomization = normalized;
        project.Orchestration.Catalog = CloneCatalog(catalog ?? PresetCatalogFactory.CreateGeneralMidiFallback());
        foreach ((StructuredStyleDefinition influence, double weight) in additionalInfluences)
        {
            ApplyAdditionalStyleInfluence(project, influence, weight);
        }
        CompositionGenome genome = BuildGenome(project, entropy.Hex, sourceStyles, random);
        project.Orchestration.Genome = genome;
        ApplyGenome(project, random);
        _composer.RegenerateAdvanced(project);
        project.Tags.RemoveAll(item => item.StartsWith("random-mode:", StringComparison.OrdinalIgnoreCase));
        project.Tags.Add("stage7");
        project.Tags.Add("procedural-orchestration");
        project.Tags.Add("total-randomizer");
        project.Tags.Add("random-mode:" + normalized.Mode.ToString().ToLowerInvariant());
        project.Title = BuildUniverseTitle(project, genome);
        project.Song.Title = project.Title;
        return project;
    }

    public void RandomizeDomain(ProjectDocument project, RandomizationDomain domain)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Orchestration.Randomization.Locks.IsLocked(domain)) return;
        project.Orchestration.GenerationSequence++;
        ulong revisionSeed = SeedDerivation.Derive(
            project.MasterSeed,
            "stage7",
            "domain-randomize",
            domain.ToString(),
            project.Orchestration.GenerationSequence.ToString(CultureInfo.InvariantCulture));
        DeterministicRandom random = new(revisionSeed);
        switch (domain)
        {
            case RandomizationDomain.Everything:
                if (HasAnyDomainLock(project.Orchestration.Randomization.Locks))
                {
                    foreach (RandomizationDomain child in Enum.GetValues<RandomizationDomain>().Where(item => item is not RandomizationDomain.Everything))
                    {
                        if (!project.Orchestration.Randomization.Locks.IsLocked(child)) RandomizeDomain(project, child);
                    }
                }
                else
                {
                    ProjectDocument replacement = CreateNewUniverse(project.Orchestration.Randomization, project.Orchestration.Catalog);
                    CopyInto(project, replacement);
                }
                break;
            case RandomizationDomain.Style:
                StructuredStyleDefinition style = random.Pick(StructuredStyleLibrary.All);
                ProjectDocument styled = _composer.ApplyStyle(project, style.Id);
                styled.Orchestration.Catalog = CloneCatalog(project.Orchestration.Catalog);
                styled.Orchestration.Randomization = CloneSettings(project.Orchestration.Randomization);
                styled.Orchestration.GenerationSequence = project.Orchestration.GenerationSequence;
                _composer.RegenerateAdvanced(styled);
                CopyInto(project, styled);
                project.Orchestration.Genome.SourceStyleIds = new List<string> { style.Id };
                break;
            case RandomizationDomain.Form:
                project.Orchestration.Genome.StructuralFamily = random.Pick(StructuralFamilies);
                _composer.RegenerateStructure(project);
                ApplyStructuralDimension(project, project.Orchestration.Genome.StructuralFamily);
                _composer.RegenerateAdvanced(project);
                break;
            case RandomizationDomain.Harmony:
                project.Orchestration.Genome.HarmonicLanguage = random.Pick(HarmonicLanguages);
                ApplyHarmonicDimension(project, project.Orchestration.Genome.HarmonicLanguage, random);
                _composer.RegenerateAdvanced(project);
                break;
            case RandomizationDomain.Ensemble:
                project.Orchestration.Genome.EnsembleArchetypeId = random.Pick(EnsembleArchetypeLibrary.All).Id;
                _composer.RegenerateAdvanced(project);
                break;
            case RandomizationDomain.Instruments:
                _composer.RegenerateAdvanced(project);
                break;
            case RandomizationDomain.Rhythm:
                project.Orchestration.Genome.RhythmicLanguage = random.Pick(RhythmicLanguages);
                foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => !item.Locked))
                {
                    role.Density = Math.Clamp(role.Density + random.NextInt(-22, 23), 10, 100);
                    role.Revision++;
                }
                ApplyRhythmicDimension(project, project.Orchestration.Genome.RhythmicLanguage);
                _composer.RegenerateUnlocked(project);
                break;
            case RandomizationDomain.Algorithms:
                foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => !item.Locked && item.Role is TrackRole.Lead or TrackRole.Arpeggio or TrackRole.Texture))
                {
                    role.GeneratorId = random.Chance(0.4) ? role.GeneratorId : string.Empty;
                    role.Revision++;
                }
                _composer.RegenerateUnlocked(project);
                break;
            case RandomizationDomain.Arrangement:
                project.AdvancedComposition.Arrangement.MutationRate = Math.Clamp(0.04 + random.NextDouble() * 0.28, 0.02, 0.5);
                project.AdvancedComposition.Tension.ClimaxPosition = 0.55 + random.NextDouble() * 0.35;
                _composer.RegenerateAdvanced(project);
                break;
            case RandomizationDomain.Performance:
                foreach (SongTrack track in project.Song.Tracks)
                {
                    track.Volume = Math.Clamp(0.72 + random.NextDouble() * 0.42, 0.5, 1.25);
                    track.Pan = track.IsPercussion ? 0 : -0.55 + random.NextDouble() * 1.1;
                }
                break;
        }
    }

    private static CompositionGenome BuildGenome(ProjectDocument project, string entropyHex, List<string> sourceStyles, DeterministicRandom random)
    {
        TotalRandomizationSettings settings = project.Orchestration.Randomization;
        CompositionGenome genome = new()
        {
            Id = SeedDerivation.StableId("genome", project.MasterSeed, settings.Mode.ToString()),
            EntropySeedHex = entropyHex,
            Mode = settings.Mode,
            SourceStyleIds = sourceStyles,
            StructuralFamily = random.Pick(StructuralFamilies),
            HarmonicLanguage = random.Pick(HarmonicLanguages),
            RhythmicLanguage = random.Pick(RhythmicLanguages),
            OrchestrationLanguage = random.Pick(OrchestrationLanguages),
            EmotionalModifier = random.Pick(EmotionalModifiers),
            PerformanceModifier = random.Pick(PerformanceModifiers),
            EnsembleArchetypeId = random.Pick(EnsembleArchetypeLibrary.All).Id
        };

        int budget = settings.SurpriseBudget;
        StyleFamily family = ResolveFamily(project.StructuredComposition.ActiveStyleId);
        while (budget > 0 && genome.TraitIds.Count < 8)
        {
            IReadOnlyList<StyleTraitDefinition> candidates = StyleTraitLibrary.Compatible(genome.TraitIds, family, budget);
            if (candidates.Count == 0) break;
            StyleTraitDefinition trait = random.Pick(candidates);
            double acceptance = settings.Mode switch
            {
                OrchestrationRandomizationMode.Coherent => 0.42,
                OrchestrationRandomizationMode.Adventurous => 0.62,
                OrchestrationRandomizationMode.Experimental => 0.78,
                OrchestrationRandomizationMode.Chaos => 0.93,
                _ => 0.58
            };
            if (!random.Chance(acceptance)) break;
            genome.TraitIds.Add(trait.Id);
            genome.SurpriseCosts[trait.Id] = trait.SurpriseCost;
            genome.SurpriseSpent += trait.SurpriseCost;
            budget -= trait.SurpriseCost;
        }
        return genome;
    }

    private static void ApplyGenome(ProjectDocument project, DeterministicRandom random)
    {
        CompositionGenome genome = project.Orchestration.Genome;
        ApplyStructuralDimension(project, genome.StructuralFamily);
        ApplyHarmonicDimension(project, genome.HarmonicLanguage, random);
        ApplyRhythmicDimension(project, genome.RhythmicLanguage);
        List<StyleTraitDefinition> traits = genome.TraitIds.Select(StyleTraitLibrary.Get).ToList();
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            role.InstrumentSelector ??= PresetCatalogFactory.DefaultSelector(role.Role, role.IsPercussion);
            foreach (StyleTraitDefinition trait in traits)
            {
                role.InstrumentSelector.PreferredTags = role.InstrumentSelector.PreferredTags
                    .Union(trait.PreferredTags, StringComparer.OrdinalIgnoreCase).ToList();
                role.InstrumentSelector.AvoidTags = role.InstrumentSelector.AvoidTags
                    .Union(trait.AvoidTags, StringComparer.OrdinalIgnoreCase).ToList();
                if (trait.DensityAdjustments.TryGetValue(role.Role, out int adjustment))
                {
                    role.Density = Math.Clamp(role.Density + adjustment, 5, 100);
                }
            }
        }

        double energyAdjustment = traits.Sum(item => item.EnergyAdjustment);
        foreach (FormSectionPlan section in project.StructuredComposition.FormPlan)
        {
            section.Energy = Math.Clamp(section.Energy + energyAdjustment, 0.05, 1.0);
        }
        double humanization = traits.Sum(item => item.HumanizationAdjustment);
        if (genome.PerformanceModifier == "mechanical") humanization -= 0.18;
        if (genome.PerformanceModifier is "humanized" or "loose" or "flowing") humanization += 0.16;
        project.AdvancedComposition.Arrangement.MutationRate = Math.Clamp(0.08 + Math.Abs(humanization) * 0.25 + project.Orchestration.Randomization.Surprise / 900.0, 0.03, 0.5);
        project.AdvancedComposition.Tension.ClimaxPosition = 0.58 + random.NextDouble() * 0.28;
        project.AdvancedComposition.Tension.ClimaxStrength = Math.Clamp(0.72 + energyAdjustment + random.NextDouble() * 0.22, 0.45, 1.0);
        if (genome.RhythmicLanguage == "sparse")
        {
            foreach (CompositionRoleSettings role in project.StructuredComposition.Roles) role.Density = Math.Max(10, role.Density - 18);
        }
        if (genome.RhythmicLanguage is "syncopated" or "breakbeat" or "euclidean")
        {
            CompositionRoleSettings? drums = project.StructuredComposition.Roles.FirstOrDefault(item => item.Role == TrackRole.Drums);
            if (drums is not null) drums.Density = Math.Min(100, drums.Density + 16);
        }
        ApplyOrchestrationDimension(project, genome.OrchestrationLanguage, genome.EmotionalModifier);
        project.Orchestration.Randomization.FavorCompleteSoundFontCoverage |= project.Orchestration.Randomization.Exploration == PresetExplorationMode.SoundFontExpedition;
    }

    private static void ApplyAdditionalStyleInfluence(ProjectDocument project, StructuredStyleDefinition style, double weight)
    {
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            CompositionRoleSettings? influence = style.Roles.FirstOrDefault(item => item.Role == role.Role);
            if (influence is null) continue;
            role.Density = Math.Clamp((int)Math.Round(role.Density * (1 - weight) + influence.Density * weight), 5, 100);
            InstrumentSelectorSettings selector = role.InstrumentSelector ?? PresetCatalogFactory.DefaultSelector(role.Role, role.IsPercussion);
            InstrumentSelectorSettings other = influence.InstrumentSelector ?? PresetCatalogFactory.DefaultSelector(influence.Role, influence.IsPercussion);
            selector.Families = selector.Families.Union(other.Families).Distinct().ToList();
            selector.PreferredTags = selector.PreferredTags.Union(other.PreferredTags, StringComparer.OrdinalIgnoreCase).ToList();
            selector.AvoidTags = selector.AvoidTags.Union(other.AvoidTags, StringComparer.OrdinalIgnoreCase).ToList();
            role.InstrumentSelector = selector;
        }
        project.StructuredComposition.ResolvedStylePackIds = project.StructuredComposition.ResolvedStylePackIds
            .Append(style.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ApplyStructuralDimension(ProjectDocument project, string dimension)
    {
        project.StructuredComposition.BarsPerPhrase = dimension switch
        {
            "theme-and-variations" or "aaba" => 4,
            "ambient-evolution" or "through-composed" => 3,
            "algorithmic-process" => 1,
            _ => 2
        };
        if (dimension == "loop-build")
        {
            for (int index = 0; index < project.StructuredComposition.FormPlan.Count; index++)
                project.StructuredComposition.FormPlan[index].Energy = Math.Clamp(0.2 + index / (double)Math.Max(1, project.StructuredComposition.FormPlan.Count - 1) * 0.75, 0.05, 1);
        }
        else if (dimension == "ambient-evolution")
        {
            foreach (FormSectionPlan section in project.StructuredComposition.FormPlan)
                section.LengthBars = Math.Max(section.LengthBars, 6);
        }
        else if (dimension == "algorithmic-process")
        {
            foreach (FormSectionPlan section in project.StructuredComposition.FormPlan)
                section.MotifTreatment = section.VariationIndex % 2 == 0 ? MotifTransformationKind.Sequence : MotifTransformationKind.Rotate;
        }
    }

    private static void ApplyHarmonicDimension(ProjectDocument project, string dimension, DeterministicRandom random)
    {
        project.Song.Scale = dimension switch
        {
            "modal" => random.Pick(new[] { ScaleKind.Dorian, ScaleKind.Phrygian, ScaleKind.Lydian, ScaleKind.Mixolydian }),
            "blues" => ScaleKind.MinorPentatonic,
            "minimalist" => random.Chance(0.5) ? ScaleKind.MajorPentatonic : ScaleKind.MinorPentatonic,
            "atonal-colour" or "set-based" => ScaleKind.Chromatic,
            "quartal" => random.Chance(0.5) ? ScaleKind.Dorian : ScaleKind.Lydian,
            _ => random.Chance(0.55) ? ScaleKind.Major : ScaleKind.NaturalMinor
        };
        if (dimension == "drone")
        {
            CompositionRoleSettings? bass = project.StructuredComposition.Roles.FirstOrDefault(item => item.Role == TrackRole.Bass);
            if (bass is not null) bass.Density = Math.Min(bass.Density, 38);
        }
    }

    private static void ApplyRhythmicDimension(ProjectDocument project, string dimension)
    {
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            role.Density = dimension switch
            {
                "sparse" or "rubato" => Math.Max(8, role.Density - 18),
                "breakbeat" or "cellular" => Math.Min(100, role.Density + (role.IsPercussion ? 20 : 5)),
                "polymetric" => Math.Clamp(role.Density + (role.MidiChannel % 3 - 1) * 8, 8, 100),
                _ => role.Density
            };
            if (dimension == "euclidean" && role.IsPercussion) role.RhythmPatternId = "euclidean-pulse";
            if (dimension == "swing") role.RhythmPatternId = role.IsPercussion ? "swing-kit" : role.RhythmPatternId;
        }
    }

    private static void ApplyOrchestrationDimension(ProjectDocument project, string language, string emotion)
    {
        string[] preferred = language switch
        {
            "acoustic" or "organic" => new[] { "organic", "warm" },
            "electronic" => new[] { "electronic", "sustaining" },
            "chamber" or "orchestral" => new[] { "organic", "ensemble" },
            "percussive" => new[] { "percussive", "short-attack" },
            "vocal-like" => new[] { "airy", "sustaining" },
            "metallic" => new[] { "metallic", "bright" },
            "lo-fi" => new[] { "warm", "dark" },
            _ => Array.Empty<string>()
        };
        if (emotion is "bright" or "triumphant" or "playful") preferred = preferred.Append("bright").ToArray();
        if (emotion is "dark" or "mysterious" or "melancholic") preferred = preferred.Append("dark").ToArray();
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            role.InstrumentSelector ??= PresetCatalogFactory.DefaultSelector(role.Role, role.IsPercussion);
            role.InstrumentSelector.PreferredTags = role.InstrumentSelector.PreferredTags
                .Union(preferred, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private static bool HasAnyDomainLock(RandomizationLockSettings locks) =>
        locks.Style || locks.Form || locks.Harmony || locks.Rhythm || locks.Ensemble ||
        locks.Instruments || locks.Algorithms || locks.Arrangement || locks.Performance;

    private static double HybridChance(OrchestrationRandomizationMode mode) => mode switch
    {
        OrchestrationRandomizationMode.Coherent => 0.22,
        OrchestrationRandomizationMode.Adventurous => 0.55,
        OrchestrationRandomizationMode.Experimental => 0.78,
        OrchestrationRandomizationMode.Chaos => 0.94,
        _ => 0.48
    };

    private static StyleFamily ResolveFamily(string styleId)
    {
        try { return StructuredStyleLibrary.Get(styleId).Family; }
        catch (KeyNotFoundException) { return StyleFamily.Hybrid; }
    }

    private static string BuildUniverseTitle(ProjectDocument project, CompositionGenome genome)
    {
        string emotion = string.IsNullOrWhiteSpace(genome.EmotionalModifier) ? "Unknown" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(genome.EmotionalModifier.Replace('-', ' '));
        string structure = string.IsNullOrWhiteSpace(genome.StructuralFamily) ? "Form" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(genome.StructuralFamily.Replace('-', ' '));
        return $"{emotion} {structure} {project.MasterSeed % 10000:0000}";
    }

    private static EntropySeed CreatePseudoRandomEntropy()
    {
        ulong seed;
        do
        {
            seed = unchecked((ulong)Random.Shared.NextInt64());
        }
        while (seed == 0);
        return new EntropySeed(seed, seed.ToString("X16", CultureInfo.InvariantCulture));
    }

    private static TotalRandomizationSettings NormalizeSettings(TotalRandomizationSettings source)
    {
        TotalRandomizationSettings result = CloneSettings(source);
        result.Surprise = Math.Clamp(result.Surprise, 0, 100);
        result.SurpriseBudget = Math.Clamp(result.SurpriseBudget, 0, 200);
        result.MaximumStyleSources = Math.Clamp(result.MaximumStyleSources, 1, 5);
        result.MinimumEnsembleSize = Math.Clamp(result.MinimumEnsembleSize, 1, 16);
        result.MaximumEnsembleSize = Math.Clamp(result.MaximumEnsembleSize, 1, 16);
        if (result.MinimumEnsembleSize > result.MaximumEnsembleSize)
        {
            (result.MinimumEnsembleSize, result.MaximumEnsembleSize) =
                (result.MaximumEnsembleSize, result.MinimumEnsembleSize);
        }
        return result;
    }

    private static TotalRandomizationSettings CloneSettings(TotalRandomizationSettings source) => new()
    {
        Mode = source.Mode,
        Exploration = source.Exploration,
        Surprise = source.Surprise,
        SurpriseBudget = source.SurpriseBudget,
        UseCryptographicSeedForNewUniverses = source.UseCryptographicSeedForNewUniverses,
        AllowStyleHybridization = source.AllowStyleHybridization,
        MaximumStyleSources = source.MaximumStyleSources,
        MinimumEnsembleSize = source.MinimumEnsembleSize,
        MaximumEnsembleSize = source.MaximumEnsembleSize,
        PermitSectionInstrumentChanges = source.PermitSectionInstrumentChanges,
        PermitLayering = source.PermitLayering,
        FavorCompleteSoundFontCoverage = source.FavorCompleteSoundFontCoverage,
        Locks = new RandomizationLockSettings
        {
            Style = source.Locks.Style,
            Form = source.Locks.Form,
            Harmony = source.Locks.Harmony,
            Rhythm = source.Locks.Rhythm,
            Ensemble = source.Locks.Ensemble,
            Instruments = source.Locks.Instruments,
            Algorithms = source.Locks.Algorithms,
            Arrangement = source.Locks.Arrangement,
            Performance = source.Locks.Performance
        }
    };

    public static InstrumentCatalogDocument CloneCatalog(InstrumentCatalogDocument source) => new()
    {
        SoundFontId = source.SoundFontId,
        SoundFontName = source.SoundFontName,
        SourcePath = source.SourcePath,
        SourceFingerprint = source.SourceFingerprint,
        ScannedAtUtc = source.ScannedAtUtc,
        IsComplete = source.IsComplete,
        Presets = source.Presets.Select(item => item.Clone()).ToList(),
        Diagnostics = source.Diagnostics.ToList()
    };

    private static void CopyInto(ProjectDocument target, ProjectDocument source)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.EngineVersion = source.EngineVersion;
        target.ProjectId = source.ProjectId;
        target.Title = source.Title;
        target.MasterSeed = source.MasterSeed;
        target.Song = source.Song;
        target.GenerationLab = source.GenerationLab;
        target.StructuredComposition = source.StructuredComposition;
        target.AdvancedComposition = source.AdvancedComposition;
        target.Orchestration = source.Orchestration;
        target.Audio = source.Audio;
        target.Tags = source.Tags;
    }
}
