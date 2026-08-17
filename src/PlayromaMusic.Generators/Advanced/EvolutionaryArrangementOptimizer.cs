using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Generators.Advanced;

public sealed class EvolutionaryArrangementOptimizer
{
    public IReadOnlyList<ArrangementSectionPlan> Optimize(
        ulong masterSeed,
        IReadOnlyList<FormSectionPlan> sections,
        IReadOnlyList<CompositionRoleSettings> roles,
        EvolutionaryArrangementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(settings);
        if (sections.Count == 0 || roles.Count == 0) return Array.Empty<ArrangementSectionPlan>();

        int populationSize = Math.Clamp(settings.PopulationSize, 6, 128);
        int eliteCount = Math.Clamp(settings.EliteCount, 1, populationSize - 1);
        DeterministicRandom random = new(SeedDerivation.Derive(masterSeed, "stage6", "arrangement"));
        List<Genome> population = Enumerable.Range(0, populationSize)
            .Select(index => CreateGenome(random, sections, roles, index == 0))
            .ToList();

        for (int generation = 0; generation < Math.Clamp(settings.Generations, 1, 200); generation++)
        {
            foreach (Genome genome in population) genome.Fitness = Score(genome, sections, roles, settings);
            population = population.OrderByDescending(item => item.Fitness).ToList();
            List<Genome> next = population.Take(eliteCount).Select(item => item.Clone()).ToList();
            while (next.Count < populationSize)
            {
                Genome a = Tournament(population, random);
                Genome b = Tournament(population, random);
                Genome child = Crossover(a, b, random);
                Mutate(child, random, settings.MutationRate);
                Repair(child, sections, roles, settings);
                next.Add(child);
            }
            population = next;
        }

        Genome best = population.OrderByDescending(item => Score(item, sections, roles, settings)).First().Clone();
        EnsureSectionContrast(best, sections, roles, settings);
        best.Fitness = Score(best, sections, roles, settings);
        return sections.Select((section, sectionIndex) => new ArrangementSectionPlan
        {
            SectionId = section.Id,
            TargetEnergy = section.Energy,
            Fitness = best.Fitness,
            Roles = roles.Select((role, roleIndex) => new ArrangementRoleState
            {
                RoleId = role.Id,
                Role = role.Role,
                Active = best.Active[sectionIndex, roleIndex],
                Intensity = best.Intensity[sectionIndex, roleIndex]
            }).ToList()
        }).ToList();
    }

    private static Genome CreateGenome(DeterministicRandom random, IReadOnlyList<FormSectionPlan> sections, IReadOnlyList<CompositionRoleSettings> roles, bool baseline)
    {
        Genome genome = new(sections.Count, roles.Count);
        for (int section = 0; section < sections.Count; section++)
        {
            for (int role = 0; role < roles.Count; role++)
            {
                double energy = sections[section].Energy;
                double roleBias = roles[role].Role switch
                {
                    TrackRole.Lead => 0.82,
                    TrackRole.Bass => 0.74,
                    TrackRole.Drums => 0.66,
                    TrackRole.Chords or TrackRole.Harmony => 0.72,
                    TrackRole.Arpeggio => 0.48,
                    TrackRole.Texture => 0.54,
                    _ => 0.45
                };
                genome.Active[section, role] = baseline ? roles[role].Enabled : roles[role].Enabled && random.Chance(Math.Clamp(0.18 + energy * roleBias, 0.1, 0.96));
                genome.Intensity[section, role] = Math.Clamp((int)Math.Round(35 + energy * 60 + random.NextInt(-8, 9)), 10, 100);
            }
        }
        return genome;
    }

    private static double Score(Genome genome, IReadOnlyList<FormSectionPlan> sections, IReadOnlyList<CompositionRoleSettings> roles, EvolutionaryArrangementSettings settings)
    {
        double score = 0;
        for (int section = 0; section < sections.Count; section++)
        {
            int active = 0;
            double intensity = 0;
            for (int role = 0; role < roles.Count; role++)
            {
                if (!genome.Active[section, role]) continue;
                active++;
                intensity += genome.Intensity[section, role] / 100.0;
            }
            double actualEnergy = intensity / Math.Max(1, roles.Count);
            score += 20.0 - Math.Abs(actualEnergy - sections[section].Energy) * 24.0;
            if (active == 0) score -= 80;

            bool lead = IsActive(genome, section, roles, TrackRole.Lead);
            bool bass = IsActive(genome, section, roles, TrackRole.Bass);
            bool drums = IsActive(genome, section, roles, TrackRole.Drums);
            if (sections[section].Kind is FormSectionKind.Chorus or FormSectionKind.Solo && lead) score += 5;
            if (settings.PreserveLeadAtCadences && sections[section].Kind == FormSectionKind.Outro && lead) score += 6;
            if (settings.RequireRhythmicFoundationAtHighEnergy && sections[section].Energy > 0.65)
            {
                if (bass) score += 4; else score -= 7;
                if (drums) score += 4; else score -= 7;
            }
            if (section > 0)
            {
                int changes = 0;
                for (int role = 0; role < roles.Count; role++) if (genome.Active[section, role] != genome.Active[section - 1, role]) changes++;
                score += changes is >= 1 and <= 3 ? 4 : changes > 5 ? -4 : 0;
            }
        }
        return score;
    }

    private static void EnsureSectionContrast(
        Genome genome,
        IReadOnlyList<FormSectionPlan> sections,
        IReadOnlyList<CompositionRoleSettings> roles,
        EvolutionaryArrangementSettings settings)
    {
        if (sections.Count < 2 || roles.Count < 2) return;
        bool differs = false;
        for (int section = 1; section < sections.Count && !differs; section++)
        for (int role = 0; role < roles.Count; role++)
        {
            if (genome.Active[section, role] != genome.Active[section - 1, role])
            {
                differs = true;
                break;
            }
        }
        if (differs) return;

        int quietSection = sections.Select((section, index) => (section.Energy, Index: index))
            .OrderBy(item => item.Energy)
            .ThenBy(item => item.Index)
            .First().Index;
        int optionalRole = roles.ToList().FindIndex(role => role.Enabled && role.Role is (TrackRole.Arpeggio or TrackRole.Texture or TrackRole.CounterMelody));
        if (optionalRole < 0)
        {
            optionalRole = roles.Select((role, index) => (role, index))
                .Where(item => item.role.Enabled)
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .Last();
        }
        if (optionalRole < 0) return;
        int activeCount = Enumerable.Range(0, roles.Count).Count(role => roles[role].Enabled && genome.Active[quietSection, role]);
        if (activeCount > 1) genome.Active[quietSection, optionalRole] = false;
        else genome.Active[quietSection, optionalRole] = true;
        Repair(genome, sections, roles, settings);
    }

    private static bool IsActive(Genome genome, int section, IReadOnlyList<CompositionRoleSettings> roles, TrackRole role)
    {
        for (int index = 0; index < roles.Count; index++) if (roles[index].Role == role && genome.Active[section, index]) return true;
        return false;
    }

    private static Genome Tournament(IReadOnlyList<Genome> population, DeterministicRandom random)
    {
        Genome a = population[random.NextInt(population.Count)];
        Genome b = population[random.NextInt(population.Count)];
        Genome c = population[random.NextInt(population.Count)];
        return new[] { a, b, c }.OrderByDescending(item => item.Fitness).First();
    }

    private static Genome Crossover(Genome a, Genome b, DeterministicRandom random)
    {
        Genome child = new(a.Active.GetLength(0), a.Active.GetLength(1));
        int cut = random.NextInt(Math.Max(1, a.Active.GetLength(0)));
        for (int section = 0; section < a.Active.GetLength(0); section++)
        for (int role = 0; role < a.Active.GetLength(1); role++)
        {
            Genome source = section <= cut ? a : b;
            child.Active[section, role] = source.Active[section, role];
            child.Intensity[section, role] = source.Intensity[section, role];
        }
        return child;
    }

    private static void Mutate(Genome genome, DeterministicRandom random, double rate)
    {
        rate = Math.Clamp(rate, 0.0, 0.8);
        for (int section = 0; section < genome.Active.GetLength(0); section++)
        for (int role = 0; role < genome.Active.GetLength(1); role++)
        {
            if (random.Chance(rate)) genome.Active[section, role] = !genome.Active[section, role];
            if (random.Chance(Math.Min(1.0, rate * 1.5))) genome.Intensity[section, role] = Math.Clamp(genome.Intensity[section, role] + random.NextInt(-18, 19), 5, 100);
        }
    }

    private static void Repair(Genome genome, IReadOnlyList<FormSectionPlan> sections, IReadOnlyList<CompositionRoleSettings> roles, EvolutionaryArrangementSettings settings)
    {
        for (int section = 0; section < sections.Count; section++)
        {
            for (int role = 0; role < roles.Count; role++)
            {
                if (!roles[role].Enabled) genome.Active[section, role] = false;
            }
            if (!Enumerable.Range(0, roles.Count).Any(role => roles[role].Enabled && genome.Active[section, role]))
            {
                int preferred = roles.ToList().FindIndex(role => role.Enabled && role.Role is (TrackRole.Lead or TrackRole.Chords));
                if (preferred < 0) preferred = roles.ToList().FindIndex(role => role.Enabled);
                if (preferred >= 0) genome.Active[section, preferred] = true;
            }
            if (settings.PreserveLeadAtCadences && sections[section].Kind is FormSectionKind.Chorus or FormSectionKind.Outro)
            {
                int lead = roles.ToList().FindIndex(role => role.Enabled && role.Role == TrackRole.Lead);
                if (lead >= 0) genome.Active[section, lead] = true;
            }
            if (settings.RequireRhythmicFoundationAtHighEnergy && sections[section].Energy > 0.65)
            {
                int bass = roles.ToList().FindIndex(role => role.Enabled && role.Role == TrackRole.Bass);
                int drums = roles.ToList().FindIndex(role => role.Enabled && role.Role == TrackRole.Drums);
                if (bass >= 0) genome.Active[section, bass] = true;
                if (drums >= 0) genome.Active[section, drums] = true;
            }
        }
    }

    private sealed class Genome
    {
        public Genome(int sections, int roles)
        {
            Active = new bool[sections, roles];
            Intensity = new int[sections, roles];
        }
        public bool[,] Active { get; }
        public int[,] Intensity { get; }
        public double Fitness { get; set; }
        public Genome Clone()
        {
            Genome clone = new(Active.GetLength(0), Active.GetLength(1)) { Fitness = Fitness };
            Array.Copy(Active, clone.Active, Active.Length);
            Array.Copy(Intensity, clone.Intensity, Intensity.Length);
            return clone;
        }
    }
}
