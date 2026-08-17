namespace Playroma.Tile.Tilesets.Models;

public sealed class TestMapDefinition
{
    public int Width { get; set; } = 20;
    public int Height { get; set; } = 15;
    public Dictionary<TestMapLayer, int[]> Layers { get; set; } = new();
    public int PlayerX { get; set; } = 2;
    public int PlayerY { get; set; } = 2;
    public string Name { get; set; } = "Tileset Test Map";

    public static TestMapDefinition CreateDefault(int width = 20, int height = 15)
    {
        var map = new TestMapDefinition { Width = width, Height = height };
        foreach (var layer in Enum.GetValues<TestMapLayer>())
            map.Layers[layer] = Enumerable.Repeat(-1, width * height).ToArray();
        return map;
    }

    public int Get(TestMapLayer layer, int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return -1;
        return Layers.TryGetValue(layer, out var values) && values.Length == Width * Height
            ? values[y * Width + x]
            : -1;
    }

    public void Set(TestMapLayer layer, int x, int y, int value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        if (!Layers.TryGetValue(layer, out var values) || values.Length != Width * Height)
        {
            values = Enumerable.Repeat(-1, Width * Height).ToArray();
            Layers[layer] = values;
        }
        values[y * Width + x] = value;
    }
}
