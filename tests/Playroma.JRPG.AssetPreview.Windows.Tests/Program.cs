using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP Phase 5 Windows preview tests require Windows.");
    return 0;
}

List<(string Name, Func<Task> Test)> tests =
[
    ("Image preview releases source handle", ImageHandleAsync),
    ("Thumbnail provider releases source handle", ThumbnailHandleAsync),
    ("Private font session is disposable", FontSessionAsync)
];
int failures = 0;
foreach ((string name, Func<Task> test) in tests)
{
    try { await test().ConfigureAwait(false); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception); }
}
return failures == 0 ? 0 : 1;

static async Task ImageHandleAsync()
{
    using TemporaryDirectory temp = new();
    string path = Path.Combine(temp.Path, "img", "pictures", "Preview.png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, TinyPng()).ConfigureAwait(false);
    PhysicalAssetContentSource content = new(temp.Path);
    using Bitmap bitmap = await AssetImageLoader.LoadOwnedBitmapAsync(content, "img/pictures/Preview.png").ConfigureAwait(false);
    string renamed = Path.Combine(Path.GetDirectoryName(path)!, "Renamed.png");
    File.Move(path, renamed);
    True(File.Exists(renamed));
}

static async Task ThumbnailHandleAsync()
{
    using TemporaryDirectory temp = new();
    string path = Path.Combine(temp.Path, "img", "pictures", "Preview.png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, TinyPng()).ConfigureAwait(false);
    PhysicalAssetContentSource content = new(temp.Path);
    WindowsImageThumbnailProvider provider = new(content);
    AssetVariant variant = new("img/pictures/Preview.png", ".png", new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), null, false);
    AssetRecord record = new(new AssetKey(AssetCategory.Pictures, "Preview"), AssetMediaKind.Image, [variant], AssetMetadataSummary.Empty, AssetUsageSummary.Empty, [], AssetStatus.Available);
    AssetThumbnail thumbnail = await provider.CreateAsync(record, variant, new AssetThumbnailRequest(96, 96)).ConfigureAwait(false);
    True(thumbnail.PngBytes.Length > 0);
    File.Delete(path);
    True(!File.Exists(path));
}

static Task FontSessionAsync()
{
    string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    string? path = Directory.EnumerateFiles(fonts, "*.ttf").FirstOrDefault();
    if (path is null) return Task.CompletedTask;
    using PrivateFontPreviewSession session = new(path);
    using Font font = session.CreateFont(12f);
    True(font.Size > 0);
    return Task.CompletedTask;
}

static byte[] TinyPng() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+X9iVAAAAAElFTkSuQmCC");
static void True(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PlayromaJRPG-Phase5-Windows-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
    public string Path { get; }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
