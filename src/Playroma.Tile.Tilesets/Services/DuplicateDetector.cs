using System.Drawing;
using System.Security.Cryptography;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Tilesets.Models;

namespace Playroma.Tile.Tilesets.Services;

public sealed class DuplicateDetector
{
    public IReadOnlyList<TileDuplicateMatch> FindDuplicates(
        IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets,
        double similarityThreshold = 0.985)
    {
        var tiles = ExtractTiles(sheets).ToArray();
        var result = new List<TileDuplicateMatch>();
        var exactGroups = tiles.GroupBy(x => x.Hash).Where(x => x.Count() > 1);
        var exactPairs = new HashSet<(MvSheetSlot, int, MvSheetSlot, int)>();
        foreach (var group in exactGroups)
        {
            var items = group.ToArray();
            for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
            {
                result.Add(Create(items[i], items[j], 1, true));
                exactPairs.Add((items[i].Sheet, items[i].Index, items[j].Sheet, items[j].Index));
            }
        }

        for (var i = 0; i < tiles.Length; i++)
        for (var j = i + 1; j < tiles.Length; j++)
        {
            if (exactPairs.Contains((tiles[i].Sheet, tiles[i].Index, tiles[j].Sheet, tiles[j].Index))) continue;
            if (Math.Abs(tiles[i].AverageAlpha - tiles[j].AverageAlpha) > 0.08) continue;
            var similarity = Similarity(tiles[i].Signature, tiles[j].Signature);
            if (similarity >= similarityThreshold)
                result.Add(Create(tiles[i], tiles[j], similarity, false));
        }
        return result.OrderByDescending(x => x.Similarity).ToArray();
    }

    private static TileDuplicateMatch Create(TileSample a, TileSample b, double similarity, bool exact) => new()
    {
        FirstSheet = a.Sheet,
        FirstCell = a.Index,
        SecondSheet = b.Sheet,
        SecondCell = b.Index,
        Similarity = similarity,
        IsExact = exact
    };

    private static IEnumerable<TileSample> ExtractTiles(IReadOnlyDictionary<MvSheetSlot, Bitmap> sheets)
    {
        foreach (var pair in sheets)
        {
            var columns = pair.Value.Width / 48;
            var rows = pair.Value.Height / 48;
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var signature = new float[8 * 8 * 4];
                var alpha = 0d;
                var p = 0;
                for (var sy = 0; sy < 8; sy++)
                for (var sx = 0; sx < 8; sx++)
                {
                    var color = pair.Value.GetPixel(x * 48 + sx * 6 + 3, y * 48 + sy * 6 + 3);
                    signature[p++] = color.R / 255f;
                    signature[p++] = color.G / 255f;
                    signature[p++] = color.B / 255f;
                    signature[p++] = color.A / 255f;
                    alpha += color.A / 255d;
                }
                using var sha = SHA256.Create();
                var bytes = new byte[48 * 48 * 4];
                var index = 0;
                for (var py = 0; py < 48; py++)
                for (var px = 0; px < 48; px++)
                {
                    var color = pair.Value.GetPixel(x * 48 + px, y * 48 + py);
                    bytes[index++] = color.R; bytes[index++] = color.G; bytes[index++] = color.B; bytes[index++] = color.A;
                }
                yield return new TileSample(pair.Key, y * columns + x, Convert.ToHexString(sha.ComputeHash(bytes)), signature, alpha / 64d);
            }
        }
    }

    private static double Similarity(float[] a, float[] b)
    {
        double error = 0;
        for (var i = 0; i < a.Length; i++) error += Math.Abs(a[i] - b[i]);
        return Math.Clamp(1d - error / a.Length, 0d, 1d);
    }

    private sealed record TileSample(MvSheetSlot Sheet, int Index, string Hash, float[] Signature, double AverageAlpha);
}
