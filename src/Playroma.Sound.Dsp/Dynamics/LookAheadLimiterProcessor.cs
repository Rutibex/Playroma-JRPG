using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Dynamics;

/// <summary>
/// True look-ahead peak limiter. The constructor fixes latency so hosts can compensate
/// accurately. It scans the buffered future window for a channel-linked peak.
/// </summary>
public sealed class LookAheadLimiterProcessor : AudioProcessorBase
{
    private readonly double _lookAheadMilliseconds;
    private float[] _buffer = [];
    private int _bufferFrames;
    private int _writeFrame;
    private float _gain = 1f;
    private int _latencyFrames;
    private float[] _peakDequeValues = [];
    private long[] _peakDequeSequences = [];
    private int _dequeHead;
    private int _dequeTail;
    private int _dequeCount;
    private long _sequence;

    public LookAheadLimiterProcessor(double lookAheadMilliseconds = 5.0)
    {
        if (!double.IsFinite(lookAheadMilliseconds) || lookAheadMilliseconds < 0 || lookAheadMilliseconds > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(lookAheadMilliseconds));
        }

        _lookAheadMilliseconds = lookAheadMilliseconds;
        CeilingDecibels = new AudioParameter(
            new ParameterMetadata("ceiling", "Ceiling", "dB", -24f, 0f, -0.5f));
        ReleaseMilliseconds = new AudioParameter(
            new ParameterMetadata("release", "Release", "ms", 1f, 5_000f, 80f, ParameterScale.Logarithmic));
    }

    public AudioParameter CeilingDecibels { get; }
    public AudioParameter ReleaseMilliseconds { get; }
    public override int LatencyFrames => _latencyFrames;
    public float CurrentGain => _gain;

    protected override void OnPrepare(AudioFormat format)
    {
        _latencyFrames = Math.Max(0, (int)Math.Round(_lookAheadMilliseconds * format.SampleRate / 1_000.0));
        _bufferFrames = _latencyFrames + 1;
        _buffer = new float[checked(_bufferFrames * format.Channels)];
        _peakDequeValues = new float[_bufferFrames];
        _peakDequeSequences = new long[_bufferFrames];
        CeilingDecibels.Prepare(format.SampleRate);
        ReleaseMilliseconds.Prepare(format.SampleRate);
    }

    public override void Reset()
    {
        Array.Clear(_buffer);
        _writeFrame = 0;
        _gain = 1f;
        _dequeHead = 0;
        _dequeTail = 0;
        _dequeCount = 0;
        _sequence = 0;
        CeilingDecibels.Reset();
        ReleaseMilliseconds.Reset();
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        Span<float> outputFrame = stackalloc float[32];
        outputFrame = outputFrame[..channels];
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int writeOffset = _writeFrame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                _buffer[writeOffset + channel] = samples[index + channel];
            }

            int readFrame = _writeFrame + 1;
            if (readFrame >= _bufferFrames)
            {
                readFrame = 0;
            }

            int readOffset = readFrame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                outputFrame[channel] = _buffer[readOffset + channel];
            }

            float currentPeak = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                currentPeak = MathF.Max(currentPeak, MathF.Abs(samples[index + channel]));
            }

            long oldestRelevantSequence = _sequence - _latencyFrames;
            RemoveExpiredPeaks(oldestRelevantSequence);
            AddPeak(_sequence, currentPeak);
            float futurePeak = _dequeCount > 0 ? _peakDequeValues[_dequeHead] : 0f;

            float ceiling = AudioMath.DecibelsToLinear(CeilingDecibels.NextValue());
            float desiredGain = futurePeak > ceiling && futurePeak > 0f ? ceiling / futurePeak : 1f;
            float releaseSeconds = ReleaseMilliseconds.NextValue() / 1_000f;
            float releaseCoefficient = releaseSeconds <= 0f
                ? 0f
                : MathF.Exp(-1f / (releaseSeconds * Format.SampleRate));

            if (desiredGain < _gain)
            {
                _gain = desiredGain;
            }
            else
            {
                _gain = desiredGain + (releaseCoefficient * (_gain - desiredGain));
            }

            for (int channel = 0; channel < channels; channel++)
            {
                samples[index++] = outputFrame[channel] * _gain;
            }

            _writeFrame = readFrame;
            _sequence++;
        }
    }

    private void AddPeak(long sequence, float value)
    {
        while (_dequeCount > 0)
        {
            int back = _dequeTail - 1;
            if (back < 0)
            {
                back += _peakDequeValues.Length;
            }

            if (_peakDequeValues[back] > value)
            {
                break;
            }

            _dequeTail = back;
            _dequeCount--;
        }

        _peakDequeValues[_dequeTail] = value;
        _peakDequeSequences[_dequeTail] = sequence;
        _dequeTail++;
        if (_dequeTail >= _peakDequeValues.Length)
        {
            _dequeTail = 0;
        }

        _dequeCount++;
    }

    private void RemoveExpiredPeaks(long oldestRelevantSequence)
    {
        while (_dequeCount > 0 && _peakDequeSequences[_dequeHead] < oldestRelevantSequence)
        {
            _dequeHead++;
            if (_dequeHead >= _peakDequeValues.Length)
            {
                _dequeHead = 0;
            }

            _dequeCount--;
        }
    }
}
