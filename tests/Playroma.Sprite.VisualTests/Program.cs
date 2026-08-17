using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

try
{
    RpgMakerMvImageProfile profile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired("mv-character-standard");
    IReadOnlyList<Playroma.Sprite.Core.IntRect> cells = MvGuideLayout.CreateCellRects(profile, 576, 384);
    Assert(cells.Count == 96 && cells[0].Width == 48 && cells[^1].X == 528 && cells[^1].Y == 336, "Character guide geometry changed.");

    BrushDefinition circle = BrushDefinition.CreateBuiltIn(BrushShape.Circle, 5);
    Assert(circle.Mask.ToArray().Count(value => value) is > 12 and < 25, "Circle brush mask is implausible.");
    BrushDefinition custom = new("L", 2, 3, new[] { true, false, true, false, true, true }, 0, 1);
    BrushDefinition rotated = custom.RotateClockwise();
    Assert(rotated.Width == 3 && rotated.Height == 2, "Custom brush rotation changed dimensions incorrectly.");

    PixelBuffer symmetry = new(9, 7);
    PixelPaintSettings symmetric = new()
    {
        Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1),
        PrimaryColor = new Rgba32(255, 0, 0),
        Symmetry = SymmetryMode.Horizontal | SymmetryMode.Vertical,
        SymmetryAxisX = 4,
        SymmetryAxisY = 3
    };
    PixelDrawing.Stamp(symmetry, 1, 2, symmetric);
    Assert(symmetry.GetPixel(1, 2).A == 255 && symmetry.GetPixel(7, 2).A == 255 && symmetry.GetPixel(1, 4).A == 255 && symmetry.GetPixel(7, 4).A == 255, "Four-way symmetry failed.");

    PixelBuffer tiled = new(4, 4);
    PixelPaintSettings wrapped = symmetric with { Symmetry = SymmetryMode.None, TiledMode = TiledMode.X | TiledMode.Y };
    PixelDrawing.Stamp(tiled, -1, -1, wrapped);
    Assert(tiled.GetPixel(3, 3).A == 255, "Wrapped stamp did not cross both boundaries.");

    PixelBuffer patterned = new(6, 2);
    PixelBuffer patternPixels = new(2, 1);
    patternPixels.SetPixel(0, 0, Rgba32.Black); patternPixels.SetPixel(1, 0, Rgba32.White);
    PixelPaintSettings patternSettings = new()
    {
        Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1),
        Pattern = new BrushPattern("Stripe", patternPixels),
        PatternAlignment = PatternAlignment.Canvas
    };
    PixelDrawing.DrawLine(patterned, new StrokeSample(0, 0), new StrokeSample(5, 0), patternSettings);
    Assert(patterned.GetPixel(0, 0) == Rgba32.Black && patterned.GetPixel(1, 0) == Rgba32.White && patterned.GetPixel(4, 0) == Rgba32.Black, "Pattern alignment failed.");

    PixelBuffer shapes = new(32, 32);
    PixelPaintSettings paint = new() { Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1), PrimaryColor = new Rgba32(40, 180, 240) };
    PixelDrawing.DrawQuadraticCurve(shapes, new StrokeSample(2, 25), new StrokeSample(16, 1), new StrokeSample(29, 25), paint);
    PixelDrawing.DrawRoundedRectangle(shapes, 3, 3, 12, 12, 3, paint, false);
    PixelDrawing.DrawEllipse(shapes, 17, 4, 29, 15, paint, true);
    PixelDrawing.DrawPolygon(shapes, [new StrokeSample(4, 18), new StrokeSample(14, 17), new StrokeSample(10, 28)], paint, true);
    Assert(shapes.GetPixel(10, 22).A > 0 && shapes.GetPixel(23, 9).A > 0, "Filled polygon or ellipse failed.");

    PixelBuffer gradient = new(8, 2);
    PixelDrawing.DrawLinearGradient(gradient, 0, 0, 7, 0, Rgba32.Black, Rgba32.White);
    Assert(gradient.GetPixel(0, 0).R == 0 && gradient.GetPixel(7, 0).R == 255 && gradient.GetPixel(4, 0).R is > 120 and < 170, "Linear gradient failed.");
    PixelDrawing.DrawLinearGradient(gradient, 0, 0, 7, 0, Rgba32.Black, Rgba32.White, true);
    Assert(gradient.Bytes.Span.ToArray().Chunk(4).Select(pixel => pixel[0]).Distinct().Count() == 2, "Dithered gradient produced more than two colors.");

    PixelBuffer ink = new(2, 2); ink.Clear(new Rgba32(20, 40, 60, 128));
    PixelPaintSettings lockAlpha = paint with { PrimaryColor = new Rgba32(250, 10, 20), InkMode = InkMode.LockAlpha };
    PixelDrawing.Stamp(ink, 0, 0, lockAlpha);
    Assert(ink.GetPixel(0, 0).A == 128 && ink.GetPixel(0, 0).R > 20, "Lock-alpha ink changed alpha or failed to paint.");
    PixelPaintSettings erase = paint with { InkMode = InkMode.Erase, Opacity = 255 };
    PixelDrawing.Stamp(ink, 1, 1, erase);
    Assert(ink.GetPixel(1, 1).A == 0, "Erase ink failed.");

    PixelBuffer outline = new(7, 7); outline.SetPixel(3, 3, Rgba32.White);
    int outlined = PixelEffects.Outline(outline, Rgba32.Black, 1, true, false);
    Assert(outlined == 8, "One-pixel diagonal outline should paint eight neighbours.");

    StrokeDynamicsSettings dynamics = new() { PressureAffectsSize = true, PressureAffectsOpacity = true, VelocityAffectsSize = true, MinimumSizeFactor = .25, MinimumOpacityFactor = .2 };
    StrokeDynamicsResult low = dynamics.Evaluate(.25, 800);
    Assert(low.SizeFactor < .3 && low.OpacityFactor < .5, "Stroke dynamics did not reduce low-pressure fast input.");

    SelectionMask rectangle = SelectionMask.Rectangle(16, 16, new IntRect(2, 3, 6, 5));
    SelectionMask ellipseSelection = SelectionMask.Ellipse(16, 16, new IntRect(4, 4, 8, 8));
    SelectionMask combined = rectangle.Clone(); combined.Combine(ellipseSelection, SelectionCombineMode.Add);
    Assert(rectangle.CountSelected() == 30 && combined.CountSelected() > rectangle.CountSelected(), "Selection rectangle/add failed.");
    combined.Combine(ellipseSelection, SelectionCombineMode.Intersect);
    Assert(combined.CountSelected() > 0 && combined.CountSelected() <= ellipseSelection.CountSelected(), "Selection intersection failed.");

    PixelBuffer wandSource = new(8, 4); wandSource.Clear(Rgba32.Black);
    for (int y = 0; y < 4; y++) for (int x = 4; x < 8; x++) wandSource.SetPixel(x, y, Rgba32.White);
    SelectionMask wand = SelectionMask.MagicWand(wandSource, 1, 1, 0, true);
    Assert(wand.CountSelected() == 16 && !wand.Contains(7, 1), "Contiguous magic wand crossed a color boundary.");

    PixelBuffer clipped = new(8, 8);
    SelectionMask clipMask = SelectionMask.Rectangle(8, 8, new IntRect(2, 2, 3, 3));
    PixelDrawing.DrawRectangle(clipped, 0, 0, 7, 7, paint with { Selection = clipMask }, true);
    Assert(clipped.GetPixel(3, 3).A > 0 && clipped.GetPixel(0, 0).A == 0, "Selection-clipped painting failed.");

    PixelBuffer transform = new(12, 12); transform.SetPixel(3, 4, new Rgba32(255, 10, 20)); transform.SetPixel(4, 4, new Rgba32(10, 255, 20));
    SelectionMask transformSelection = SelectionMask.Rectangle(12, 12, new IntRect(3, 4, 2, 1));
    SelectionMask moved = PixelSelectionTransforms.Move(transform, transformSelection, 3, 2);
    Assert(moved.Contains(6, 6) && transform.GetPixel(3, 4).A == 0 && transform.GetPixel(6, 6).A == 255, "Selection move failed.");
    SelectionMask rotatedSelection = PixelSelectionTransforms.RotateQuarter(transform, moved, QuarterTurn.Clockwise90);
    Assert(rotatedSelection.CountSelected() == 2, "Quarter-turn rotation lost selected pixels.");
    SelectionMask scaledSelection = PixelSelectionTransforms.Scale(transform, rotatedSelection, new IntRect(5, 5, 4, 2));
    Assert(scaledSelection.GetBounds()?.Width == 4 && scaledSelection.GetBounds()?.Height == 2, "Nearest-neighbor selection scaling failed.");

    Rgba32 multiplied = LayerCompositor.Blend(new Rgba32(200, 100, 50), new Rgba32(128, 255, 128), SpriteBlendModes.Multiply);
    Rgba32 screened = LayerCompositor.Blend(new Rgba32(40, 80, 120), new Rgba32(128, 128, 128), SpriteBlendModes.Screen);
    Assert(multiplied.R is >= 99 and <= 101 && multiplied.G == 100 && screened.R > 40, "Layer blend mode contract failed.");

    PixelBuffer deterministicA = new(32, 32);
    PixelBuffer deterministicB = new(32, 32);
    List<StrokeSample> samples = [new(2, 2), new(10, 4), new(12, 18), new(25, 24)];
    PixelPaintSettings perfect = paint with { PixelPerfect = true, Brush = BrushDefinition.CreateBuiltIn(BrushShape.Circle, 3) };
    PixelDrawing.DrawStroke(deterministicA, samples, perfect);
    PixelDrawing.DrawStroke(deterministicB, samples, perfect);
    Assert(deterministicA.ComputeSha256() == deterministicB.ComputeSha256(), "Pixel-perfect stroke is not deterministic.");

    SpritePackageData onionPackage = SpritePackageData.CreateEmpty("Onion", 8, 8);
    SpriteLayer onionLayer = onionPackage.Document.Layers[0];
    onionPackage.ActiveBufferForTest().SetPixel(1, 1, new Rgba32(255, 0, 0));
    SpriteFrame onionSecond = SpriteAnimationOperations.DuplicateFrame(onionPackage, 0, linked: false);
    SpriteCel onionSecondCel = onionLayer.Cels.Single(cel => cel.FrameId == onionSecond.Id);
    PixelBuffer onionSecondPixels = onionPackage.GetRequired(onionSecondCel.PixelContentId);
    onionSecondPixels.Clear(Rgba32.Transparent); onionSecondPixels.SetPixel(2, 1, new Rgba32(0, 255, 0));
    SpriteFrame onionThird = SpriteAnimationOperations.DuplicateFrame(onionPackage, 1, linked: false);
    SpriteCel onionThirdCel = onionLayer.Cels.Single(cel => cel.FrameId == onionThird.Id);
    PixelBuffer onionThirdPixels = onionPackage.GetRequired(onionThirdCel.PixelContentId);
    onionThirdPixels.Clear(Rgba32.Transparent); onionThirdPixels.SetPixel(3, 1, new Rgba32(0, 0, 255));
    OnionSkinSettings onionSettings = new() { PreviousFrames = 1, NextFrames = 1, BaseOpacity = 128 };
    PixelBuffer onion = OnionSkinRenderer.Render(onionPackage, 1, onionSettings);
    Assert(onion.GetPixel(1, 1).A > 0 && onion.GetPixel(3, 1).A > 0, "Onion skin did not include adjacent frames.");
    PixelBuffer onionComposite = OnionSkinRenderer.CompositeWithCurrent(onionPackage, 1, onionSettings);
    Assert(onionComposite.GetPixel(2, 1).G > 200, "Onion skin obscured the current frame.");

    Console.WriteLine("PASS Playroma Sprite Phase 4 drawing, layers, transforms, and onion-skin visual contracts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class VisualPackageExtensions
{
    public static PixelBuffer ActiveBufferForTest(this SpritePackageData package) =>
        package.GetRequired(package.Document.Layers[0].Cels[0].PixelContentId);
}
