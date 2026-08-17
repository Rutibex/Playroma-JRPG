using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Tilesets.Services;

public static class TileAddressCodec
{
    private const int Multiplier = 10000;
    public static int Encode(MvSheetSlot sheet, int index) => ((int)sheet + 1) * Multiplier + index;
    public static bool TryDecode(int address, out MvSheetSlot sheet, out int index)
    {
        var raw = address / Multiplier - 1;
        if (raw < 0 || raw > (int)MvSheetSlot.E)
        {
            sheet = default;
            index = -1;
            return false;
        }
        sheet = (MvSheetSlot)raw;
        index = address % Multiplier;
        return true;
    }
}
