using System.Drawing;
using Playroma.Tile.Autotiles.Models;
using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Autotiles.Services;

public static class AutotileKindInfo
{
    public static AutotileFamily GetFamily(AutotileKind kind) => kind switch
    {
        AutotileKind.A1Waterfall => AutotileFamily.Waterfall,
        AutotileKind.A3Building or AutotileKind.A4Wall => AutotileFamily.Wall,
        _ => AutotileFamily.Floor
    };

    public static Size GetSourceSize(AutotileKind kind) => kind switch
    {
        AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water => new Size(288, 144),
        AutotileKind.A1Waterfall => new Size(96, 144),
        AutotileKind.A3Building or AutotileKind.A4Wall => new Size(96, 96),
        _ => new Size(96, 144)
    };

    public static int GetFrameCount(AutotileKind kind) => kind switch
    {
        AutotileKind.A1Ocean or AutotileKind.A1DeepOcean or AutotileKind.A1Water or AutotileKind.A1Waterfall => 3,
        _ => 1
    };

    public static bool IsCompatible(AutotileKind kind, MvSheetSlot slot, int regionIndex)
    {
        return slot switch
        {
            MvSheetSlot.A1 => kind switch
            {
                AutotileKind.A1Ocean => regionIndex == 0,
                AutotileKind.A1DeepOcean => regionIndex == 1,
                AutotileKind.A1OceanDecoration => regionIndex is 2 or 3,
                AutotileKind.A1Water => regionIndex >= 4 && regionIndex % 2 == 0,
                AutotileKind.A1Waterfall => regionIndex >= 5 && regionIndex % 2 == 1,
                _ => false
            },
            MvSheetSlot.A2 => kind == AutotileKind.A2Floor,
            MvSheetSlot.A3 => kind == AutotileKind.A3Building,
            MvSheetSlot.A4 => regionIndex >= 0 && regionIndex < 48 &&
                              (regionIndex / 8 % 2 == 0
                                  ? kind == AutotileKind.A4Floor
                                  : kind == AutotileKind.A4Wall),
            _ => false
        };
    }

    public static AutotileKind InferKind(MvSheetSlot slot, int regionIndex) => slot switch
    {
        MvSheetSlot.A1 when regionIndex == 0 => AutotileKind.A1Ocean,
        MvSheetSlot.A1 when regionIndex == 1 => AutotileKind.A1DeepOcean,
        MvSheetSlot.A1 when regionIndex is 2 or 3 => AutotileKind.A1OceanDecoration,
        MvSheetSlot.A1 when regionIndex >= 4 && regionIndex % 2 == 0 => AutotileKind.A1Water,
        MvSheetSlot.A1 => AutotileKind.A1Waterfall,
        MvSheetSlot.A2 => AutotileKind.A2Floor,
        MvSheetSlot.A3 => AutotileKind.A3Building,
        MvSheetSlot.A4 when regionIndex / 8 % 2 == 0 => AutotileKind.A4Floor,
        MvSheetSlot.A4 => AutotileKind.A4Wall,
        _ => throw new ArgumentException($"Sheet {slot} region {regionIndex} is not an autotile region.")
    };
}
