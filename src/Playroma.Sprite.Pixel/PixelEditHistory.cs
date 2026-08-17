namespace Playroma.Sprite.Pixel;

public sealed class PixelEditHistory
{
    private readonly Stack<PixelEditEntry> undo = new();
    private readonly Stack<PixelEditEntry> redo = new();
    private long memoryBytes;

    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public long MemoryBytes => memoryBytes;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public bool Commit(string description, PixelBuffer target, PixelBuffer before)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(before);
        if (target.Width != before.Width || target.Height != before.Height) throw new ArgumentException("History buffers must have matching dimensions.");
        PixelBuffer after = target.Clone();
        if (before.Bytes.Span.SequenceEqual(after.Bytes.Span)) return false;
        PixelEditEntry entry = new(description.Trim(), before, after);
        undo.Push(entry);
        memoryBytes += entry.EstimatedBytes;
        while (redo.TryPop(out PixelEditEntry discarded)) memoryBytes -= discarded.EstimatedBytes;
        return true;
    }

    public string? Undo(PixelBuffer target)
    {
        if (undo.Count == 0) return null;
        PixelEditEntry entry = undo.Pop();
        target.CopyFrom(entry.Before);
        redo.Push(entry);
        return entry.Description;
    }

    public string? Redo(PixelBuffer target)
    {
        if (redo.Count == 0) return null;
        PixelEditEntry entry = redo.Pop();
        target.CopyFrom(entry.After);
        undo.Push(entry);
        return entry.Description;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
        memoryBytes = 0;
    }

    private sealed record PixelEditEntry(string Description, PixelBuffer Before, PixelBuffer After)
    {
        public long EstimatedBytes => checked((long)Before.Width * Before.Height * 8L);
    }
}
