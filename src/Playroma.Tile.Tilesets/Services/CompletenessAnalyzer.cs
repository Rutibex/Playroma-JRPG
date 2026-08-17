using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class CompletenessAnalyzer
{
    public CompletenessReport Analyze(TilesetAssemblyDefinition definition, ConstructionTemplate template)
    {
        var report = new CompletenessReport();
        var searchable = definition.Assets
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Category))
            .Select(x => $"{x.Name} {x.Category}".ToLowerInvariant())
            .Concat(definition.Objects.Select(x => $"{x.Name} {x.Category} {string.Join(' ', x.Tags)}".ToLowerInvariant()))
            .ToArray();

        foreach (var requirement in template.Requirements)
        {
            var count = searchable.Count(text => requirement.MatchTags.Any(tag => text.Contains(tag, StringComparison.OrdinalIgnoreCase)));
            var coverage = new RequirementCoverage { Requirement = requirement, FoundCount = count };
            report.Coverage.Add(coverage);
            switch (requirement.Level)
            {
                case RequirementLevel.Required:
                    report.RequiredTotal++;
                    if (coverage.IsSatisfied) report.RequiredSatisfied++;
                    break;
                case RequirementLevel.Recommended:
                    report.RecommendedTotal++;
                    if (coverage.IsSatisfied) report.RecommendedSatisfied++;
                    break;
                case RequirementLevel.Optional:
                    report.OptionalTotal++;
                    if (coverage.IsSatisfied) report.OptionalSatisfied++;
                    break;
            }
        }

        var required = report.RequiredTotal == 0 ? 1d : report.RequiredSatisfied / (double)report.RequiredTotal;
        var recommended = report.RecommendedTotal == 0 ? 1d : report.RecommendedSatisfied / (double)report.RecommendedTotal;
        var optional = report.OptionalTotal == 0 ? 1d : report.OptionalSatisfied / (double)report.OptionalTotal;
        report.Score = Math.Round((required * 0.70 + recommended * 0.23 + optional * 0.07) * 100, 1);
        return report;
    }
}
