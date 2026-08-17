using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }

Rgba32 sample = new(40, 120, 220, 180);
(double hue, double saturation, double lightness) = ColorMath.ToHsl(sample);
Rgba32 hslRoundTrip = ColorMath.FromHsl(hue, saturation, lightness, sample.A);
Check(Math.Abs(hslRoundTrip.R - sample.R) <= 1 && Math.Abs(hslRoundTrip.G - sample.G) <= 1 && Math.Abs(hslRoundTrip.B - sample.B) <= 1, "HSL round-trip drifted.");

PixelBuffer source = new(32, 16);
for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++)
    source.SetPixel(x, y, x == 0 && y == 0 ? Rgba32.Transparent : new((byte)(x * 8), (byte)(y * 16), (byte)((x + y) * 5), 255));

PaletteQuantizationResult quantized = PaletteQuantizer.Quantize(source, new PaletteQuantizationOptions(16, Dither: PaletteDitherMode.FloydSteinberg));
Check(quantized.Palette.Count <= 16, "Quantizer exceeded the color budget.");
Check(quantized.Indices.Length == source.Width * source.Height, "Indexed payload has the wrong length.");
Check(quantized.TransparentIndex == 0 && quantized.Palette[0].A == 0, "Transparent palette entry was not reserved.");
PaletteQuantizationResult deterministic = PaletteQuantizer.Quantize(source, new PaletteQuantizationOptions(16, Dither: PaletteDitherMode.FloydSteinberg));
Check(quantized.Remapped.ComputeSha256() == deterministic.Remapped.ComputeSha256(), "Quantization is not deterministic.");

SelectionMask selection = SelectionMask.Rectangle(source.Width, source.Height, new(0, 0, 8, 8));
PixelBuffer inverted = source.Clone(); Rgba32 untouched = inverted.GetPixel(20, 10); AdvancedPixelEffects.Invert(inverted, false, selection);
Check(inverted.GetPixel(20, 10) == untouched, "Selection-aware effect modified an unselected pixel.");
Check(inverted.GetPixel(4, 4) != source.GetPixel(4, 4), "Selection-aware effect did not modify selected pixels.");

SelectionMask feathered = new(source.Width, source.Height); feathered.Set(1, 1, 128);
PixelBuffer featherOutline = new(source.Width, source.Height); featherOutline.SetPixel(2, 1, Rgba32.White);
AdvancedPixelEffects.Outline(featherOutline, new Rgba32(255, 0, 0), 1, selection: feathered);
Check(featherOutline.GetPixel(1, 1).A is > 0 and < 255, "Feathered outline coverage was not blended.");
PixelBuffer selectedDither = source.Clone(); Rgba32 ditherOutside = selectedDither.GetPixel(20, 10);
AdvancedPixelEffects.OrderedDither(selectedDither, Rgba32.Black, Rgba32.White, selection: selection);
Check(selectedDither.GetPixel(20, 10) == ditherOutside, "Selection-aware dither modified an unselected pixel.");

PixelBuffer blurred = source.Clone(); AdvancedPixelEffects.GaussianBlur(blurred, 2); Check(blurred.ComputeSha256() != source.ComputeSha256(), "Blur had no effect.");
PixelBuffer pixelated = source.Clone(); AdvancedPixelEffects.Pixelate(pixelated, 4); Check(pixelated.ComputeSha256() != source.ComputeSha256(), "Pixelate had no effect.");

string tempRoot = Path.Combine(Path.GetTempPath(), "playroma-color-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tempRoot);
try
{
    SpritePalette palette = PaletteDocumentOperations.FromColors("Test Palette", quantized.Palette);
    foreach (string extension in new[] { ".gpl", ".pal", ".hex", ".json" })
    {
        string path = Path.Combine(tempRoot, "palette" + extension); PaletteFileCodec.Save(palette, path); SpritePalette loaded = PaletteFileCodec.Load(path);
        Check(loaded.Entries.Count == palette.Entries.Count, $"{extension} palette round-trip changed color count.");
    }

    string indexedPng = Path.Combine(tempRoot, "indexed.png");
    PngCodec.Save(source, indexedPng, new(PngExportColorMode.Indexed, quantized.Palette, PaletteDitherMode.None, quantized.TransparentIndex));
    PngColorMetadata indexedMetadata = PngColorMetadataReader.Read(indexedPng);
    PixelBuffer indexedLoaded = PngCodec.Load(indexedPng);
    Check(indexedMetadata.ColorMode == PngExportColorMode.Indexed, "Indexed PNG metadata was not detected.");
    Check(indexedMetadata.Palette.Count == quantized.Palette.Count, "Indexed PNG palette count changed.");
    Check(indexedLoaded.Width == source.Width && indexedLoaded.Height == source.Height, "Indexed PNG dimensions changed.");


    IReadOnlyList<Rgba32> opaquePalette = [new Rgba32(0, 0, 0), new Rgba32(255, 255, 255)];
    string transparencyPng = Path.Combine(tempRoot, "indexed-transparency.png");
    PngCodec.Save(source, transparencyPng, new(PngExportColorMode.Indexed, opaquePalette));
    PngColorMetadata transparencyMetadata = PngColorMetadataReader.Read(transparencyPng);
    Check(transparencyMetadata.Palette.Count == 3 && transparencyMetadata.Palette[0].A == 0, "Indexed PNG export did not insert a required transparent entry.");

    SpritePackageData transparencyPackage = SpritePackageData.CreateEmpty("Transparency Repair", 2, 1);
    transparencyPackage.ActiveBuffer().SetPixel(0, 0, Rgba32.Transparent); transparencyPackage.ActiveBuffer().SetPixel(1, 0, Rgba32.White);
    SpritePalette noAlphaPalette = PaletteDocumentOperations.FromColors("Opaque", opaquePalette); transparencyPackage.Document.Palettes.Add(noAlphaPalette);
    PaletteDocumentOperations.ApplyPalette(transparencyPackage, noAlphaPalette, PaletteDitherMode.None);
    Check(noAlphaPalette.Entries[0].Color.A == 0 && transparencyPackage.Document.TransparentPaletteIndex == 0, "Palette application did not repair missing transparency.");

    string grayPng = Path.Combine(tempRoot, "gray.png"); PngCodec.Save(source, grayPng, new(PngExportColorMode.Grayscale));
    PngColorMetadata grayMetadata = PngColorMetadataReader.Read(grayPng); PixelBuffer grayLoaded = PngCodec.Load(grayPng);
    Check(grayMetadata.ColorMode == PngExportColorMode.Grayscale, "Grayscale PNG metadata was not detected.");
    Rgba32 grayPixel = grayLoaded.GetPixel(10, 5); Check(grayPixel.R == grayPixel.G && grayPixel.G == grayPixel.B, "Grayscale PNG contains chromatic pixels.");

    SpritePackageData package = SpritePackageData.CreateEmpty("Indexed Package", 8, 8); package.ActiveBuffer().CopyFrom(new PixelBuffer(8, 8));
    SpritePalette packagePalette = PaletteDocumentOperations.GeneratePalette(package, "Package Palette", 8);
    PaletteDocumentOperations.ApplyPalette(package, packagePalette, PaletteDitherMode.None);
    Check(package.Document.ColorMode == SpriteColorMode.Indexed, "Document did not enter indexed mode.");
    SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    string packagePath = Path.Combine(tempRoot, "indexed.playromasprite"); await new SpritePackageSerializer().SaveAsync(package, packagePath);
    SpritePackageData reopened = await new SpritePackageSerializer().LoadPackageAsync(packagePath);
    Check(reopened.Document.ColorMode == SpriteColorMode.Indexed && reopened.Document.Palettes.Count == 1, "Indexed package did not round-trip.");

    SpriteDocument invalidColorDocument = SpriteDocumentJsonSerializer.Deserialize(SpriteDocumentJsonSerializer.Serialize(reopened.Document));
    invalidColorDocument.ActivePaletteId = null;
    Check(SpriteDocumentValidator.Validate(invalidColorDocument).Any(issue => issue.Code == "PSPR-DOC-045"), "Indexed validation did not require an active palette.");
    invalidColorDocument.ColorMode = SpriteColorMode.Rgba; invalidColorDocument.ColorProfileMetadata.Gamma = 0d;
    Check(SpriteDocumentValidator.Validate(invalidColorDocument).Any(issue => issue.Code == "PSPR-DOC-044"), "Color-profile validation accepted an invalid gamma.");

    SpriteFrame second = new() { Number = 1 }; package.Document.Frames.Add(second);
    SpriteLayer layer = package.Document.Layers[0]; SpriteCel firstCel = layer.Cels[0]; layer.Cels.Add(new SpriteCel { FrameId = second.Id, PixelContentId = firstCel.PixelContentId });
    int applications = 0; int affected = BatchPixelEffects.Apply(package, new(BatchEffectScope.EntireDocument, layer.Id, package.Document.Frames[0].Id), buffer => { applications++; AdvancedPixelEffects.Invert(buffer); });
    Check(affected == 1 && applications == 1, "Linked cels were processed more than once by batch effects.");


    SpritePackageData profilePackage = SpritePackageData.CreateEmpty("Profile Conversion", 2, 1);
    profilePackage.ActiveBuffer().SetPixel(0, 0, new Rgba32(128, 96, 64, 255));
    SpritePalette profilePalette = PaletteDocumentOperations.FromColors("Profile Palette", [new Rgba32(128, 96, 64, 255), Rgba32.Transparent]);
    profilePackage.Document.Palettes.Add(profilePalette); profilePackage.Document.ActivePaletteId = profilePalette.Id;
    string beforeProfileHash = profilePackage.ActiveBuffer().ComputeSha256();
    int convertedPayloads = ColorProfileOperations.Convert(profilePackage, ColorProfileOperations.GetRequired("linear-srgb"));
    Check(convertedPayloads == 1 && profilePackage.ActiveBuffer().ComputeSha256() != beforeProfileHash, "Color-profile conversion did not transform pixels.");
    Check(profilePackage.Document.ColorProfileMetadata.Kind == "linear-srgb" && Math.Abs(profilePackage.Document.ColorProfileMetadata.Gamma - 1d) < .000001d, "Color-profile metadata was not updated.");
    Check(profilePalette.Entries[0].Color.R != 128, "Color-profile conversion did not transform palette colors.");

    SpritePalette sortable = PaletteDocumentOperations.FromColors("Sortable", [new Rgba32(255, 0, 0), new Rgba32(0, 0, 255), new Rgba32(0, 255, 0)]);
    PaletteSorting.Sort(sortable, PaletteSortMode.Hue);
    Check(sortable.Entries.Select(entry => entry.Index).SequenceEqual([0, 1, 2]), "Palette sorting did not reindex colors.");
    Check(sortable.Entries[0].Color.R == 255 && sortable.Entries[1].Color.G == 255 && sortable.Entries[2].Color.B == 255, "Hue palette sorting produced the wrong order.");
}
finally { Directory.Delete(tempRoot, true); }

Console.WriteLine($"Playroma Sprite Color tests passed: {checks} checks.");

static class PackageTestExtensions
{
    public static PixelBuffer ActiveBuffer(this SpritePackageData package) => package.GetRequired(package.Document.PixelContents[0].Id);
}
