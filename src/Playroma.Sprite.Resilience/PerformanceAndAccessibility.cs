using System.Collections.Concurrent;
using System.Diagnostics;
using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Resilience;

public sealed record SpritePerformanceSample(string Operation, TimeSpan Elapsed, long ManagedBytes, DateTimeOffset RecordedAt);
public sealed record SpritePerformanceSummary(string Operation, int Samples, double AverageMilliseconds, double MaximumMilliseconds, long MaximumManagedBytes);

public sealed class SpritePerformanceMonitor
{
    private readonly ConcurrentQueue<SpritePerformanceSample> samples = new();
    public int MaximumSamples { get; set; } = 500;

    public IDisposable Measure(string operation) => new Measurement(this, operation);

    public IReadOnlyList<SpritePerformanceSample> Samples => samples.Reverse().ToArray();

    public IReadOnlyList<SpritePerformanceSummary> Summaries => samples
        .GroupBy(item => item.Operation, StringComparer.OrdinalIgnoreCase)
        .Select(group => new SpritePerformanceSummary(group.Key, group.Count(), group.Average(item => item.Elapsed.TotalMilliseconds), group.Max(item => item.Elapsed.TotalMilliseconds), group.Max(item => item.ManagedBytes)))
        .OrderByDescending(item => item.AverageMilliseconds).ToArray();

    public void Clear() { while (samples.TryDequeue(out _)) { } }

    private void Record(string operation, TimeSpan elapsed)
    {
        samples.Enqueue(new SpritePerformanceSample(operation, elapsed, GC.GetTotalMemory(false), DateTimeOffset.UtcNow));
        while (samples.Count > Math.Clamp(MaximumSamples, 10, 10000)) samples.TryDequeue(out _);
    }

    private sealed class Measurement : IDisposable
    {
        private readonly SpritePerformanceMonitor owner;
        private readonly string operation;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private bool disposed;
        public Measurement(SpritePerformanceMonitor owner, string operation) { this.owner = owner; this.operation = operation; }
        public void Dispose() { if (disposed) return; disposed = true; stopwatch.Stop(); owner.Record(operation, stopwatch.Elapsed); }
    }
}

public sealed record SpriteLargeDocumentAssessment(bool IsLarge, long CanvasPixels, long EstimatedBytes, IReadOnlyList<string> Recommendations);

public static class SpriteLargeDocumentPolicy
{
    public static SpriteLargeDocumentAssessment Assess(SpritePackageData package, long pixelThreshold = 16_777_216)
    {
        ArgumentNullException.ThrowIfNull(package);
        long canvasPixels = checked((long)package.Document.Canvas.Width * package.Document.Canvas.Height * Math.Max(1, package.Document.Frames.Count));
        long estimated = package.EstimateMemoryBytes();
        bool large = canvasPixels >= pixelThreshold || estimated >= 768L * 1024L * 1024L;
        List<string> recommendations = [];
        if (large)
        {
            recommendations.Add("Keep onion-skin frame counts low while painting.");
            recommendations.Add("Use linked cels for unchanged animation frames.");
            recommendations.Add("Rasterize tilemaps only for final export operations.");
            recommendations.Add("Use background exports and retain recovery snapshots before batch effects.");
        }
        return new(large, canvasPixels, estimated, recommendations);
    }
}

public sealed record SpriteAccessibilityIssue(string Code, string Message, string Severity);

public static class SpriteAccessibilityAuditor
{
    public static IReadOnlyList<SpriteAccessibilityIssue> Audit(SpriteEditorPreferences preferences)
    {
        List<SpriteAccessibilityIssue> issues = [];
        foreach (string shortcut in SpriteShortcutRegistry.Validate(preferences.Shortcuts)) issues.Add(new("PSPR-A11Y-SHORTCUT", shortcut, "warning"));
        if (preferences.FontScale < .9d) issues.Add(new("PSPR-A11Y-FONT", "Font scale below 90% may reduce readability.", "warning"));
        if (preferences.UiScale < .9d) issues.Add(new("PSPR-A11Y-UI", "UI scale below 90% may make controls difficult to target.", "warning"));
        if (preferences.Theme == SpriteUiTheme.Dark && !preferences.HighContrast) issues.Add(new("PSPR-A11Y-CONTRAST", "Run visual contrast checks after changing dark-theme colors.", "information"));
        return issues;
    }

    public static double ContrastRatio((byte R, byte G, byte B) foreground, (byte R, byte G, byte B) background)
    {
        double first = RelativeLuminance(foreground); double second = RelativeLuminance(background);
        double lighter = Math.Max(first, second); double darker = Math.Min(first, second);
        return (lighter + .05d) / (darker + .05d);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        static double Convert(byte component)
        {
            double value = component / 255d;
            return value <= .04045d ? value / 12.92d : Math.Pow((value + .055d) / 1.055d, 2.4d);
        }
        return .2126d * Convert(color.R) + .7152d * Convert(color.G) + .0722d * Convert(color.B);
    }
}
