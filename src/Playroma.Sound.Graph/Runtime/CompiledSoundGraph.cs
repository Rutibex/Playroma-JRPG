using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Events;

namespace Playroma.Sound.Graph.Runtime;

/// <summary>
/// Immutable, prepared execution plan. Processing performs no graph traversal, JSON work, or buffer allocation.
/// </summary>
public sealed class CompiledSoundGraph : IAudioNode
{
    private readonly AudioFormat _format;
    private readonly IRuntimeGraphNode[] _nodes;
    private readonly CompiledCommand[] _commands;
    private readonly float[][] _buffers;
    private readonly int _outputSlot;
    private readonly Dictionary<string, CompiledParameterAddress> _parameterAddresses;
    private readonly CompiledMacro[] _macros;
    private readonly Dictionary<string, int> _macroIndices;
    private readonly int[] _generatorIndices;
    private readonly bool _infinite;
    private readonly int _maximumTailFrames;
    private readonly BoundedRuntimeEventQueue _eventQueue;
    private readonly bool _applyMacroDefaults;
    private ScheduledRuntimeEvent _pendingEvent;
    private bool _hasPendingEvent;
    private int _remainingTailFrames;
    private long _sampleFrame;
    private long _lastScheduledFrame;

    internal CompiledSoundGraph(
        string id,
        AudioFormat format,
        IRuntimeGraphNode[] nodes,
        CompiledCommand[] commands,
        int bufferSlotCount,
        int outputSlot,
        Dictionary<string, CompiledParameterAddress> parameterAddresses,
        CompiledMacro[] macros,
        Dictionary<string, int> macroIndices,
        int[] generatorIndices,
        bool infinite,
        int maximumTailFrames,
        int totalLatencyFrames,
        IReadOnlyList<CompiledGraphNodeInfo> nodeInfos,
        int eventQueueCapacity,
        bool applyMacroDefaults)
    {
        Id = id;
        _format = format;
        _nodes = nodes;
        _commands = commands;
        _buffers = Enumerable.Range(0, bufferSlotCount)
            .Select(_ => new float[format.SamplesPerBlock])
            .ToArray();
        _outputSlot = outputSlot;
        _parameterAddresses = parameterAddresses;
        _macros = macros;
        _macroIndices = macroIndices;
        _generatorIndices = generatorIndices;
        _infinite = infinite;
        _maximumTailFrames = maximumTailFrames;
        TotalLatencyFrames = totalLatencyFrames;
        NodeInfos = nodeInfos;
        _eventQueue = new BoundedRuntimeEventQueue(eventQueueCapacity);
        _applyMacroDefaults = applyMacroDefaults;
        Reset();
    }

    public string Id { get; }
    public AudioFormat CompiledFormat => _format;
    public GraphActivityState State { get; private set; }
    public long CurrentSampleFrame => Volatile.Read(ref _sampleFrame);
    public int TotalLatencyFrames { get; }
    public int BufferSlotCount => _buffers.Length;
    public IReadOnlyList<CompiledGraphNodeInfo> NodeInfos { get; }
    public bool IsComplete => State == GraphActivityState.Complete;

    public void Prepare(AudioFormat format)
    {
        if (format != _format)
        {
            throw new InvalidOperationException("A compiled sound graph must be recompiled for a different audio format.");
        }

        Reset();
    }

    public void Reset()
    {
        foreach (IRuntimeGraphNode node in _nodes)
        {
            node.Reset();
        }

        foreach (float[] buffer in _buffers)
        {
            Array.Clear(buffer);
        }

        _eventQueue.Clear();
        _hasPendingEvent = false;
        _remainingTailFrames = _maximumTailFrames;
        Volatile.Write(ref _sampleFrame, 0);
        Volatile.Write(ref _lastScheduledFrame, 0);
        State = _infinite ? GraphActivityState.Infinite : GraphActivityState.Active;

        if (_applyMacroDefaults)
        {
            for (int i = 0; i < _macros.Length; i++)
            {
                ApplyMacro(i, _macros[i].DefaultValue);
            }
        }
    }

    public bool TrySetParameter(string targetPath, float value) =>
        TryScheduleParameter(targetPath, value, CurrentSampleFrame);

    public bool TryScheduleParameter(string targetPath, float value, long sampleFrame)
    {
        if (!_parameterAddresses.TryGetValue(targetPath, out CompiledParameterAddress? address) || address is null)
        {
            return false;
        }

        return TrySchedule(new ScheduledRuntimeEvent(
            NormalizeScheduledFrame(sampleFrame),
            RuntimeEventKind.Parameter,
            address.NodeIndex,
            address.ParameterIndex,
            value));
    }

    public bool TrySetMacro(string macroId, float value) =>
        TryScheduleMacro(macroId, value, CurrentSampleFrame);

    public bool TryScheduleMacro(string macroId, float value, long sampleFrame)
    {
        if (!_macroIndices.TryGetValue(macroId, out int macroIndex))
        {
            return false;
        }

        return TrySchedule(new ScheduledRuntimeEvent(
            NormalizeScheduledFrame(sampleFrame),
            RuntimeEventKind.Macro,
            macroIndex,
            0,
            value));
    }

    public void Process(Span<float> output, int frameCount)
    {
        if (frameCount < 0 || frameCount > _format.BlockFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        int requiredSamples = checked(frameCount * _format.Channels);
        if (output.Length < requiredSamples)
        {
            throw new ArgumentException("Output span is too small for the requested frame count.", nameof(output));
        }

        output = output[..requiredSamples];
        output.Clear();
        if (frameCount == 0 || IsComplete)
        {
            return;
        }

        int framesRemaining = frameCount;
        int outputFrameOffset = 0;
        while (framesRemaining > 0 && !IsComplete)
        {
            EnsurePendingEvent();
            long currentFrame = CurrentSampleFrame;
            while (_hasPendingEvent && _pendingEvent.SampleFrame <= currentFrame)
            {
                ApplyEvent(_pendingEvent);
                _hasPendingEvent = false;
                EnsurePendingEvent();
            }

            int segmentFrames = framesRemaining;
            if (_hasPendingEvent && _pendingEvent.SampleFrame < currentFrame + framesRemaining)
            {
                segmentFrames = checked((int)(_pendingEvent.SampleFrame - currentFrame));
            }

            if (segmentFrames <= 0)
            {
                continue;
            }

            ExecuteSegment(output.Slice(outputFrameOffset * _format.Channels, segmentFrames * _format.Channels), segmentFrames);
            outputFrameOffset += segmentFrames;
            framesRemaining -= segmentFrames;
            Volatile.Write(ref _sampleFrame, currentFrame + segmentFrames);
            UpdateActivity(segmentFrames);
        }
    }

    private bool TrySchedule(ScheduledRuntimeEvent scheduledEvent)
    {
        long previous = Volatile.Read(ref _lastScheduledFrame);
        if (scheduledEvent.SampleFrame < previous)
        {
            return false;
        }

        if (!_eventQueue.TryEnqueue(scheduledEvent))
        {
            return false;
        }

        Volatile.Write(ref _lastScheduledFrame, scheduledEvent.SampleFrame);
        return true;
    }

    private long NormalizeScheduledFrame(long sampleFrame) => Math.Max(CurrentSampleFrame, sampleFrame);

    private void EnsurePendingEvent()
    {
        if (!_hasPendingEvent && _eventQueue.TryDequeue(out ScheduledRuntimeEvent item))
        {
            _pendingEvent = item;
            _hasPendingEvent = true;
        }
    }

    private void ApplyEvent(ScheduledRuntimeEvent item)
    {
        if (item.Kind == RuntimeEventKind.Parameter)
        {
            _nodes[item.TargetIndex].SetParameter(item.ParameterIndex, item.Value);
        }
        else
        {
            ApplyMacro(item.TargetIndex, item.Value);
        }
    }

    private void ApplyMacro(int macroIndex, float value)
    {
        CompiledMacro macro = _macros[macroIndex];
        value = Math.Clamp(value, macro.Minimum, macro.Maximum);
        foreach (CompiledMacroBinding binding in macro.Bindings)
        {
            float amount = (value - binding.InputMinimum) / (binding.InputMaximum - binding.InputMinimum);
            amount = Math.Clamp(amount, 0f, 1f);
            amount = binding.Curve switch
            {
                MacroMappingCurve.SmoothStep => amount * amount * (3f - (2f * amount)),
                MacroMappingCurve.Exponential => amount * amount,
                MacroMappingCurve.Logarithmic => MathF.Sqrt(amount),
                MacroMappingCurve.Inverted => 1f - amount,
                MacroMappingCurve.Stepped => Quantize(amount, binding.Steps),
                _ => amount
            };
            float mapped = binding.OutputMinimum + ((binding.OutputMaximum - binding.OutputMinimum) * amount);
            _nodes[binding.NodeIndex].SetParameter(binding.ParameterIndex, mapped);
        }
    }

    private void ExecuteSegment(Span<float> output, int frameCount)
    {
        foreach (CompiledCommand command in _commands)
        {
            _nodes[command.NodeIndex].Process(_buffers, command.InputSlots, command.OutputSlot, frameCount);
        }

        _buffers[_outputSlot].AsSpan(0, frameCount * _format.Channels).CopyTo(output);
    }

    private void UpdateActivity(int processedFrames)
    {
        if (State == GraphActivityState.Infinite || State == GraphActivityState.Complete)
        {
            return;
        }

        if (State == GraphActivityState.TailActive)
        {
            _remainingTailFrames -= processedFrames;
            if (_remainingTailFrames <= 0)
            {
                State = GraphActivityState.Complete;
            }

            return;
        }

        bool sourcesComplete = true;
        for (int i = 0; i < _generatorIndices.Length; i++)
        {
            if (!_nodes[_generatorIndices[i]].IsComplete)
            {
                sourcesComplete = false;
                break;
            }
        }

        if (!sourcesComplete)
        {
            return;
        }

        State = GraphActivityState.SourceComplete;
        if (_maximumTailFrames > 0)
        {
            _remainingTailFrames = _maximumTailFrames;
            State = GraphActivityState.TailActive;
        }
        else
        {
            State = GraphActivityState.Complete;
        }
    }

    private static float Quantize(float value, int steps)
    {
        steps = Math.Max(2, steps);
        return MathF.Round(value * (steps - 1)) / (steps - 1);
    }
}
