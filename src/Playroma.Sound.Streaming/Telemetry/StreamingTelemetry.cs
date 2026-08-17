namespace Playroma.Sound.Streaming.Telemetry;

public sealed record BusTelemetry(string Id, float Peak, float Rms);
public sealed record StateMachineTelemetry(string Id, string CurrentState);
public sealed record SoundPoolTelemetry(string SoundId, int ActiveVoices, int Capacity);

/// <summary>
/// Immutable control-thread snapshot copied from the renderer's double-buffered telemetry publication.
/// Reading telemetry may allocate; publishing telemetry on the audio thread does not.
/// </summary>
public sealed record StreamingTelemetrySnapshot(
    long SampleFrame,
    int ActiveVoices,
    int VirtualVoices,
    int ProtectedVoices,
    long StartedVoices,
    long CompletedVoices,
    long StolenVoices,
    long RejectedVoices,
    long FaultedVoices,
    long CommandQueueOverflows,
    int CommandQueueCount,
    int CommandQueueCapacity,
    int MaximumVoices,
    double LastRenderMilliseconds,
    double PeakRenderMilliseconds,
    double RenderDeadlineMilliseconds,
    double LastRenderLoad,
    double PeakRenderLoad,
    IReadOnlyList<BusTelemetry> Buses,
    IReadOnlyList<StateMachineTelemetry> StateMachines,
    IReadOnlyList<SoundPoolTelemetry> Pools)
{
    public static StreamingTelemetrySnapshot Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
        [], [], []);
}

internal sealed class StreamingTelemetryBuffer
{
    public StreamingTelemetryBuffer(int busCount, int stateMachineCount, int soundPoolCount)
    {
        BusPeaks = new float[busCount];
        BusRms = new float[busCount];
        StateMachineStates = new int[stateMachineCount];
        PoolActiveVoices = new int[soundPoolCount];
    }

    public int Version;
    public long SampleFrame;
    public int ActiveVoices;
    public int VirtualVoices;
    public int ProtectedVoices;
    public long StartedVoices;
    public long CompletedVoices;
    public long StolenVoices;
    public long RejectedVoices;
    public long FaultedVoices;
    public long CommandQueueOverflows;
    public int CommandQueueCount;
    public double LastRenderMilliseconds;
    public double PeakRenderMilliseconds;
    public float[] BusPeaks { get; }
    public float[] BusRms { get; }
    public int[] StateMachineStates { get; }
    public int[] PoolActiveVoices { get; }
}
