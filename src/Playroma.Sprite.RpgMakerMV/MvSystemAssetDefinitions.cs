using Playroma.Sprite.Core;

namespace Playroma.Sprite.RpgMakerMV;

public enum MvSystemAssetKind
{
    IconSet,
    Window,
    Balloon,
    Damage,
    States,
    Weapons1,
    Weapons2,
    Weapons3,
    ButtonSet,
    Loading,
    GameOver,
    Shadow1,
    Shadow2
}

public enum MvSystemRegionRole
{
    Cell,
    Background,
    Frame,
    Cursor,
    Arrow,
    Pause,
    Digit,
    Symbol,
    State,
    Weapon,
    Button,
    Shadow,
    FullImage,
    ColorPalette
}

public sealed record MvSystemAssetRegion(
    string Id,
    string DisplayName,
    IntRect Bounds,
    MvSystemRegionRole Role,
    int Index = 0,
    int Frame = 0,
    string? Description = null);

public sealed record MvSystemAssetDefinition(
    string ProfileId,
    MvSystemAssetKind Kind,
    string DisplayName,
    string ReservedFileName,
    int StockWidth,
    int StockHeight,
    int CellWidth,
    int CellHeight,
    int Columns,
    int Rows,
    bool Animated,
    int FramesPerItem,
    string ItemNoun,
    string Description)
{
    public int ItemCount => Kind switch
    {
        MvSystemAssetKind.ButtonSet => 5,
        _ => FramesPerItem <= 0 ? Math.Max(1, Columns * Rows) : Math.Max(1, Columns * Rows / FramesPerItem)
    };
}

public sealed record MvSystemAssetDiagnostic(string Severity, string Code, string Message);

public static class MvSystemAssetCatalog
{
    private static readonly IReadOnlyList<MvSystemAssetDefinition> definitions =
    [
        new("mv-iconset", MvSystemAssetKind.IconSet, "IconSet Editor", "IconSet.png", 512, 640, 32, 32, 16, 20, false, 1, "Icon", "Edit 32×32 icons with a sixteen-column picker and enlarged runtime preview."),
        new("mv-window", MvSystemAssetKind.Window, "Window Skin Editor", "Window.png", 192, 192, 48, 48, 4, 4, true, 4, "Window region", "Edit the stock 192×192 window skin with background, frame, cursor, arrow, and pause regions."),
        new("mv-balloon", MvSystemAssetKind.Balloon, "Balloon Editor", "Balloon.png", 384, 720, 48, 48, 8, 15, true, 8, "Balloon", "Edit fifteen balloon types with eight 48×48 animation frames each."),
        new("mv-damage", MvSystemAssetKind.Damage, "Damage Number Editor", "Damage.png", 240, 160, 24, 32, 10, 5, false, 10, "Damage row", "Edit the five stock digit and symbol rows and preview composed damage values."),
        new("mv-states", MvSystemAssetKind.States, "State Overlay Editor", "States.png", 768, 960, 96, 96, 8, 10, true, 8, "State overlay", "Edit ten stock state-overlay rows with eight 96×96 animation frames each."),
        new("mv-weapons1", MvSystemAssetKind.Weapons1, "Weapon Sheet 1 Editor", "Weapons1.png", 576, 384, 96, 64, 6, 6, true, 3, "Weapon", "Edit twelve stock weapon animation blocks arranged as two three-frame groups across six rows."),
        new("mv-weapons2", MvSystemAssetKind.Weapons2, "Weapon Sheet 2 Editor", "Weapons2.png", 576, 384, 96, 64, 6, 6, true, 3, "Weapon", "Edit twelve additional weapon animation blocks arranged as two three-frame groups across six rows."),
        new("mv-weapons3", MvSystemAssetKind.Weapons3, "Weapon Sheet 3 Editor", "Weapons3.png", 288, 384, 96, 64, 3, 6, true, 3, "Weapon", "Edit six user-defined weapon animation blocks with three frames per motion."),
        new("mv-buttonset", MvSystemAssetKind.ButtonSet, "Touch Button Editor", "ButtonSet.png", 288, 96, 48, 48, 6, 2, false, 2, "Button", "Edit the four 48×48 quantity buttons and the 96×48 OK button in normal and pressed states."),
        new("mv-loading", MvSystemAssetKind.Loading, "Loading Image Editor", "Loading.png", 400, 100, 400, 100, 1, 1, false, 1, "Image", "Edit and preview the stock loading image centered on an MV game canvas."),
        new("mv-gameover", MvSystemAssetKind.GameOver, "Game Over Editor", "GameOver.png", 816, 624, 816, 624, 1, 1, false, 1, "Image", "Edit the full-screen Game Over image with 816×624 runtime framing."),
        new("mv-shadow1", MvSystemAssetKind.Shadow1, "Airship Shadow Editor", "Shadow1.png", 48, 48, 48, 48, 1, 1, false, 1, "Shadow", "Edit and preview the stock airship shadow beneath a vehicle silhouette."),
        new("mv-shadow2", MvSystemAssetKind.Shadow2, "Side-view Actor Shadow Editor", "Shadow2.png", 82, 38, 82, 38, 1, 1, false, 1, "Shadow", "Edit and preview the stock side-view actor shadow beneath a battler silhouette.")
    ];

    public static IReadOnlyList<MvSystemAssetDefinition> Definitions => definitions;

    public static bool TryGet(string? profileId, out MvSystemAssetDefinition? definition)
    {
        definition = definitions.FirstOrDefault(item => string.Equals(item.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        return definition is not null;
    }

    public static MvSystemAssetDefinition GetRequired(string profileId) =>
        TryGet(profileId, out MvSystemAssetDefinition? definition) ? definition : throw new KeyNotFoundException($"'{profileId}' is not a specialized MV system-asset profile.");

    public static IReadOnlyList<MvSystemAssetRegion> GetRegions(MvSystemAssetDefinition definition, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (width <= 0 || height <= 0) return [];
        if (definition.Kind == MvSystemAssetKind.Window) return WindowRegions(width, height);
        if (definition.Kind == MvSystemAssetKind.ButtonSet) return ButtonRegions(width, height);
        if (definition.Kind is MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 or MvSystemAssetKind.Weapons3)
            return WeaponRegions(definition, width, height);
        if (definition.Kind is MvSystemAssetKind.Loading or MvSystemAssetKind.GameOver or MvSystemAssetKind.Shadow1 or MvSystemAssetKind.Shadow2)
            return [new("image", definition.ItemNoun, new IntRect(0, 0, width, height), definition.Kind is MvSystemAssetKind.Shadow1 or MvSystemAssetKind.Shadow2 ? MvSystemRegionRole.Shadow : MvSystemRegionRole.FullImage)];

        (int columns, int rows, int cellWidth, int cellHeight) = ResolveGrid(definition, width, height);
        List<MvSystemAssetRegion> regions = [];
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int cellIndex = row * columns + column;
            int item = definition.FramesPerItem > 0 ? cellIndex / definition.FramesPerItem : cellIndex;
            int frame = definition.FramesPerItem > 0 ? cellIndex % definition.FramesPerItem : 0;
            MvSystemRegionRole role = definition.Kind switch
            {
                MvSystemAssetKind.IconSet => MvSystemRegionRole.Cell,
                MvSystemAssetKind.Balloon => MvSystemRegionRole.Cell,
                MvSystemAssetKind.Damage => row == 0 ? MvSystemRegionRole.Digit : MvSystemRegionRole.Symbol,
                MvSystemAssetKind.States => MvSystemRegionRole.State,
                MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 or MvSystemAssetKind.Weapons3 => MvSystemRegionRole.Weapon,
                MvSystemAssetKind.ButtonSet => MvSystemRegionRole.Button,
                _ => MvSystemRegionRole.Cell
            };
            string name = definition.Kind switch
            {
                MvSystemAssetKind.IconSet => $"Icon {cellIndex}",
                MvSystemAssetKind.Balloon => $"Balloon {item + 1}, frame {frame + 1}",
                MvSystemAssetKind.Damage => $"Row {row + 1}, glyph {column}",
                MvSystemAssetKind.States => $"State overlay {item + 1}, frame {frame + 1}",
                MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 or MvSystemAssetKind.Weapons3 => $"Weapon {item + 1}, frame {frame + 1}",
                MvSystemAssetKind.ButtonSet => $"Button {cellIndex + 1}",
                _ => $"Cell {cellIndex}"
            };
            regions.Add(new($"cell-{cellIndex}", name, new IntRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight), role, item, frame));
        }
        return regions;
    }

    public static IReadOnlyList<MvSystemAssetDiagnostic> Validate(MvSystemAssetDefinition definition, string? fileName, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<MvSystemAssetDiagnostic> issues = [];
        if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(Path.GetFileName(fileName), definition.ReservedFileName, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("error", "PSPR-SYS-001", $"This asset must be installed as {definition.ReservedFileName}."));
        if (width <= 0 || height <= 0)
        {
            issues.Add(new("error", "PSPR-SYS-002", "The image has invalid dimensions."));
            return issues;
        }
        if (definition.Kind == MvSystemAssetKind.IconSet)
        {
            if (width % 32 != 0 || height % 32 != 0) issues.Add(new("error", "PSPR-SYS-003", "IconSet dimensions must be divisible by 32 pixels."));
            if (width != 512) issues.Add(new("warning", "PSPR-SYS-004", "The stock MV IconSet is 512 pixels wide (sixteen icons)."));
        }
        else if (definition.Kind == MvSystemAssetKind.Window)
        {
            if (width != 192 || height != 192) issues.Add(new("error", "PSPR-SYS-005", "The stock MV Window skin must be 192×192 pixels."));
        }
        else if (definition.Kind == MvSystemAssetKind.ButtonSet)
        {
            if (width != 288 || height != 96) issues.Add(new("error", "PSPR-SYS-010", "The stock MV ButtonSet must be exactly 288×96 pixels."));
        }
        else if (definition.Kind == MvSystemAssetKind.Damage)
        {
            if (width % 10 != 0 || height % 5 != 0) issues.Add(new("error", "PSPR-SYS-012", "Damage.png must divide into ten columns and five rows."));
            if (width != definition.StockWidth || height != definition.StockHeight) issues.Add(new("warning", "PSPR-SYS-009", $"The stock MV sheet size is {definition.StockWidth}×{definition.StockHeight}; plugin variants may differ."));
        }
        else if (definition.Kind is MvSystemAssetKind.Loading)
        {
            if (width != definition.StockWidth || height != definition.StockHeight) issues.Add(new("warning", "PSPR-SYS-006", $"The stock loading image is {definition.StockWidth}×{definition.StockHeight}."));
        }
        else if (definition.Kind is MvSystemAssetKind.GameOver or MvSystemAssetKind.Shadow1 or MvSystemAssetKind.Shadow2)
        {
            if (width != definition.StockWidth || height != definition.StockHeight) issues.Add(new("warning", "PSPR-SYS-007", $"The stock asset size is {definition.StockWidth}×{definition.StockHeight}."));
        }
        else
        {
            if (width % definition.CellWidth != 0 || height % definition.CellHeight != 0)
                issues.Add(new("error", "PSPR-SYS-008", $"Dimensions must be divisible by {definition.CellWidth}×{definition.CellHeight}."));
            if (width != definition.StockWidth || height != definition.StockHeight)
                issues.Add(new("warning", "PSPR-SYS-009", $"The stock MV sheet size is {definition.StockWidth}×{definition.StockHeight}; plugin variants may differ."));
        }
        return issues;
    }

    private static IReadOnlyList<MvSystemAssetRegion> WindowRegions(int width, int height)
    {
        if (width < 192 || height < 192)
            return [new("window", "Window skin", new IntRect(0, 0, width, height), MvSystemRegionRole.FullImage)];
        return
        [
            new("background-tone", "Window background tone", new IntRect(0, 0, 96, 96), MvSystemRegionRole.Background, Description: "Stretched base background and tone source."),
            new("background-pattern", "Window background pattern", new IntRect(0, 96, 96, 96), MvSystemRegionRole.Background, Description: "Tiled background pattern source."),
            new("frame", "Window frame", new IntRect(96, 0, 96, 96), MvSystemRegionRole.Frame, Description: "Twenty-four-pixel frame corners and edges."),
            new("cursor", "Selection cursor", new IntRect(96, 96, 48, 48), MvSystemRegionRole.Cursor, Description: "Four-pixel nine-sliced selection cursor."),
            new("up-arrow", "Up scroll arrow", new IntRect(132, 24, 24, 12), MvSystemRegionRole.Arrow),
            new("down-arrow", "Down scroll arrow", new IntRect(132, 60, 24, 12), MvSystemRegionRole.Arrow),
            new("pause", "Pause animation", new IntRect(144, 96, 48, 48), MvSystemRegionRole.Pause, Description: "Four 24×24 animation frames in a 2×2 block."),
            new("text-colors", "Text and gauge colors", new IntRect(96, 144, 96, 48), MvSystemRegionRole.ColorPalette, Description: "Thirty-two 12×12 color swatches sampled at each swatch center.")
        ];
    }

    private static IReadOnlyList<MvSystemAssetRegion> ButtonRegions(int width, int height)
    {
        if (width < 288 || height < 96)
            return [new("buttonset", "Touch button set", new IntRect(0, 0, width, height), MvSystemRegionRole.FullImage)];
        string[] names = ["Decrease by 10", "Decrease by 1", "Increase by 1", "Increase by 10", "Confirm"];
        int[] xs = [0, 48, 96, 144, 192];
        int[] widths = [48, 48, 48, 48, 96];
        List<MvSystemAssetRegion> regions = [];
        for (int item = 0; item < names.Length; item++)
        for (int state = 0; state < 2; state++)
            regions.Add(new(
                $"button-{item}-{(state == 0 ? "normal" : "pressed")}",
                $"{names[item]} — {(state == 0 ? "normal" : "pressed")}",
                new IntRect(xs[item], state * 48, widths[item], 48),
                MvSystemRegionRole.Button,
                item,
                state));
        return regions;
    }

    public static (int Columns, int Rows, int CellWidth, int CellHeight) ResolveGrid(MvSystemAssetDefinition definition, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Kind == MvSystemAssetKind.Damage)
            return (10, 5, Math.Max(1, width / 10), Math.Max(1, height / 5));
        return (
            Math.Max(1, width / Math.Max(1, definition.CellWidth)),
            Math.Max(1, height / Math.Max(1, definition.CellHeight)),
            Math.Max(1, definition.CellWidth),
            Math.Max(1, definition.CellHeight));
    }

    private static IReadOnlyList<MvSystemAssetRegion> WeaponRegions(MvSystemAssetDefinition definition, int width, int height)
    {
        int columns = Math.Max(1, width / Math.Max(1, definition.CellWidth));
        int rows = Math.Max(1, height / Math.Max(1, definition.CellHeight));
        int itemCount = definition.Kind == MvSystemAssetKind.Weapons3 ? Math.Min(6, rows) : Math.Min(12, rows * 2);
        List<MvSystemAssetRegion> regions = [];
        for (int item = 0; item < itemCount; item++)
        {
            int group = definition.Kind == MvSystemAssetKind.Weapons3 ? 0 : item / 6;
            int row = item % 6;
            for (int frame = 0; frame < 3; frame++)
            {
                int column = group * 3 + frame;
                if (column >= columns || row >= rows) continue;
                regions.Add(new(
                    $"weapon-{item}-frame-{frame}",
                    $"Weapon {item + 1}, frame {frame + 1}",
                    new IntRect(column * definition.CellWidth, row * definition.CellHeight, definition.CellWidth, definition.CellHeight),
                    MvSystemRegionRole.Weapon,
                    item,
                    frame,
                    "MV weapon frames are 96×64 and each weapon uses three horizontal frames."));
            }
        }
        return regions;
    }
}
