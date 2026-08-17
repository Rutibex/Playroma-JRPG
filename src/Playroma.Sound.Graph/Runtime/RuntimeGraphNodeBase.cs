using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Graph.Runtime;

public abstract class RuntimeGraphNodeBase : IRuntimeGraphNode
{
    private readonly List<RuntimeParameter> _parameters = [];
    private readonly Dictionary<string, int> _parameterIndices = new(StringComparer.Ordinal);
    private bool _prepared;

    protected RuntimeGraphNodeBase(NodeTypeDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    protected AudioFormat Format { get; private set; }
    public NodeTypeDescriptor Descriptor { get; }
    public virtual int LatencyFrames => 0;
    public virtual int MaximumTailFrames => 0;
    public virtual bool IsGenerator => false;
    public virtual bool IsInfinite => false;
    public virtual bool IsComplete => false;
    public virtual bool SupportsInPlace => false;
    public int ParameterCount => _parameters.Count;

    public void Prepare(AudioFormat format)
    {
        Format = format;
        OnPrepare(format);
        _prepared = true;
        Reset();
    }

    public abstract void Reset();

    public void Process(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount)
    {
        if (!_prepared)
        {
            throw new InvalidOperationException($"{GetType().Name} must be prepared before processing.");
        }

        if (frameCount < 0 || frameCount > Format.BlockFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        ProcessCore(buffers, inputSlots, outputSlot, frameCount);
    }

    public bool TryGetParameterIndex(string parameterId, out int index) =>
        _parameterIndices.TryGetValue(parameterId, out index);

    public void SetParameter(int parameterIndex, float value)
    {
        if ((uint)parameterIndex >= (uint)_parameters.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(parameterIndex));
        }

        RuntimeParameter parameter = _parameters[parameterIndex];
        parameter.Setter(Math.Clamp(value, parameter.Minimum, parameter.Maximum));
    }

    protected int RegisterParameter(string id, float minimum, float maximum, Action<float> setter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(setter);
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        if (_parameterIndices.ContainsKey(id))
        {
            throw new InvalidOperationException($"Runtime parameter '{id}' is already registered.");
        }

        int index = _parameters.Count;
        _parameters.Add(new RuntimeParameter(minimum, maximum, setter));
        _parameterIndices.Add(id, index);
        return index;
    }

    protected Span<float> GetOutput(float[][] buffers, int outputSlot, int frameCount)
    {
        int samples = checked(frameCount * Format.Channels);
        return buffers[outputSlot].AsSpan(0, samples);
    }

    protected ReadOnlySpan<float> GetInput(float[][] buffers, int[] inputSlots, int inputIndex, int frameCount)
    {
        if ((uint)inputIndex >= (uint)inputSlots.Length)
        {
            return ReadOnlySpan<float>.Empty;
        }

        int samples = checked(frameCount * Format.Channels);
        return buffers[inputSlots[inputIndex]].AsSpan(0, samples);
    }

    protected virtual void OnPrepare(AudioFormat format)
    {
    }

    protected abstract void ProcessCore(float[][] buffers, int[] inputSlots, int outputSlot, int frameCount);

    private sealed record RuntimeParameter(float Minimum, float Maximum, Action<float> Setter);
}
