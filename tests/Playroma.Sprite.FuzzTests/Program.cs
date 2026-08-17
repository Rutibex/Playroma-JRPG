using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

try
{
    Random random = new(20260805);
    for (int index = 0; index < 250; index++)
    {
        SpriteDocument source = SpriteDocument.CreateEmpty("Fuzz " + index, random.Next(1, 2049), random.Next(1, 2049));
        int extraFrames = random.Next(0, 25);
        for (int frame = 0; frame < extraFrames; frame++) source.Frames.Add(new SpriteFrame { Number = source.Frames.Count, DurationMilliseconds = random.Next(1, 2001) });
        if (source.Frames.Count > 1) source.Tags.Add(new SpriteAnimationTag { Name = "All", FromFrame = 0, ToFrame = source.Frames.Count - 1 });
        SpriteDocument copy = SpriteDocumentJsonSerializer.Deserialize(SpriteDocumentJsonSerializer.Serialize(source));
        if (copy.Canvas.Width != source.Canvas.Width || copy.Frames.Count != source.Frames.Count) throw new InvalidOperationException("Document fuzz round-trip mismatch at " + index);
    }

    for (int index = 0; index < 300; index++)
    {
        int width = random.Next(1, 65), height = random.Next(1, 65);
        PixelBuffer pixels = new(width, height);
        BrushShape shape = (BrushShape)random.Next(0, 5);
        int size = random.Next(1, 12);
        PixelPaintSettings settings = new()
        {
            Brush = BrushDefinition.CreateBuiltIn(shape, size),
            PrimaryColor = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(1, 256)),
            Opacity = (byte)random.Next(1, 256),
            InkMode = (InkMode)random.Next(Enum.GetValues<InkMode>().Length),
            Symmetry = (SymmetryMode)random.Next(4),
            SymmetryAxisX = (width - 1) / 2d,
            SymmetryAxisY = (height - 1) / 2d,
            TiledMode = (TiledMode)random.Next(4),
            PixelPerfect = random.Next(2) == 0
        };
        List<StrokeSample> samples = [];
        for (int sample = 0; sample < random.Next(1, 20); sample++)
            samples.Add(new StrokeSample(random.Next(-width, width * 2), random.Next(-height, height * 2), random.NextDouble(), random.NextDouble() * 1200));
        PixelDrawing.DrawStroke(pixels, samples, settings);
        if (pixels.Bytes.Length != width * height * 4) throw new InvalidOperationException("Pixel fuzz changed buffer length at " + index);
        _ = pixels.ComputeSha256();
    }

    for (int index = 0; index < 300; index++)
    {
        int width = random.Next(2, 65), height = random.Next(2, 65);
        PixelBuffer pixels = new(width, height);
        for (int sample = 0; sample < random.Next(1, width * height); sample++)
            pixels.SetPixel(random.Next(width), random.Next(height), new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        IntRect region = new(random.Next(width), random.Next(height), random.Next(1, width + 1), random.Next(1, height + 1));
        SelectionMask selection = SelectionMask.Rectangle(width, height, region);
        if (random.Next(2) == 0) selection.Combine(SelectionMask.Ellipse(width, height, region), (SelectionCombineMode)random.Next(4));
        if (random.Next(2) == 0) selection.Grow(random.Next(0, 4)); else selection.Shrink(random.Next(0, 4));
        if (!selection.IsEmpty)
        {
            SelectionMask transformed = PixelSelectionTransforms.Move(pixels, selection, random.Next(-width, width), random.Next(-height, height));
            if (!transformed.IsEmpty && random.Next(2) == 0) _ = PixelSelectionTransforms.RotateQuarter(pixels, transformed, (QuarterTurn)random.Next(3));
        }
        if (pixels.Bytes.Length != width * height * 4) throw new InvalidOperationException("Selection fuzz changed buffer length at " + index);
    }

    for (int index = 0; index < 100; index++)
    {
        SpritePackageData package = SpritePackageData.CreateEmpty("Layer Fuzz " + index, random.Next(2, 64), random.Next(2, 64));
        Guid frame = package.Document.Frames[0].Id;
        SpriteLayer group = SpritePackageOperations.AddLayer(package, "Group", SpriteLayerKind.Group, frame);
        for (int layer = 0; layer < random.Next(1, 8); layer++)
        {
            SpriteLayer added = SpritePackageOperations.AddLayer(package, "Layer " + layer, layer % 4 == 0 ? SpriteLayerKind.Reference : SpriteLayerKind.Image, frame, group.Id);
            added.BlendMode = SpriteBlendModes.All[random.Next(SpriteBlendModes.All.Count)]; added.Opacity = (byte)random.Next(256);
        }
        PixelBuffer composite = LayerCompositor.Composite(package.Document, package.PixelBuffers, frame);
        if (composite.Width != package.Document.Canvas.Width || composite.Height != package.Document.Canvas.Height) throw new InvalidOperationException("Layer fuzz composite size mismatch at " + index);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
    }

    for (int index = 0; index < 150; index++)
    {
        SpritePackageData animation = SpritePackageData.CreateEmpty("Animation Fuzz " + index, random.Next(2, 33), random.Next(2, 33));
        int frames = random.Next(2, 25);
        for (int frame = 1; frame < frames; frame++)
            SpriteAnimationOperations.DuplicateFrame(animation, frame - 1, linked: random.Next(3) == 0);
        for (int frame = 0; frame < animation.Document.Frames.Count; frame++)
            animation.Document.Frames[frame].DurationMilliseconds = random.Next(1, 1001);
        if (animation.Document.Frames.Count > 2)
        {
            int from = random.Next(animation.Document.Frames.Count - 1);
            int to = random.Next(from, animation.Document.Frames.Count);
            animation.Document.Tags.Add(new SpriteAnimationTag { Name = "Range", FromFrame = from, ToFrame = to, Direction = (AnimationPlaybackDirection)random.Next(4) });
            _ = AnimationSequenceBuilder.Build(animation.Document.Tags[0]);
        }
        if (random.Next(2) == 0)
        {
            int from = random.Next(animation.Document.Frames.Count);
            int to = random.Next(from, animation.Document.Frames.Count);
            SpriteAnimationOperations.ReverseFrames(animation, from, to);
        }
        SpriteLayer layer = animation.Document.Layers[0];
        if (animation.Document.Frames.Count > 1 && random.Next(2) == 0)
        {
            CelSelection[] cels = layer.Cels.Take(Math.Min(3, layer.Cels.Count)).Select(cel => new CelSelection(layer.Id, cel.FrameId)).ToArray();
            if (cels.Length > 1) SpriteAnimationOperations.LinkCels(animation, cels);
        }
        SpriteDocumentValidator.ThrowIfInvalid(animation.Document);
        IReadOnlyList<PixelBuffer> rendered = AnimationFrameRenderer.RenderFrames(animation);
        if (rendered.Count != animation.Document.Frames.Count || rendered.Any(buffer => buffer.Width != animation.Document.Canvas.Width))
            throw new InvalidOperationException("Animation fuzz render mismatch at " + index);
    }

    Console.WriteLine("PASS Playroma Sprite Phase 4 deterministic document, drawing, layer, selection, and animation fuzz tests");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}
