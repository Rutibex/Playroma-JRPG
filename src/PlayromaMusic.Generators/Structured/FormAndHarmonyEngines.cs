using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Generators.Structured;

public sealed class FormGrammarEngine
{
    public IReadOnlyList<FormSectionPlan> Generate(
        ulong masterSeed,
        StructuredStyleDefinition style,
        out List<FormDerivationStep> derivation)
    {
        DeterministicRandom random = new(SeedDerivation.Derive(masterSeed, "stage3", "form", style.Id));
        string[] production = style.FormProductions[random.NextInt(style.FormProductions.Count)];
        derivation = new List<FormDerivationStep>
        {
            new()
            {
                Step = 0,
                ExpandedSymbol = "Song",
                Production = string.Join(" ", production),
                ResultingSymbols = production.ToList()
            }
        };

        List<FormSectionPlan> plan = new();
        Dictionary<FormSectionKind, int> variations = new();
        int startBar = 0;
        for (int index = 0; index < production.Length; index++)
        {
            string symbol = production[index];
            if (!style.Tokens.TryGetValue(symbol, out FormTokenDefinition? token))
            {
                throw new InvalidOperationException($"Form grammar {style.FormGrammarId} emitted unknown token {symbol}.");
            }

            int variation = variations.GetValueOrDefault(token.Kind);
            variations[token.Kind] = variation + 1;
            FormSectionPlan section = new()
            {
                Id = SeedDerivation.StableId("form", masterSeed, style.Id, index.ToString(), symbol),
                Symbol = symbol,
                Name = token.Name,
                Kind = token.Kind,
                VariationIndex = variation,
                StartBar = startBar,
                LengthBars = token.LengthBars,
                Energy = Math.Clamp(token.Energy + (variation * 0.035), 0.0, 1.0),
                MotifTreatment = token.Treatment
            };
            plan.Add(section);
            startBar += token.LengthBars;
        }

        derivation.Add(new FormDerivationStep
        {
            Step = 1,
            ExpandedSymbol = "Terminal sections",
            Production = "Assign section lengths, energy and motif treatment",
            ResultingSymbols = plan.Select(section => $"{section.Symbol}:{section.LengthBars}").ToList()
        });
        return plan;
    }
}

public sealed class HarmonicGrammarEngine
{
    public IReadOnlyList<HarmonicEventPlan> Generate(
        ulong masterSeed,
        StructuredStyleDefinition style,
        IReadOnlyList<FormSectionPlan> form)
    {
        DeterministicRandom random = new(SeedDerivation.Derive(masterSeed, "stage3", "harmony", style.Id));
        List<HarmonicEventPlan> result = new();

        foreach (FormSectionPlan section in form)
        {
            IReadOnlyList<int[]> choices = style.Progressions[section.Kind];
            int[] progression = choices[(section.VariationIndex + random.NextInt(choices.Count)) % choices.Count];
            CadenceKind sectionCadence = CadenceFor(section.Kind, section.VariationIndex, style.HarmonicGrammarId);

            for (int localBar = 0; localBar < section.LengthBars; localBar++)
            {
                int globalBar = section.StartBar + localBar;
                int degree = progression[localBar % progression.Length];
                bool finalBar = localBar == section.LengthBars - 1;
                CadenceKind cadence = finalBar ? sectionCadence : CadenceKind.None;

                if (finalBar)
                {
                    degree = cadence switch
                    {
                        CadenceKind.Half => 4,
                        CadenceKind.Authentic => 0,
                        CadenceKind.Plagal => 0,
                        CadenceKind.Deceptive => 5,
                        CadenceKind.Modal => 0,
                        _ => degree
                    };
                }

                HarmonicFunction function = FunctionForDegree(degree, cadence, style.HarmonicGrammarId);
                result.Add(new HarmonicEventPlan
                {
                    Id = SeedDerivation.StableId("harmony", masterSeed, style.Id, section.Id, localBar.ToString()),
                    SectionId = section.Id,
                    StartBar = globalBar,
                    LengthBars = 1,
                    ScaleDegree = degree,
                    ChordToneCount = section.Kind is FormSectionKind.Chorus or FormSectionKind.Bridge ? 4 : 3,
                    Inversion = localBar == 0 ? 0 : random.NextInt(3),
                    Function = function,
                    Cadence = cadence,
                    Tension = TensionFor(function, cadence, section.Energy)
                });
            }

            ApplyCadence(result, section, sectionCadence, style.HarmonicGrammarId);
        }

        return result;
    }


    private static void ApplyCadence(
        List<HarmonicEventPlan> result,
        FormSectionPlan section,
        CadenceKind cadence,
        string grammarId)
    {
        HarmonicEventPlan final = result[^1];
        final.Cadence = cadence;
        final.ScaleDegree = cadence switch
        {
            CadenceKind.Half => 4,
            CadenceKind.Authentic => 0,
            CadenceKind.Plagal => 0,
            CadenceKind.Deceptive => 5,
            CadenceKind.Modal => 0,
            _ => final.ScaleDegree
        };
        final.Function = FunctionForDegree(final.ScaleDegree, cadence, grammarId);
        final.Tension = TensionFor(final.Function, cadence, section.Energy);

        if (section.LengthBars < 2)
        {
            return;
        }

        HarmonicEventPlan approach = result[^2];
        approach.Cadence = CadenceKind.None;
        approach.ScaleDegree = cadence switch
        {
            CadenceKind.Authentic => 4,
            CadenceKind.Deceptive => 4,
            CadenceKind.Plagal => 3,
            CadenceKind.Modal => 6,
            CadenceKind.Half => 1,
            _ => approach.ScaleDegree
        };
        approach.Function = FunctionForDegree(approach.ScaleDegree, CadenceKind.None, grammarId);
        approach.Tension = cadence switch
        {
            CadenceKind.Authentic or CadenceKind.Deceptive => Math.Max(0.86, TensionFor(approach.Function, CadenceKind.None, section.Energy)),
            CadenceKind.Half => Math.Max(0.58, TensionFor(approach.Function, CadenceKind.None, section.Energy)),
            _ => TensionFor(approach.Function, CadenceKind.None, section.Energy)
        };
    }

    private static CadenceKind CadenceFor(FormSectionKind kind, int variation, string grammarId) => kind switch
    {
        FormSectionKind.Intro => CadenceKind.Half,
        FormSectionKind.Verse => variation % 2 == 0 ? CadenceKind.Half : CadenceKind.Deceptive,
        FormSectionKind.Chorus => CadenceKind.Authentic,
        FormSectionKind.Bridge => variation % 2 == 0 ? CadenceKind.Plagal : CadenceKind.Half,
        FormSectionKind.Breakdown => grammarId.Contains("modal", StringComparison.OrdinalIgnoreCase)
            ? CadenceKind.Modal
            : CadenceKind.Half,
        FormSectionKind.Solo => CadenceKind.Authentic,
        FormSectionKind.Outro => CadenceKind.Authentic,
        _ => CadenceKind.None
    };

    private static HarmonicFunction FunctionForDegree(int degree, CadenceKind cadence, string grammarId)
    {
        int normalized = Mod(degree, 7);
        if (cadence is CadenceKind.Half or CadenceKind.Deceptive || normalized is 4 or 6)
        {
            return HarmonicFunction.Dominant;
        }
        if (normalized is 1 or 3)
        {
            return HarmonicFunction.Predominant;
        }
        if (normalized is 0 or 2 or 5)
        {
            return HarmonicFunction.Tonic;
        }
        return grammarId.Contains("modal", StringComparison.OrdinalIgnoreCase)
            ? HarmonicFunction.Modal
            : HarmonicFunction.Tonic;
    }

    private static double TensionFor(HarmonicFunction function, CadenceKind cadence, double energy)
    {
        double baseTension = function switch
        {
            HarmonicFunction.Tonic => 0.18,
            HarmonicFunction.Predominant => 0.48,
            HarmonicFunction.Dominant => 0.78,
            HarmonicFunction.Modal => 0.42,
            _ => 0.30
        };
        baseTension += cadence switch
        {
            CadenceKind.Half => 0.14,
            CadenceKind.Deceptive => 0.18,
            CadenceKind.Authentic => -0.08,
            CadenceKind.Plagal => -0.04,
            _ => 0.0
        };
        return Math.Clamp((baseTension * 0.72) + (energy * 0.28), 0.0, 1.0);
    }

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}

public sealed class MotifEngine
{
    public MotifDefinition Generate(ulong masterSeed, StructuredStyleDefinition style, int revision)
    {
        DeterministicRandom random = new(SeedDerivation.Derive(
            masterSeed,
            "stage3",
            "motif",
            style.Id,
            revision.ToString()));
        int length = random.NextInt(4, 8);
        List<int> degrees = new() { 0 };
        int current = 0;
        for (int index = 1; index < length; index++)
        {
            current += random.Pick(new[] { -2, -1, 1, 1, 1, 2, 3 });
            current = Math.Clamp(current, -4, 6);
            degrees.Add(current);
        }

        int[][] rhythmChoices =
        {
            new[] { 2, 2, 4, 2, 2, 4 },
            new[] { 3, 1, 2, 2, 4, 4 },
            new[] { 1, 1, 2, 4, 2, 2, 4 },
            new[] { 4, 2, 2, 3, 1, 4 }
        };
        int[] rhythm = rhythmChoices[random.NextInt(rhythmChoices.Length)];
        return new MotifDefinition
        {
            Id = SeedDerivation.StableId("motif", masterSeed, style.Id, revision.ToString()),
            Name = $"{style.Name} Motif {revision + 1}",
            Revision = revision,
            DegreeOffsets = degrees,
            RhythmUnits = rhythm.Take(length).Concat(rhythm.Take(Math.Max(0, length - rhythm.Length))).Take(length).ToList(),
            Accents = Enumerable.Range(0, length).Select(index => index == 0 || index % 4 == 0 ? 1 : 0).ToList()
        };
    }

    public IReadOnlyList<int> TransformDegrees(MotifDefinition motif, MotifTransformationKind kind, int variation)
    {
        List<int> source = motif.DegreeOffsets.Count == 0 ? new List<int> { 0, 1, 2, 0 } : motif.DegreeOffsets.ToList();
        return kind switch
        {
            MotifTransformationKind.Original => source,
            MotifTransformationKind.Transpose => source.Select(value => value + 2 + variation).ToList(),
            MotifTransformationKind.Invert => source.Select(value => -value).ToList(),
            MotifTransformationKind.Retrograde => source.AsEnumerable().Reverse().ToList(),
            MotifTransformationKind.Rotate => Rotate(source, Math.Max(1, variation + 1)),
            MotifTransformationKind.Sequence => source.Select((value, index) => value + (index / Math.Max(1, source.Count / 2))).ToList(),
            MotifTransformationKind.Augment => source.Select(value => value + (variation % 2)).ToList(),
            MotifTransformationKind.Diminish => source.Where((_, index) => index % 2 == 0).DefaultIfEmpty(0).ToList(),
            MotifTransformationKind.Fragment => source.Take(Math.Max(2, source.Count / 2)).ToList(),
            _ => source
        };
    }

    public IReadOnlyList<int> TransformRhythm(MotifDefinition motif, MotifTransformationKind kind)
    {
        List<int> source = motif.RhythmUnits.Count == 0 ? new List<int> { 2, 2, 4, 2 } : motif.RhythmUnits.ToList();
        return kind switch
        {
            MotifTransformationKind.Retrograde => source.AsEnumerable().Reverse().ToList(),
            MotifTransformationKind.Augment => source.Select(value => Math.Min(16, value * 2)).ToList(),
            MotifTransformationKind.Diminish => source.Select(value => Math.Max(1, value / 2)).ToList(),
            MotifTransformationKind.Rotate => Rotate(source, 1),
            MotifTransformationKind.Fragment => source.Take(Math.Max(2, source.Count / 2)).ToList(),
            _ => source
        };
    }

    private static List<int> Rotate(IReadOnlyList<int> source, int amount)
    {
        int count = source.Count;
        int rotation = ((amount % count) + count) % count;
        return source.Skip(rotation).Concat(source.Take(rotation)).ToList();
    }
}
