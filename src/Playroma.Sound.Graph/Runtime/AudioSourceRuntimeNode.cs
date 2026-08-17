using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public sealed class AudioSourceRuntimeNode : RuntimeGraphNodeBase
{
    private readonly IAudioNode _source;
    private readonly bool _infinite;

    public AudioSourceRuntimeNode(NodeTypeDescriptor descriptor, IAudioNode source, bool infinite)
        : base(descriptor)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _infinite = infinite;
    }

    public override bool IsGenerator => true;
    public override bool IsInfinite => _infinite;
    public override bool IsComplete => !_infinite && _source.IsComplete;

    public int AddParameter(string id, float minimum, float maximum, Action<float> setter) =>
        RegisterParameter(id, minimum, maximum, setter);

    protected override void OnPrepare(AudioFormat format) => _source.Prepare(format);

    public override void Reset() => _source.Reset();

    protected override void ProcessCore(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount) =>
        _source.Process(GetOutput(buffers, outputSlot, frameCount), frameCount);
}
