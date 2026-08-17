using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

try
{
    SpriteDocument document = SpriteDocument.CreateEmpty("Hero", 48, 48);
    if (SpriteDocumentValidator.Validate(document).Any(issue => issue.Severity == "error")) throw new InvalidOperationException("Empty document is invalid.");
    if (document.PixelContents.Count != 1 || document.Layers[0].Cels.Count != 1) throw new InvalidOperationException("Static document did not create its initial cel and pixel content.");

    PixelBuffer buffer = new(16, 16);
    PixelDrawing.DrawLine(buffer, 0, 0, 15, 15, new Rgba32(1, 2, 3, 255));
    if (buffer.GetPixel(8, 8) != new Rgba32(1, 2, 3, 255)) throw new InvalidOperationException("Line rasterization failed.");
    PixelDrawing.DrawRectangle(buffer, 2, 2, 10, 10, new Rgba32(20, 30, 40, 255));
    PixelDrawing.DrawEllipse(buffer, 3, 3, 12, 12, new Rgba32(90, 80, 70, 255));
    PixelDrawing.FloodFill(buffer, 15, 0, new Rgba32(5, 6, 7, 255));
    if (buffer.GetPixel(15, 0) != new Rgba32(5, 6, 7, 255)) throw new InvalidOperationException("Flood fill failed.");

    PixelEditHistory history = new();
    PixelBuffer before = buffer.Clone();
    buffer.SetPixel(4, 4, new Rgba32(200, 100, 50, 255));
    history.Commit("Paint", buffer, before);
    history.Undo(buffer);
    if (buffer.GetPixel(4, 4) != before.GetPixel(4, 4)) throw new InvalidOperationException("Undo failed.");
    history.Redo(buffer);
    if (buffer.GetPixel(4, 4) != new Rgba32(200, 100, 50, 255)) throw new InvalidOperationException("Redo failed.");

    SpriteAnimationTag tag = new() { FromFrame = 0, ToFrame = 2, Direction = AnimationPlaybackDirection.PingPong };
    if (!AnimationSequenceBuilder.Build(tag).SequenceEqual([0, 1, 2, 1])) throw new InvalidOperationException("Ping-pong sequence failed.");
    tag.Direction = AnimationPlaybackDirection.PingPongReverse;
    if (!AnimationSequenceBuilder.Build(tag).SequenceEqual([2, 1, 0, 1])) throw new InvalidOperationException("Reverse ping-pong sequence failed.");

    SpriteDocument playbackDocument = SpriteDocument.CreateEmpty("Playback", 8, 8);
    playbackDocument.Frames.Add(new SpriteFrame { Number = 1, DurationMilliseconds = 200 });
    playbackDocument.Frames.Add(new SpriteFrame { Number = 2, DurationMilliseconds = 300 });
    SpriteAnimationTag playbackTag = new() { FromFrame = 0, ToFrame = 2, Direction = AnimationPlaybackDirection.PingPong };
    AnimationPlaybackController playback = new(); playback.Configure(playbackDocument, playbackTag); playback.Play();
    if (playback.CurrentFrameIndex != 0 || !playback.Advance(playbackDocument, 100) || playback.CurrentFrameIndex != 1)
        throw new InvalidOperationException("Duration-aware playback did not advance to frame 1.");
    playback.SpeedMultiplier = 2d;
    if (!playback.Advance(playbackDocument, 100) || playback.CurrentFrameIndex != 2)
        throw new InvalidOperationException("Playback speed multiplier failed.");
    playback.Loop = false; playback.Advance(playbackDocument, 300); playback.Advance(playbackDocument, 200);
    if (playback.IsPlaying) throw new InvalidOperationException("Non-looping playback did not stop at the sequence end.");

    string auditRoot = Path.Combine(Path.GetTempPath(), "PlayromaSpriteAudit-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(auditRoot);
    try
    {
        File.WriteAllText(Path.Combine(auditRoot, "aseprite.exe"), "synthetic forbidden marker");
        if (new CleanRoomAudit().Audit(auditRoot).Success) throw new InvalidOperationException("Clean-room audit did not reject a forbidden binary.");
    }
    finally { Directory.Delete(auditRoot, recursive: true); }

    Console.WriteLine("PASS Playroma Sprite Core Phase 4 document, history, and playback contracts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}
