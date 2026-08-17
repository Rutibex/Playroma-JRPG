namespace Playroma.Tile.Materials.Models;

public sealed class MaterialRecipe
{
    public int FormatVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Material";
    public string Description { get; set; } = string.Empty;
    public MaterialCategory Category { get; set; } = MaterialCategory.Custom;
    public List<string> Tags { get; set; } = new();
    public MaterialGraph Graph { get; set; } = new();
    public List<ExposedMaterialParameter> ExposedParameters { get; set; } = new();
    public int Revision { get; set; } = 1;
    public int Seed { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public MaterialStyleProfile StyleProfile { get; set; } = new();
    public MaterialProvenance Provenance { get; set; } = new();
    public List<MaterialGenerationRecord> History { get; set; } = new();
}

public sealed class ExposedMaterialParameter
{
    public string DisplayName { get; set; } = string.Empty;
    public Guid NodeId { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public float Minimum { get; set; }
    public float Maximum { get; set; } = 1f;
    public float Step { get; set; } = 0.01f;
    public bool IsSeedLike { get; set; }
}

public sealed class MaterialStyleProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Classic RPG Maker";
    public MaterialStyle Style { get; set; } = MaterialStyle.ClassicRpgMaker;
    public List<string> Palette { get; set; } = new();
    public float Contrast { get; set; } = 1f;
    public float Saturation { get; set; } = 1f;
    public float DetailScale { get; set; } = 1f;
    public float TextureStrength { get; set; } = 1f;
    public float OutlineStrength { get; set; }
    public float Wear { get; set; }
    public float LightAngleDegrees { get; set; } = 315f;
    public float LightHeight { get; set; } = 0.7f;
    public float ShadowStrength { get; set; } = 0.35f;
    public int ColorBudget { get; set; } = 32;
    public DitherMode Dither { get; set; } = DitherMode.None;
}

public sealed class MaterialGenerationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Seed { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, MaterialParameterValue> Parameters { get; set; } = new();
    public string ResultHash { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
    public int? Rating { get; set; }
}

public sealed class MaterialProvenance
{
    public string Creator { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string SourceDescription { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string AiProvider { get; set; } = string.Empty;
    public string AiModel { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
