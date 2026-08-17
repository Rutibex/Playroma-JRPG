using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Generators.Advanced;

public sealed class LongRangeMotifPlanner
{
    public IReadOnlyList<MotifOccurrencePlan> Apply(
        IReadOnlyList<FormSectionPlan> sections,
        MotifDefinition motif,
        LongRangeMotifSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(motif);
        ArgumentNullException.ThrowIfNull(settings);
        if (sections.Count == 0) return Array.Empty<MotifOccurrencePlan>();

        List<MotifOccurrencePlan> result = new();
        Dictionary<string, int> familyCounts = new(StringComparer.OrdinalIgnoreCase);
        string Normalize(string symbol) => new string(symbol.Where(char.IsLetter).ToArray()).ToUpperInvariant();

        for (int index = 0; index < sections.Count; index++)
        {
            FormSectionPlan section = sections[index];
            string family = Normalize(section.Symbol);
            int occurrence = familyCounts.GetValueOrDefault(family);
            familyCounts[family] = occurrence + 1;
            bool required = occurrence == 0 || section.Kind is FormSectionKind.Chorus or FormSectionKind.Outro;
            MotifTransformationKind transformation = occurrence switch
            {
                0 => MotifTransformationKind.Original,
                1 => section.MotifTreatment,
                2 => MotifTransformationKind.Sequence,
                _ => section.MotifTreatment
            };
            if (settings.RequireFinalRecall && index == sections.Count - 1)
            {
                transformation = MotifTransformationKind.Fragment;
                required = true;
            }
            section.MotifTreatment = transformation;
            result.Add(new MotifOccurrencePlan
            {
                SectionId = section.Id,
                MotifId = motif.Id,
                FamilyId = family,
                OccurrenceIndex = occurrence,
                Transformation = transformation,
                TargetSimilarity = occurrence == 0 ? 1.0 : Math.Max(settings.MinimumContourSimilarity, 0.76 - occurrence * 0.08),
                Required = required
            });
        }

        // MinimumRecurrences is an authoring target, not a reason to reject short forms.
        // A one-section reference piece can only contain one occurrence, while a two-section
        // piece can only contain two. Clamp the effective target to the form that actually
        // exists and preserve the configured value for future, longer regenerations.
        int effectiveMinimumRecurrences = Math.Clamp(settings.MinimumRecurrences, 1, result.Count);
        int requiredCount = result.Count(item => item.Required);
        for (int index = 1; requiredCount < effectiveMinimumRecurrences && index < result.Count - 1; index++)
        {
            if (result[index].Required) continue;
            result[index].Required = true;
            result[index].Transformation = MotifTransformationKind.Rotate;
            sections[index].MotifTreatment = MotifTransformationKind.Rotate;
            requiredCount++;
        }

        int maximumGap = Math.Max(1, settings.MaximumSectionsBetweenRecurrences);
        int previousRequired = 0;
        for (int index = 1; index < result.Count; index++)
        {
            if (result[index].Required)
            {
                while (index - previousRequired > maximumGap)
                {
                    int inserted = previousRequired + maximumGap;
                    result[inserted].Required = true;
                    result[inserted].Transformation = MotifTransformationKind.Rotate;
                    result[inserted].TargetSimilarity = Math.Max(result[inserted].TargetSimilarity, settings.MinimumContourSimilarity);
                    sections[inserted].MotifTreatment = MotifTransformationKind.Rotate;
                    previousRequired = inserted;
                }
                previousRequired = index;
            }
        }

        if (settings.RequireClimacticReturn)
        {
            int climaxIndex = sections.Select((section, index) => (Energy: section.Energy, Index: index)).OrderByDescending(item => item.Energy).First().Index;
            result[climaxIndex].Required = true;
            result[climaxIndex].TargetSimilarity = Math.Max(result[climaxIndex].TargetSimilarity, 0.72);
        }
        return result;
    }
}
