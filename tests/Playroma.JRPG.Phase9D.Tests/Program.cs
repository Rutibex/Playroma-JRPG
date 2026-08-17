using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.AnimationEditing;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

List<(string Name, Action Test)> tests =
[
    ("Animation template contains one 16-lane frame", AnimationTemplate),
    ("Reader accepts numeric mirror values and nullable sounds", ReaderCompatibility),
    ("Reader preserves timing extension fields", TimingExtensions),
    ("Cell serializer preserves extra values and mirror representation", CellSerialization),
    ("Runtime sheet mapping always uses five columns", SheetMapping),
    ("Sheet two patterns use the 100 range", SheetTwoMapping),
    ("Position projection supports all four MV anchors", PositionProjection),
    ("Tweening interpolates selected lanes", Tweening),
    ("Tweening uses shortest rotation path", RotationTween),
    ("Batch editing changes only addressed cells", BatchEditing),
    ("Frames beyond sixteen lanes survive serialization", ExtensionCells),
    ("Timing serializer preserves plugin fields", TimingSerialization),
    ("Clipboard dependencies report missing assets", ClipboardDependencies),
    ("Playback clock matches four ticks per frame", PlaybackClock)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} Phase 9D tests passed.");
return failures == 0 ? 0 : 1;

static void AnimationTemplate()
{
    using JsonDocument json = JsonDocument.Parse(DatabaseRecordTemplateFactory.Create(MvDocumentKind.Animations, 1));
    JsonElement frames = json.RootElement.GetProperty("frames");
    Equal(1, frames.GetArrayLength());
    Equal(16, frames[0].GetArrayLength());
    Equal(-1, frames[0][0][0].GetInt32());
}

static void ReaderCompatibility()
{
    string json = "[null,{\"id\":1,\"name\":\"Fire\",\"animation1Name\":\"Fire1\",\"animation1Hue\":10,\"animation2Name\":\"\",\"animation2Hue\":0,\"position\":1,\"frames\":[[[3,1,2,100,0,1,255,1]]],\"timings\":[{\"frame\":0,\"flashScope\":1,\"flashColor\":[1,2,3,4],\"flashDuration\":5,\"se\":null}]}]";
    MvAnimationSnapshot animation = MvAnimationReader.Read(Create(json), 1);
    True(animation.Frames[0].Cells[0].Mirror);
    True(animation.Timings[0].SoundEffect is null);
}

static void TimingExtensions()
{
    string json = "[null,{\"id\":1,\"name\":\"Test\",\"animation1Name\":\"\",\"animation1Hue\":0,\"animation2Name\":\"\",\"animation2Hue\":0,\"position\":1,\"frames\":[[]],\"timings\":[{\"frame\":0,\"flashScope\":0,\"flashColor\":[0,0,0,0],\"flashDuration\":5,\"se\":null,\"conditions\":{\"switch\":4}}]}]";
    MvAnimationSnapshot animation = MvAnimationReader.Read(Create(json), 1);
    True(animation.Timings[0].ExtensionFields.ContainsKey("conditions"));
}

static void CellSerialization()
{
    MvAnimationCellSnapshot cell = MvAnimationReader.ReadCell("[1,2,3,100,0,0,255,1,\"keep\"]") with { Mirror = true, X = 9 };
    using JsonDocument json = JsonDocument.Parse(MvAnimationSerializer.SerializeCell(cell));
    Equal(9, json.RootElement[1].GetInt32());
    Equal(1, json.RootElement[5].GetInt32());
    Equal("keep", json.RootElement[8].GetString());
}

static void SheetMapping()
{
    MvAnimationPatternCell pattern = MvAnimationSheetMapper.Map(7);
    Equal(384, pattern.SourceX);
    Equal(192, pattern.SourceY);
}

static void SheetTwoMapping()
{
    MvAnimationPatternCell pattern = MvAnimationSheetMapper.Map(107);
    True(pattern.UsesSecondSheet);
    Equal(7, pattern.LocalPattern);
    Equal(384, pattern.SourceX);
}

static void PositionProjection()
{
    AnimationPreviewTarget target = new(100, 200, 80, 120, 816, 624);
    Equal(new AnimationPoint(140, 200), MvAnimationPositionCalculator.Calculate(0, target));
    Equal(new AnimationPoint(140, 260), MvAnimationPositionCalculator.Calculate(1, target));
    Equal(new AnimationPoint(140, 320), MvAnimationPositionCalculator.Calculate(2, target));
    Equal(new AnimationPoint(408, 312), MvAnimationPositionCalculator.Calculate(3, target));
}

static void Tweening()
{
    MvAnimationFrameSnapshot[] frames =
    [
        Frame(0, Cell(0, 0, 0)),
        Frame(1, Cell(0, 999, 999)),
        Frame(2, Cell(0, 100, 50))
    ];
    IReadOnlyList<MvAnimationFrameSnapshot> result = AnimationTweenEngine.Apply(frames, new AnimationTweenRequest(0, 2, new HashSet<int> { 0 }));
    Near(50, result[1].Cells[0].X);
    Near(25, result[1].Cells[0].Y);
}

static void RotationTween()
{
    MvAnimationFrameSnapshot[] frames = [Frame(0, Cell(0, rotation: 350)), Frame(1, Cell(0)), Frame(2, Cell(0, rotation: 10))];
    IReadOnlyList<MvAnimationFrameSnapshot> result = AnimationTweenEngine.Apply(frames, new AnimationTweenRequest(0, 2, new HashSet<int> { 0 }, RotationMode: AnimationRotationMode.ShortestPath));
    Near(360, result[1].Cells[0].Rotation);
}

static void BatchEditing()
{
    MvAnimationFrameSnapshot[] frames = [Frame(0, Cell(1, 10, 20), Cell(2, 30, 40))];
    IReadOnlyList<MvAnimationFrameSnapshot> result = AnimationBatchEditor.Apply(frames, [new AnimationCellAddress(0, 1)], new AnimationBatchEdit(OffsetX: 5));
    Near(10, result[0].Cells[0].X);
    Near(35, result[0].Cells[1].X);
}

static void ExtensionCells()
{
    MvAnimationCellSnapshot[] cells = Enumerable.Range(0, 18).Select(index => Cell(index)).ToArray();
    string raw = MvAnimationSerializer.SerializeFrames([new MvAnimationFrameSnapshot(0, cells, "[]")]);
    using JsonDocument json = JsonDocument.Parse(raw);
    Equal(18, json.RootElement[0].GetArrayLength());
}

static void TimingSerialization()
{
    MvAnimationTimingSnapshot timing = new(0, 2, 1, [1,2,3,4], 5, null, new Dictionary<string,string>{{"conditions","true"}}, "{\"frame\":0,\"flashScope\":0,\"flashColor\":[0,0,0,0],\"flashDuration\":1,\"se\":null,\"conditions\":true}");
    using JsonDocument json = JsonDocument.Parse(MvAnimationSerializer.SerializeTimings([timing]));
    Equal(2, json.RootElement[0].GetProperty("frame").GetInt32());
    True(json.RootElement[0].GetProperty("conditions").GetBoolean());
}

static void ClipboardDependencies()
{
    AnimationClipboardEnvelope envelope = new(AnimationClipboardEnvelope.CurrentFormat, 1, "source", 1, "Fire1", "Fire2", [Frame(0, Cell(250))], [new MvAnimationTimingSnapshot(0,0,0,[0,0,0,0],5,new MvAnimationAudioSnapshot("Boom",90,100,0,"{}"),new Dictionary<string,string>(),"{}")]);
    AnimationDependencyReport report = AnimationClipboardAnalyzer.Analyze(envelope, new HashSet<string>{"Fire1"}, new HashSet<string>());
    True(report.MissingSheets.Contains("Fire2"));
    True(report.MissingSoundEffects.Contains("Boom"));
    True(report.OutOfRangePatterns.Contains(250));
}

static void PlaybackClock()
{
    Equal(15, MvAnimationConstants.RuntimeFramesPerSecond);
    Equal(3, AnimationPlaybackClock.FrameAt(TimeSpan.FromSeconds(0.2), 20, false));
}

static MvAnimationCellSnapshot Cell(int pattern, double x = 0, double y = 0, double rotation = 0) => new(pattern, x, y, 100, rotation, false, 255, 0, "[-1,0,0,100,0,0,255,0]");
static MvAnimationFrameSnapshot Frame(int index, params MvAnimationCellSnapshot[] cells) => new(index, cells, "[]");

static MutableJsonProjectDocument Create(string json)
{
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    return MutableJsonProjectDocument.Create("data/Animations.json", MvDocumentKind.Animations, bytes, new ContentFingerprint(bytes.LongLength, DateTimeOffset.UnixEpoch, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
}

static void True(bool value, string message = "Assertion was false.") { if (!value) throw new InvalidOperationException(message); }
static void Near(double expected, double actual, double tolerance = 0.000001) { if (Math.Abs(expected - actual) > tolerance) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}."); }
