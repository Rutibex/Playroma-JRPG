namespace Playroma.Sound.Graph.Events;

/// <summary>
/// Allocation-free single-producer/single-consumer ring queue for audio-thread commands.
/// </summary>
public sealed class BoundedRuntimeEventQueue
{
    private readonly ScheduledRuntimeEvent[] _items;
    private readonly int _mask;
    private int _head;
    private int _tail;

    public BoundedRuntimeEventQueue(int capacity)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        int powerOfTwo = 1;
        while (powerOfTwo < capacity)
        {
            powerOfTwo = checked(powerOfTwo << 1);
        }

        _items = new ScheduledRuntimeEvent[powerOfTwo];
        _mask = powerOfTwo - 1;
    }

    public int Capacity => _items.Length - 1;

    public bool TryEnqueue(ScheduledRuntimeEvent item)
    {
        int tail = _tail;
        int next = (tail + 1) & _mask;
        if (next == Volatile.Read(ref _head))
        {
            return false;
        }

        _items[tail] = item;
        Volatile.Write(ref _tail, next);
        return true;
    }

    public bool TryDequeue(out ScheduledRuntimeEvent item)
    {
        int head = _head;
        if (head == Volatile.Read(ref _tail))
        {
            item = default;
            return false;
        }

        item = _items[head];
        Volatile.Write(ref _head, (head + 1) & _mask);
        return true;
    }

    public void Clear()
    {
        Volatile.Write(ref _head, 0);
        Volatile.Write(ref _tail, 0);
    }
}
