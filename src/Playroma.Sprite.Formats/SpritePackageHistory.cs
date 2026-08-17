using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed class SpritePackageHistory
{
    private readonly Stack<IHistoryEntry> undo = new();
    private readonly Stack<IHistoryEntry> redo = new();
    private long memoryBytes;
    private long memoryLimitBytes = 512L * 1024L * 1024L;

    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public long MemoryBytes => memoryBytes;
    public long MemoryLimitBytes
    {
        get => memoryLimitBytes;
        set { memoryLimitBytes = Math.Clamp(value, 16L * 1024L * 1024L, 8L * 1024L * 1024L * 1024L); TrimToBudget(); }
    }
    public string? NextUndoDescription => undo.TryPeek(out IHistoryEntry? value) ? value.Description : null;
    public string? NextRedoDescription => redo.TryPeek(out IHistoryEntry? value) ? value.Description : null;
    public SelectionMask? LastRestoredSelection { get; private set; }
    public IReadOnlyList<string> UndoDescriptions => undo.Select(entry => entry.Description).ToArray();
    public IReadOnlyList<string> RedoDescriptions => redo.Select(entry => entry.Description).ToArray();

    public bool CommitPixel(string description, SpritePackageData package, Guid contentId, PixelBuffer before) =>
        CommitPixel(description, package, contentId, before, null, null);

    public bool CommitPixel(string description, SpritePackageData package, Guid contentId, PixelBuffer before, SelectionMask? beforeSelection, SelectionMask? afterSelection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description); ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(before);
        PixelBuffer after = package.GetRequired(contentId).Clone();
        bool pixelsChanged = !before.Bytes.Span.SequenceEqual(after.Bytes.Span);
        bool selectionChanged = !SelectionEquals(beforeSelection, afterSelection);
        if (!pixelsChanged && !selectionChanged) return false;
        Push(new PixelEntry(description.Trim(), contentId, before, after, beforeSelection?.Clone(), afterSelection?.Clone()));
        return true;
    }

    public bool CommitPackage(string description, SpritePackageData before, SpritePackageData after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description); ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        Push(new PackageEntry(description.Trim(), before.DeepClone(), after.DeepClone()));
        return true;
    }

    public SpritePackageData? Undo(SpritePackageData package)
    {
        if (!undo.TryPop(out IHistoryEntry? entry)) return null;
        SpritePackageData result = entry.Undo(package);
        LastRestoredSelection = entry.BeforeSelection?.Clone();
        redo.Push(entry);
        return result;
    }

    public SpritePackageData? Redo(SpritePackageData package)
    {
        if (!redo.TryPop(out IHistoryEntry? entry)) return null;
        SpritePackageData result = entry.Redo(package);
        LastRestoredSelection = entry.AfterSelection?.Clone();
        undo.Push(entry);
        return result;
    }

    public void Clear() { undo.Clear(); redo.Clear(); memoryBytes = 0; LastRestoredSelection = null; }

    private void Push(IHistoryEntry entry)
    {
        undo.Push(entry); memoryBytes += entry.EstimatedBytes;
        while (redo.TryPop(out IHistoryEntry? discarded)) memoryBytes -= discarded.EstimatedBytes;
        TrimToBudget();
    }

    public int TrimToBudget()
    {
        int removed = 0;
        while (memoryBytes > memoryLimitBytes && undo.Count > 1)
        {
            IHistoryEntry discarded = RemoveOldest(undo);
            memoryBytes -= discarded.EstimatedBytes;
            removed++;
        }
        while (memoryBytes > memoryLimitBytes && redo.Count > 0)
        {
            IHistoryEntry discarded = RemoveOldest(redo);
            memoryBytes -= discarded.EstimatedBytes;
            removed++;
        }
        if (memoryBytes < 0) memoryBytes = 0;
        return removed;
    }

    private static IHistoryEntry RemoveOldest(Stack<IHistoryEntry> stack)
    {
        IHistoryEntry[] newestFirst = stack.ToArray();
        IHistoryEntry oldest = newestFirst[^1];
        stack.Clear();
        for (int index = newestFirst.Length - 2; index >= 0; index--) stack.Push(newestFirst[index]);
        return oldest;
    }

    private static bool SelectionEquals(SelectionMask? first, SelectionMask? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null || first.Width != second.Width || first.Height != second.Height) return false;
        return first.Values.Span.SequenceEqual(second.Values.Span);
    }

    private interface IHistoryEntry
    {
        string Description { get; }
        long EstimatedBytes { get; }
        SelectionMask? BeforeSelection { get; }
        SelectionMask? AfterSelection { get; }
        SpritePackageData Undo(SpritePackageData current);
        SpritePackageData Redo(SpritePackageData current);
    }

    private sealed record PixelEntry(string Description, Guid ContentId, PixelBuffer Before, PixelBuffer After, SelectionMask? BeforeSelection, SelectionMask? AfterSelection) : IHistoryEntry
    {
        public long EstimatedBytes => checked((long)Before.Width * Before.Height * 8L + (BeforeSelection is null ? 0L : (long)BeforeSelection.Width * BeforeSelection.Height) + (AfterSelection is null ? 0L : (long)AfterSelection.Width * AfterSelection.Height));
        public SpritePackageData Undo(SpritePackageData current) { current.GetRequired(ContentId).CopyFrom(Before); return current; }
        public SpritePackageData Redo(SpritePackageData current) { current.GetRequired(ContentId).CopyFrom(After); return current; }
    }

    private sealed record PackageEntry(string Description, SpritePackageData Before, SpritePackageData After) : IHistoryEntry
    {
        public SelectionMask? BeforeSelection => null;
        public SelectionMask? AfterSelection => null;
        public long EstimatedBytes => Before.EstimateMemoryBytes() + After.EstimateMemoryBytes();
        public SpritePackageData Undo(SpritePackageData current) => Before.DeepClone();
        public SpritePackageData Redo(SpritePackageData current) => After.DeepClone();
    }
}
