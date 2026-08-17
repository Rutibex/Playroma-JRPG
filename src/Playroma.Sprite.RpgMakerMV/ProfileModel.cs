namespace Playroma.Sprite.RpgMakerMV;

public sealed class RpgMakerMvProfileCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Engine { get; set; } = "RPG Maker MV";
    public List<RpgMakerMvImageProfile> Profiles { get; set; } = [];
}

public sealed class RpgMakerMvImageProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AssetFolder { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RuntimeFormat { get; set; } = "png";
    public string ValidationMode { get; set; } = "strict";
    public MvDimensionRule Dimensions { get; set; } = new();
    public MvGridRule? Grid { get; set; }
    public MvFilenameRules FilenameRules { get; set; } = new();
    public string? TemplateKey { get; set; }
    public List<MvMotionDefinition> Motions { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MvDimensionRule
{
    public int? FixedWidth { get; set; }
    public int? FixedHeight { get; set; }
    public int? RecommendedWidth { get; set; }
    public int? RecommendedHeight { get; set; }
    public bool Arbitrary { get; set; }
}

public sealed class MvGridRule
{
    public int? Columns { get; set; }
    public int? Rows { get; set; }
    public int? MinimumRows { get; set; }
    public int? MaximumRows { get; set; }
    public int? CellWidth { get; set; }
    public int? CellHeight { get; set; }
    public int? MaximumCells { get; set; }
}

public sealed class MvFilenameRules
{
    public bool RequireDollarPrefix { get; set; }
    public bool AllowDollarPrefix { get; set; }
    public bool AllowObjectPrefix { get; set; }
    public bool AllowParallaxFloorPrefix { get; set; }
}

public sealed class MvMotionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int StartCell { get; set; }
    public int FrameCount { get; set; } = 3;
    public string Playback { get; set; } = "loop";
}
