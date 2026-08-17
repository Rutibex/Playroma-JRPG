using Playroma.Tile.Materials.Buffers;

namespace Playroma.Tile.Materials.Models;

public sealed class MaterialResult : IDisposable
{
    public Dictionary<MaterialChannel, MaterialValue> Channels { get; } = new();
    public List<ColorBuffer> Frames { get; } = new();
    public int Seed { get; init; }
    public string RecipeHash { get; init; } = string.Empty;
    public IReadOnlyList<MaterialIssue> Issues { get; init; } = Array.Empty<MaterialIssue>();

    public ColorBuffer? BaseColor => Channels.TryGetValue(MaterialChannel.BaseColor, out var value) ? value.Color : null;
    public ScalarField? Height => Channels.TryGetValue(MaterialChannel.Height, out var value) ? value.Scalar : null;

    public void Dispose()
    {
        Channels.Clear();
        Frames.Clear();
    }
}

public sealed class MaterialValue
{
    public MaterialDataType Type { get; private init; }
    public ColorBuffer? Color { get; private init; }
    public ScalarField? Scalar { get; private init; }
    public VectorField? Vector { get; private init; }
    public int Integer { get; private init; }
    public IReadOnlyList<Rgba32>? Palette { get; private init; }

    public static MaterialValue From(ColorBuffer value) => new() { Type = MaterialDataType.Color, Color = value };
    public static MaterialValue From(ScalarField value, bool mask = false) => new() { Type = mask ? MaterialDataType.Mask : MaterialDataType.Scalar, Scalar = value };
    public static MaterialValue From(VectorField value) => new() { Type = MaterialDataType.Vector2, Vector = value };
    public static MaterialValue From(int value) => new() { Type = MaterialDataType.Integer, Integer = value };
    public static MaterialValue From(IReadOnlyList<Rgba32> value) => new() { Type = MaterialDataType.Palette, Palette = value };
}

public enum MaterialIssueSeverity { Information, Warning, Error }
public sealed record MaterialIssue(MaterialIssueSeverity Severity, string Code, string Message, Guid? NodeId = null);
