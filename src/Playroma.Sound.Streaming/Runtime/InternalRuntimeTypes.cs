using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Random;
using Playroma.Sound.Dsp.Distortion;
using Playroma.Sound.Dsp.Dynamics;
using Playroma.Sound.Dsp.Filters;
using Playroma.Sound.Dsp.Processing;
using Playroma.Sound.Dsp.Reverb;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Spatial;

namespace Playroma.Sound.Streaming.Runtime;

internal sealed class SceneParameterRuntime
{
    private readonly float _minimum;
    private readonly float _maximum;
    private readonly float _coefficient;

    public SceneParameterRuntime(SceneParameterDefinition definition, int sampleRate, int blockFrames)
    {
        Id = definition.Id;
        _minimum = definition.Minimum;
        _maximum = definition.Maximum;
        Current = Target = Math.Clamp(definition.DefaultValue, _minimum, _maximum);
        if (definition.SmoothingSeconds <= 0)
        {
            _coefficient = 0f;
        }
        else
        {
            double blocksPerSecond = sampleRate / (double)blockFrames;
            _coefficient = (float)Math.Exp(-1.0 / Math.Max(0.000001, definition.SmoothingSeconds * blocksPerSecond));
        }
    }

    public string Id { get; }
    public float Minimum => _minimum;
    public float Maximum => _maximum;
    public float Current { get; private set; }
    public float Target { get; private set; }
    public void SetTarget(float value) => Target = Math.Clamp(float.IsFinite(value) ? value : 0f, _minimum, _maximum);
    public void Restore(float current, float target)
    {
        Current = Math.Clamp(float.IsFinite(current) ? current : 0f, _minimum, _maximum);
        Target = Math.Clamp(float.IsFinite(target) ? target : 0f, _minimum, _maximum);
    }
    public float Advance()
    {
        Current = _coefficient <= 0f ? Target : Target + ((Current - Target) * _coefficient);
        return Current;
    }
}

internal sealed class BusRuntime
{
    public BusRuntime(SoundBusDefinition definition, AudioFormat format, int parentIndex, (int Target, float Gain)[] sends)
    {
        Id = definition.Id;
        ParentIndex = parentIndex;
        BaseGain = TargetGain = CurrentGain = definition.Gain;
        Sends = sends;
        Buffer = new float[format.SamplesPerBlock];
        Processors = BuildProcessorChain(definition, format);
    }

    public string Id { get; }
    public int ParentIndex { get; }
    public float BaseGain { get; }
    public float CurrentGain { get; set; }
    public float TargetGain { get; set; }
    public (int Target, float Gain)[] Sends { get; }
    public float[] Buffer { get; }
    public ProcessorChain Processors { get; }
    public float Peak { get; set; }
    public float Rms { get; set; }

    public void Reset()
    {
        Array.Clear(Buffer);
        CurrentGain = TargetGain = BaseGain;
        Peak = Rms = 0f;
        Processors.Reset();
    }

    private static ProcessorChain BuildProcessorChain(SoundBusDefinition definition, AudioFormat format)
    {
        var chain = new ProcessorChain();
        foreach (SoundBusEffectDefinition effect in definition.Effects)
        {
            IAudioProcessor processor = effect.Kind switch
            {
                SoundBusEffectKind.LowPass => BuildFilter(BiquadFilterType.LowPass, effect),
                SoundBusEffectKind.HighPass => BuildFilter(BiquadFilterType.HighPass, effect),
                SoundBusEffectKind.Compressor => BuildCompressor(effect),
                SoundBusEffectKind.Limiter => BuildLimiter(effect),
                SoundBusEffectKind.Reverb => BuildReverb(effect),
                SoundBusEffectKind.Saturation => BuildSaturation(effect),
                _ => throw new ArgumentOutOfRangeException(nameof(effect.Kind))
            };
            chain.Add(processor);
        }
        chain.Prepare(format);
        return chain;
    }

    private static BiquadFilterProcessor BuildFilter(BiquadFilterType type, SoundBusEffectDefinition effect)
    {
        var filter = new BiquadFilterProcessor(type);
        float frequency = type == BiquadFilterType.LowPass
            ? 300f + (Math.Clamp(effect.Amount, 0f, 1f) * 19_700f)
            : 20f + (Math.Clamp(effect.Amount, 0f, 1f) * 3_980f);
        filter.Frequency.SetValue(frequency);
        filter.Q.SetValue(0.5f + (Math.Clamp(effect.Secondary, 0f, 1f) * 5f));
        return filter;
    }

    private static CompressorProcessor BuildCompressor(SoundBusEffectDefinition effect)
    {
        var compressor = new CompressorProcessor();
        compressor.ThresholdDecibels.SetValue(-6f - (Math.Clamp(effect.Amount, 0f, 1f) * 30f));
        compressor.Ratio.SetValue(1f + (Math.Clamp(effect.Secondary, 0f, 1f) * 11f));
        compressor.Mix.SetValue(Math.Clamp(effect.Mix, 0f, 1f));
        return compressor;
    }

    private static LimiterProcessor BuildLimiter(SoundBusEffectDefinition effect)
    {
        var limiter = new LimiterProcessor();
        limiter.CeilingDecibels.SetValue(-0.1f - (Math.Clamp(effect.Amount, 0f, 1f) * 5.9f));
        limiter.ReleaseMilliseconds.SetValue(20f + (Math.Clamp(effect.Secondary, 0f, 1f) * 480f));
        return limiter;
    }

    private static AlgorithmicReverbProcessor BuildReverb(SoundBusEffectDefinition effect)
    {
        var reverb = new AlgorithmicReverbProcessor();
        reverb.RoomSize.SetValue(Math.Clamp(effect.Amount, 0f, 1f));
        reverb.Damping.SetValue(Math.Clamp(effect.Secondary, 0f, 1f));
        reverb.Wet.SetValue(Math.Clamp(effect.Mix, 0f, 1f));
        reverb.Dry.SetValue(effect.WetOnly ? 0f : 1f);
        return reverb;
    }

    private static WaveshaperProcessor BuildSaturation(SoundBusEffectDefinition effect)
    {
        var saturation = new WaveshaperProcessor(WaveshaperMode.HyperbolicTangent);
        saturation.DriveDecibels.SetValue(Math.Clamp(effect.Amount, 0f, 1f) * 24f);
        saturation.Mix.SetValue(Math.Clamp(effect.Mix, 0f, 1f));
        saturation.OutputDecibels.SetValue(-Math.Clamp(effect.Secondary, 0f, 1f) * 6f);
        return saturation;
    }
}

internal sealed class LayerRuntime
{
    public LayerRuntime(SoundLayerDefinition definition, int soundIndex, int busIndex, int zoneIndex, int stateMachineIndex, int[] activeStateIndices, int sampleRate)
    {
        Definition = definition;
        SoundIndex = soundIndex;
        BusIndex = busIndex;
        ZoneIndex = zoneIndex;
        StateMachineIndex = stateMachineIndex;
        ActiveStateIndices = activeStateIndices;
        FadeFrames = Math.Max(1, (int)Math.Round(definition.FadeSeconds * sampleRate));
        SemanticGain = 1f;
        Spatial = new SoundEmitterState(
            definition.Spatial.Enabled,
            definition.Spatial.Position,
            definition.Spatial.Velocity,
            definition.Spatial.MinimumDistance,
            definition.Spatial.MaximumDistance,
            definition.Spatial.Occlusion,
            definition.Spatial.Obstruction);
    }

    public SoundLayerDefinition Definition { get; }
    public int SoundIndex { get; }
    public int BusIndex { get; }
    public int ZoneIndex { get; }
    public int StateMachineIndex { get; }
    public int[] ActiveStateIndices { get; }
    public int FadeFrames { get; }
    public int VoiceIndex { get; set; } = -1;
    public float SemanticGain { get; set; }
    public float PreviewGain { get; set; } = 1f;
    public SoundEmitterState Spatial { get; set; }

    public bool IsActive(StateMachineRuntime[] machines)
    {
        if (StateMachineIndex < 0 || ActiveStateIndices.Length == 0) return true;
        int current = machines[StateMachineIndex].CurrentStateIndex;
        for (int i = 0; i < ActiveStateIndices.Length; i++) if (ActiveStateIndices[i] == current) return true;
        return false;
    }
}

internal sealed class EventEmitterRuntime
{
    public EventEmitterRuntime(
        SoundEventEmitterDefinition definition,
        int[] soundIndices,
        int busIndex,
        int zoneIndex,
        int stateMachineIndex,
        int[] activeStateIndices,
        Pcg32 random,
        int sampleRate)
    {
        Definition = definition;
        SoundIndices = soundIndices;
        BusIndex = busIndex;
        ZoneIndex = zoneIndex;
        StateMachineIndex = stateMachineIndex;
        ActiveStateIndices = activeStateIndices;
        Random = random;
        SampleRate = sampleRate;
        RateScale = 1f;
        GainScale = 1f;
    }

    public SoundEventEmitterDefinition Definition { get; }
    public int[] SoundIndices { get; }
    public int BusIndex { get; }
    public int ZoneIndex { get; }
    public int StateMachineIndex { get; }
    public int[] ActiveStateIndices { get; }
    public Pcg32 Random;
    public int SampleRate { get; }
    public long NextEventFrame { get; set; }
    public int ActiveVoices { get; set; }
    public float RateScale { get; set; }
    public float GainScale { get; set; }
    public float PreviewGain { get; set; } = 1f;

    public bool IsActive(StateMachineRuntime[] machines)
    {
        if (StateMachineIndex < 0 || ActiveStateIndices.Length == 0) return true;
        int current = machines[StateMachineIndex].CurrentStateIndex;
        for (int i = 0; i < ActiveStateIndices.Length; i++) if (ActiveStateIndices[i] == current) return true;
        return false;
    }

    public void ScheduleNext(long fromFrame)
    {
        double minimum = Definition.MinimumIntervalSeconds;
        double maximum = Math.Max(minimum, Definition.MaximumIntervalSeconds);
        double interval = minimum + ((maximum - minimum) * Random.NextFloat());
        interval /= Math.Max(0.01f, RateScale);
        NextEventFrame = fromFrame + Math.Max(1, (long)Math.Round(interval * SampleRate));
    }
}


internal sealed class EventClusterRuntime
{
    public EventClusterRuntime(SoundEventClusterDefinition definition, ClusterEventRuntime[] events, Pcg32 random)
    {
        Definition = definition;
        Events = events;
        Random = random;
    }

    public SoundEventClusterDefinition Definition { get; }
    public ClusterEventRuntime[] Events { get; }
    public Pcg32 Random;
}

internal readonly record struct ClusterEventRuntime(
    int SoundIndex,
    int BusIndex,
    int OffsetFrames,
    float Probability,
    float Gain,
    float Pan,
    int Priority,
    int ZoneIndex,
    SoundEmitterState Spatial);

internal sealed class StateMachineRuntime
{
    public StateMachineRuntime(SceneStateMachineDefinition definition)
    {
        Definition = definition;
        StateIndices = definition.States.Select((state, index) => (state.Id, index)).ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        CurrentStateIndex = StateIndices[definition.InitialState];
    }

    public SceneStateMachineDefinition Definition { get; }
    public Dictionary<string, int> StateIndices { get; }
    public int CurrentStateIndex { get; set; }
    public string CurrentStateId => Definition.States[CurrentStateIndex].Id;

    public bool ApplyTrigger(string trigger)
    {
        string current = CurrentStateId;
        foreach (SceneTransitionDefinition transition in Definition.Transitions)
        {
            if (transition.Trigger == trigger && (transition.FromState == "*" || transition.FromState == current))
            {
                CurrentStateIndex = StateIndices[transition.ToState];
                return true;
            }
        }
        return false;
    }
}

internal sealed class VoiceRuntime
{
    public VoiceRuntime(int slotIndex, int soundIndex, string soundId, CompiledSoundGraph graph, int channels)
    {
        SlotIndex = slotIndex;
        SoundIndex = soundIndex;
        SoundId = soundId;
        Graph = graph;
        LastFrame = new float[channels];
    }

    public int SlotIndex { get; }
    public int SoundIndex { get; }
    public string SoundId { get; }
    public CompiledSoundGraph Graph { get; set; }
    public float[] LastFrame { get; }
    public bool Active { get; set; }
    public bool CountsAgainstLimit { get; set; }
    public bool Loop { get; set; }
    public bool Protected { get; set; }
    public bool Virtual { get; set; }
    public int BusIndex { get; set; }
    public int Priority { get; set; }
    public float CurrentGain { get; set; }
    public float TargetGain { get; set; }
    public float Pan { get; set; }
    public int FadeFramesRemaining { get; set; }
    public int StartDelayFrames { get; set; }
    public int LayerIndex { get; set; } = -1;
    public int EmitterIndex { get; set; } = -1;
    public int ZoneIndex { get; set; } = -1;
    public int HandleId { get; set; } = -1;
    public long LogicalFrame { get; set; }
    public long VirtualFramesOwed { get; set; }
    public long StartSequence { get; set; }
    public SoundEmitterState Spatial { get; set; } = SoundEmitterState.Disabled;
    public bool HasPendingStart { get; set; }
    public PendingVoiceStart PendingStart;

    public void ResetSlot()
    {
        Graph.Reset();
        Active = false;
        CountsAgainstLimit = false;
        Loop = false;
        Protected = false;
        Virtual = false;
        CurrentGain = TargetGain = 0f;
        Pan = 0f;
        FadeFramesRemaining = 0;
        StartDelayFrames = 0;
        LayerIndex = -1;
        EmitterIndex = -1;
        ZoneIndex = -1;
        HandleId = -1;
        LogicalFrame = 0;
        VirtualFramesOwed = 0;
        Spatial = SoundEmitterState.Disabled;
        HasPendingStart = false;
        PendingStart = default;
        Array.Clear(LastFrame);
    }
}

internal readonly record struct PendingVoiceStart(
    int BusIndex,
    float Gain,
    float Pan,
    int Priority,
    bool Loop,
    bool Protected,
    int StartDelayFrames,
    int LayerIndex,
    int EmitterIndex,
    int ZoneIndex,
    int HandleId,
    SoundEmitterState Spatial);
