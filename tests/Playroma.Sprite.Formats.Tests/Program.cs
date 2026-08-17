using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

try
{
    SpritePackageData source = SpritePackageData.CreateEmpty("Package Test", 96, 64);
    PixelBuffer pixels = source.GetRequired(source.Document.PixelContents[0].Id);
    pixels.Clear(Rgba32.Transparent);
    PixelDrawing.DrawLine(pixels, 0, 0, 95, 63, new Rgba32(220, 40, 80, 255), 2);
    source.Document.Palettes.Add(new SpritePalette
    {
        Name = "Test Palette",
        Entries = [new SpritePaletteEntry { Index = 0, Name = "Transparent", Color = new SpriteColorValue(0, 0, 0, 0), Locked = true }]
    });

    string directory = Path.Combine(Path.GetTempPath(), "PlayromaSpritePhase4-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        SpritePackageSerializer serializer = new();
        string packagePath = Path.Combine(directory, "test.playromasprite");
        string pngPath = Path.Combine(directory, "test.png");
        await serializer.SaveAsync(source, packagePath);
        SpritePackageData copy = await serializer.LoadPackageAsync(packagePath);
        PixelBuffer copiedPixels = copy.GetRequired(copy.Document.PixelContents[0].Id);
        if (copiedPixels.ComputeSha256() != pixels.ComputeSha256()) throw new InvalidOperationException("Pixel package round-trip failed.");

        PngCodec.Save(copiedPixels, pngPath);
        PixelBuffer pngCopy = PngCodec.Load(pngPath);
        if (pngCopy.Width != 96 || pngCopy.Height != 64 || pngCopy.ComputeSha256() != pixels.ComputeSha256())
            throw new InvalidOperationException("PNG round-trip failed.");

        string json = SpriteDocumentJsonSerializer.Serialize(source.Document);
        SpriteDocument jsonCopy = SpriteDocumentJsonSerializer.Deserialize(json);
        if (jsonCopy.PixelContents.Count != 1 || jsonCopy.Layers[0].Cels.Count != 1) throw new InvalidOperationException("Document JSON lost pixel references.");

        Guid frameId = copy.Document.Frames[0].Id;
        SpriteLayer group = SpritePackageOperations.AddLayer(copy, "Characters", SpriteLayerKind.Group, frameId);
        SpriteLayer top = SpritePackageOperations.AddLayer(copy, "Highlights", SpriteLayerKind.Image, frameId, group.Id);
        PixelBuffer topPixels = copy.GetRequired(top.Cels[0].PixelContentId);
        topPixels.SetPixel(5, 5, Rgba32.White); top.BlendMode = SpriteBlendModes.Screen; top.Opacity = 192;
        if (!SpriteLayerTree.IsDescendant(copy.Document, top.Id, group.Id)) throw new InvalidOperationException("Layer grouping failed.");
        SpriteLayer duplicate = SpritePackageOperations.DuplicateLayer(copy, group.Id);
        if (duplicate.Kind != SpriteLayerKind.Group || copy.Document.Layers.Count < 5) throw new InvalidOperationException("Layer subtree duplication failed.");
        PixelBuffer composite = LayerCompositor.Composite(copy.Document, copy.PixelBuffers, frameId);
        if (composite.Width != copy.Document.Canvas.Width || composite.GetPixel(5, 5).A == 0) throw new InvalidOperationException("Layer composite failed.");

        SpritePackageHistory history = new();
        SpritePackageData beforeLayerChange = copy.DeepClone(); top.Visible = false;
        history.CommitPackage("Hide Layer", beforeLayerChange, copy);
        SpritePackageData? undone = history.Undo(copy);
        if (undone is null || !SpriteLayerTree.GetRequired(undone.Document, top.Id).Visible) throw new InvalidOperationException("Package history undo failed.");
        SpritePackageData? redone = history.Redo(undone);
        if (redone is null || SpriteLayerTree.GetRequired(redone.Document, top.Id).Visible) throw new InvalidOperationException("Package history redo failed.");

        SpritePackageHistory pixelHistory = new();
        Guid activeContent = redone.Document.Layers.First(layer => layer.Kind != SpriteLayerKind.Group).Cels[0].PixelContentId;
        PixelBuffer activePixels = redone.GetRequired(activeContent); PixelBuffer beforePixels = activePixels.Clone();
        SelectionMask beforeSelection = SelectionMask.Rectangle(activePixels.Width, activePixels.Height, new IntRect(1, 1, 2, 2));
        SelectionMask afterSelection = beforeSelection.Translate(3, 2); activePixels.SetPixel(4, 3, Rgba32.White);
        pixelHistory.CommitPixel("Move Selection", redone, activeContent, beforePixels, beforeSelection, afterSelection);
        _ = pixelHistory.Undo(redone);
        if (pixelHistory.LastRestoredSelection?.Contains(1, 1) != true) throw new InvalidOperationException("Selection-aware history undo failed.");
        _ = pixelHistory.Redo(redone);
        if (pixelHistory.LastRestoredSelection?.Contains(4, 3) != true) throw new InvalidOperationException("Selection-aware history redo failed.");

        SpritePackageOperations.ResizeSprite(redone, 48, 32, PixelResamplingMode.NearestNeighbor);
        SpritePackageOperations.ResizeCanvas(redone, new IntRect(2, 2, 20, 16));
        if (redone.Document.Canvas.Width != 20 || redone.PixelBuffers.Values.Any(buffer => buffer.Width != 20 || buffer.Height != 16))
            throw new InvalidOperationException("Layer-aware canvas resize failed.");
        SpriteLayer flattenedStatic = SpritePackageOperations.Flatten(redone, redone.Document.Frames[0].Id);
        if (redone.Document.Layers.Count != 1 || flattenedStatic.Name != "Flattened") throw new InvalidOperationException("Flatten image failed.");

        SpritePackageData animation = SpritePackageData.CreateEmpty("Animation", 12, 10);
        PixelBuffer first = animation.GetRequired(animation.Document.PixelContents[0].Id);
        first.SetPixel(1, 1, new Rgba32(255, 0, 0));
        SpriteFrame copied = SpriteAnimationOperations.DuplicateFrame(animation, 0, linked: false);
        SpriteLayer animationLayer = animation.Document.Layers[0];
        SpriteCel copiedCel = animationLayer.Cels.Single(cel => cel.FrameId == copied.Id);
        animation.GetRequired(copiedCel.PixelContentId).SetPixel(2, 2, new Rgba32(0, 255, 0));
        SpriteFrame linked = SpriteAnimationOperations.DuplicateFrame(animation, 1, linked: true);
        SpriteCel linkedCel = animationLayer.Cels.Single(cel => cel.FrameId == linked.Id);
        if (linkedCel.PixelContentId != copiedCel.PixelContentId) throw new InvalidOperationException("Linked frame did not reuse pixel content.");
        SpriteAnimationOperations.UnlinkCels(animation, [new CelSelection(animationLayer.Id, linked.Id)]);
        if (linkedCel.PixelContentId == copiedCel.PixelContentId) throw new InvalidOperationException("Unlink did not create independent pixel content.");
        SpriteAnimationOperations.SetFrameDurations(animation.Document, [0, 1, 2], 140);
        animation.Document.Tags.Add(new SpriteAnimationTag { Name = "Loop", FromFrame = 0, ToFrame = 2, Direction = AnimationPlaybackDirection.PingPong });
        SpriteDocumentValidator.ThrowIfInvalid(animation.Document);

        AnimationFrameImage[] animationFrames = animation.Document.Frames
            .Select((frame, index) => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(animation, index), frame.DurationMilliseconds)).ToArray();
        string sequenceDirectory = Path.Combine(directory, "sequence");
        IReadOnlyList<string> sequencePaths = AnimationImageSequence.Save(animationFrames, sequenceDirectory, "hero");
        IReadOnlyList<AnimationFrameImage> loadedSequence = AnimationImageSequence.Load(sequencePaths.Reverse(), 140);
        if (loadedSequence.Count != 3 || loadedSequence[0].Pixels.GetPixel(1, 1).R != 255) throw new InvalidOperationException("PNG animation sequence round-trip failed.");

        string gifPath = Path.Combine(directory, "animation.gif");
        GifCodec.Save(animationFrames, gifPath);
        GifAnimation gif = GifCodec.Load(gifPath);
        if (gif.Width != 12 || gif.Height != 10 || gif.Frames.Count != 3) throw new InvalidOperationException("GIF animation dimensions or frame count changed.");
        if (gif.Frames.Any(frame => frame.DurationMilliseconds is < 130 or > 150)) throw new InvalidOperationException("GIF frame durations changed unexpectedly.");

        SpritePackageOperations.Flatten(animation, animation.Document.Frames[0].Id);
        if (animation.Document.Layers.Single().Cels.Count != 3) throw new InvalidOperationException("Flattening discarded animation frames.");
        string animationPackagePath = Path.Combine(directory, "animation.playromasprite");
        await serializer.SaveAsync(animation, animationPackagePath);
        SpritePackageData animationCopy = await serializer.LoadPackageAsync(animationPackagePath);
        if (animationCopy.Document.Frames.Count != 3 || animationCopy.Document.Tags.Count != 1 || animationCopy.Document.Layers[0].Cels.Count != 3)
            throw new InvalidOperationException("Animation package round-trip lost timeline data.");
    }
    finally { Directory.Delete(directory, recursive: true); }

    Console.WriteLine("PASS Playroma Sprite Formats Phase 4 package, frame/cel, PNG sequence, and GIF contracts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}
