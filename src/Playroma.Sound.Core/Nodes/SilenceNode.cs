namespace Playroma.Sound.Core.Nodes;

public sealed class SilenceNode : AudioNodeBase
{
    public override void Reset()
    {
    }

    protected override void ProcessCore(Span<float> output, int frameCount) =>
        output.Clear();
}
