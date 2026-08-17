namespace Playroma.Tile.Materials.Models;

public sealed record MaterialEvaluationContext
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int TileWidth { get; init; } = 48;
    public int TileHeight { get; init; } = 48;
    public int Seed { get; init; }
    public int FrameIndex { get; init; }
    public int FrameCount { get; init; } = 1;
    public bool WrapX { get; init; } = true;
    public bool WrapY { get; init; } = true;
    public MaterialQuality Quality { get; init; } = MaterialQuality.Interactive;
    public CancellationToken CancellationToken { get; init; }
    public IProgress<MaterialProgress>? Progress { get; init; }
    public Func<string, string>? AssetResolver { get; init; }
}

public readonly record struct MaterialProgress(string Stage, float Fraction, Guid? NodeId = null);
