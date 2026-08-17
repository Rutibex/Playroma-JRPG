using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Tilesets.Models;

public sealed class ConstructionTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TilesetTemplateKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<ConstructionRequirement> Requirements { get; set; } = new();
}

public sealed class ConstructionRequirement
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public RequirementLevel Level { get; set; }
    public MvSheetSlot? PreferredSheet { get; set; }
    public int MinimumCount { get; set; } = 1;
    public List<string> MatchTags { get; set; } = new();
}

public sealed class RequirementCoverage
{
    public ConstructionRequirement Requirement { get; set; } = new();
    public int FoundCount { get; set; }
    public bool IsSatisfied => FoundCount >= Requirement.MinimumCount;
}

public sealed class CompletenessReport
{
    public List<RequirementCoverage> Coverage { get; set; } = new();
    public int RequiredSatisfied { get; set; }
    public int RequiredTotal { get; set; }
    public int RecommendedSatisfied { get; set; }
    public int RecommendedTotal { get; set; }
    public int OptionalSatisfied { get; set; }
    public int OptionalTotal { get; set; }
    public double Score { get; set; }
}
