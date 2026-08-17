using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

RpgMakerMvProfileRegistry registry = RpgMakerMvProfileRegistry.LoadBuiltIn();
IReadOnlyList<MvSystemAssetDefinition> definitions = MvSystemAssetCatalog.Definitions;
Check(definitions.Count == 13, "Expected thirteen specialized system-asset definitions.");
Check(definitions.Select(item => item.ProfileId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 13, "System profile IDs are not unique.");
Check(definitions.Select(item => item.ReservedFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 13, "Reserved system filenames are not unique.");

Dictionary<string, (int Width, int Height)> stockDimensions = new(StringComparer.OrdinalIgnoreCase)
{
    ["mv-iconset"] = (512, 640), ["mv-window"] = (192, 192), ["mv-balloon"] = (384, 720),
    ["mv-damage"] = (240, 160), ["mv-states"] = (768, 960), ["mv-weapons1"] = (576, 384),
    ["mv-weapons2"] = (576, 384), ["mv-weapons3"] = (288, 384), ["mv-buttonset"] = (288, 96),
    ["mv-loading"] = (400, 100), ["mv-gameover"] = (816, 624), ["mv-shadow1"] = (48, 48),
    ["mv-shadow2"] = (82, 38)
};
foreach (MvSystemAssetDefinition definition in definitions)
{
    (int expectedWidth, int expectedHeight) = stockDimensions[definition.ProfileId];
    Check(definition.StockWidth == expectedWidth && definition.StockHeight == expectedHeight, definition.ProfileId + " stock dimensions drifted from the verified MV sample contract.");
}

Dictionary<MvSystemAssetKind, int> expectedRegions = new()
{
    [MvSystemAssetKind.IconSet] = 320,
    [MvSystemAssetKind.Window] = 8,
    [MvSystemAssetKind.Balloon] = 120,
    [MvSystemAssetKind.Damage] = 50,
    [MvSystemAssetKind.States] = 80,
    [MvSystemAssetKind.Weapons1] = 36,
    [MvSystemAssetKind.Weapons2] = 36,
    [MvSystemAssetKind.Weapons3] = 18,
    [MvSystemAssetKind.ButtonSet] = 10,
    [MvSystemAssetKind.Loading] = 1,
    [MvSystemAssetKind.GameOver] = 1,
    [MvSystemAssetKind.Shadow1] = 1,
    [MvSystemAssetKind.Shadow2] = 1
};

foreach (MvSystemAssetDefinition definition in definitions)
{
    RpgMakerMvImageProfile profile = registry.GetRequired(definition.ProfileId);
    Check(profile.AssetFolder == "img/system", definition.ProfileId + " is not routed to img/system.");
    Check(profile.Metadata.TryGetValue("reservedFileName", out string? reserved) && reserved == definition.ReservedFileName,
        definition.ProfileId + " profile metadata has the wrong reserved filename.");
    PixelBuffer template = MvSystemAssetTemplateFactory.Create(definition);
    Check(template.Width == definition.StockWidth && template.Height == definition.StockHeight, definition.ProfileId + " template dimensions are wrong.");
    IReadOnlyList<MvSystemAssetDiagnostic> valid = MvSystemAssetCatalog.Validate(definition, definition.ReservedFileName, template.Width, template.Height);
    Check(valid.All(issue => issue.Severity != "error"), definition.ProfileId + " stock template fails validation.");
    IReadOnlyList<MvSystemAssetRegion> regions = MvSystemAssetCatalog.GetRegions(definition, template.Width, template.Height);
    Check(regions.Count == expectedRegions[definition.Kind], definition.ProfileId + " named-region count is wrong.");
    Check(regions.All(region => region.Bounds.X >= 0 && region.Bounds.Y >= 0 && region.Bounds.Right <= template.Width && region.Bounds.Bottom <= template.Height),
        definition.ProfileId + " has an out-of-bounds named region.");

    PixelBuffer previewSource = CreatePreviewSource(definition, regions);
    PixelBuffer preview = MvSystemAssetPreviewRenderer.Render(previewSource, definition, new(1, 1, "8675309"));
    Check(preview.Width == 420 && preview.Height == 260, definition.ProfileId + " preview dimensions are wrong.");
    Check(preview.ComputeSha256() != new PixelBuffer(420, 260).ComputeSha256(), definition.ProfileId + " preview is empty.");

    IReadOnlyList<MvSystemAssetDiagnostic> wrongName = MvSystemAssetCatalog.Validate(definition, "WrongName.png", template.Width, template.Height);
    Check(wrongName.Any(issue => issue.Code == "PSPR-SYS-001" && issue.Severity == "error"), definition.ProfileId + " did not enforce its reserved filename.");
}

MvSystemAssetDefinition window = MvSystemAssetCatalog.GetRequired("mv-window");
Check(MvSystemAssetCatalog.GetRegions(window, 192, 192).Select(region => region.Id).SequenceEqual(
    ["background-tone", "background-pattern", "frame", "cursor", "up-arrow", "down-arrow", "pause", "text-colors"]), "Window regions are incomplete or misordered.");
Check(MvSystemAssetCatalog.Validate(window, "Window.png", 191, 192).Any(issue => issue.Code == "PSPR-SYS-005"), "Window dimensions were not enforced.");

MvSystemAssetDefinition weapons1 = MvSystemAssetCatalog.GetRequired("mv-weapons1");
Check(MvSystemAssetPreviewRenderer.ResolveCell(weapons1, 576, 384, 0, 0) == new IntRect(0, 0, 96, 64), "Weapons1 item 1 frame 1 is wrong.");
Check(MvSystemAssetPreviewRenderer.ResolveCell(weapons1, 576, 384, 5, 2) == new IntRect(192, 320, 96, 64), "Weapons1 item 6 frame 3 is wrong.");
Check(MvSystemAssetPreviewRenderer.ResolveCell(weapons1, 576, 384, 6, 0) == new IntRect(288, 0, 96, 64), "Weapons1 second weapon group starts at the wrong cell.");
Check(MvSystemAssetPreviewRenderer.ResolveCell(weapons1, 576, 384, 11, 2) == new IntRect(480, 320, 96, 64), "Weapons1 final frame is wrong.");

MvSystemAssetDefinition weapons3 = MvSystemAssetCatalog.GetRequired("mv-weapons3");
Check(MvSystemAssetPreviewRenderer.ResolveCell(weapons3, 288, 384, 5, 2) == new IntRect(192, 320, 96, 64), "Weapons3 final frame is wrong.");

MvSystemAssetDefinition states = MvSystemAssetCatalog.GetRequired("mv-states");
Check(MvSystemAssetPreviewRenderer.ResolveCell(states, 768, 960, 9, 7) == new IntRect(672, 864, 96, 96), "State overlay row ten final frame is wrong.");

MvSystemAssetDefinition buttonSet = MvSystemAssetCatalog.GetRequired("mv-buttonset");
Check(MvSystemAssetPreviewRenderer.ResolveCell(buttonSet, 288, 96, 4, 1) == new IntRect(192, 48, 96, 48), "ButtonSet confirm pressed frame is wrong.");
Check(MvSystemAssetCatalog.Validate(buttonSet, "ButtonSet.png", 96, 96).Any(issue => issue.Code == "PSPR-SYS-010"), "Incomplete ButtonSet was not rejected.");

MvSystemAssetDefinition damage = MvSystemAssetCatalog.GetRequired("mv-damage");
Check(MvSystemAssetPreviewRenderer.ResolveCell(damage, 240, 160, 4, 9) == new IntRect(216, 128, 24, 32), "Damage glyph addressing is wrong.");
Check(MvSystemAssetPreviewRenderer.ResolveCell(damage, 480, 320, 4, 9) == new IntRect(432, 256, 48, 64), "Scaled Damage glyph addressing is wrong.");

MvSystemAssetDefinition balloon = MvSystemAssetCatalog.GetRequired("mv-balloon");
Check(MvSystemAssetPreviewRenderer.ResolveCell(balloon, 384, 720, 14, 7) == new IntRect(336, 672, 48, 48), "Balloon final animation cell is wrong.");

MvSystemAssetDefinition iconSet = MvSystemAssetCatalog.GetRequired("mv-iconset");
Check(MvSystemAssetCatalog.Validate(iconSet, "IconSet.png", 510, 320).Any(issue => issue.Code == "PSPR-SYS-003"), "IconSet divisibility was not enforced.");

Console.WriteLine($"Playroma Sprite specialized MV system-asset tests passed: {checks} checks.");

static PixelBuffer CreatePreviewSource(MvSystemAssetDefinition definition, IReadOnlyList<MvSystemAssetRegion> regions)
{
    PixelBuffer source = MvSystemAssetTemplateFactory.Create(definition);
    int regionNumber = 0;
    foreach (MvSystemAssetRegion region in regions)
    {
        Rgba32 color = new((byte)(45 + regionNumber * 37 % 190), (byte)(70 + regionNumber * 61 % 170), (byte)(85 + regionNumber * 29 % 160), 220);
        int insetX = Math.Min(3, Math.Max(0, region.Bounds.Width / 4));
        int insetY = Math.Min(3, Math.Max(0, region.Bounds.Height / 4));
        for (int y = region.Bounds.Y + insetY; y < region.Bounds.Bottom - insetY; y++)
        for (int x = region.Bounds.X + insetX; x < region.Bounds.Right - insetX; x++)
            if ((uint)x < (uint)source.Width && (uint)y < (uint)source.Height && ((x + y + regionNumber) % 5 != 0)) source.SetPixel(x, y, color);
        regionNumber++;
    }
    return source;
}
