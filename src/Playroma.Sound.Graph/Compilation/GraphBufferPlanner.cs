namespace Playroma.Sound.Graph.Compilation;

internal static class GraphBufferPlanner
{
    public static (int[] Slots, int SlotCount) Plan(IReadOnlyList<PendingCommand> commands, int outputNodeIndex, bool reuse)
    {
        int count = commands.Count;
        if (!reuse)
        {
            return (Enumerable.Range(0, count).ToArray(), count);
        }

        int[] lastUse = new int[count];
        for (int i = 0; i < count; i++)
        {
            lastUse[i] = i;
        }

        for (int commandIndex = 0; commandIndex < count; commandIndex++)
        {
            foreach (int input in commands[commandIndex].InputNodeIndices)
            {
                lastUse[input] = Math.Max(lastUse[input], commandIndex);
            }
        }

        lastUse[outputNodeIndex] = Math.Max(lastUse[outputNodeIndex], count);
        int[] slots = Enumerable.Repeat(-1, count).ToArray();
        bool[] released = new bool[count];
        var free = new Stack<int>();
        int slotCount = 0;

        for (int commandIndex = 0; commandIndex < count; commandIndex++)
        {
            for (int prior = 0; prior < commandIndex; prior++)
            {
                if (!released[prior] && lastUse[prior] < commandIndex)
                {
                    free.Push(slots[prior]);
                    released[prior] = true;
                }
            }

            slots[commandIndex] = free.Count > 0 ? free.Pop() : slotCount++;
        }

        return (slots, slotCount);
    }
}
