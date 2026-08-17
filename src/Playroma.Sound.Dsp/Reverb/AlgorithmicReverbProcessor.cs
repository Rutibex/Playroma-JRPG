using Playroma.Sound.Core.Audio;
using Playroma.Sound.Dsp.Delay;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Processing;

namespace Playroma.Sound.Dsp.Reverb;

/// <summary>
/// Compact Schroeder/Freeverb-style algorithmic reverb with pre-delay,
/// parallel damped combs, serial all-pass diffusion, and stereo width.
/// </summary>
public sealed class AlgorithmicReverbProcessor : AudioProcessorBase
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356];
    private static readonly int[] AllPassTunings = [556, 441];

    private readonly FractionalDelayLine _preDelay = new();
    private FeedbackComb[,] _combs = new FeedbackComb[0, 0];
    private DiffusionAllPass[,] _allPasses = new DiffusionAllPass[0, 0];

    public AlgorithmicReverbProcessor()
    {
        PreDelayMilliseconds = new AudioParameter(
            new ParameterMetadata("preDelay", "Pre-delay", "ms", 0f, 250f, 12f));
        RoomSize = new AudioParameter(
            new ParameterMetadata("roomSize", "Room Size", string.Empty, 0f, 1f, 0.65f));
        Damping = new AudioParameter(
            new ParameterMetadata("damping", "Damping", string.Empty, 0f, 1f, 0.35f));
        Wet = new AudioParameter(
            new ParameterMetadata("wet", "Wet", string.Empty, 0f, 1f, 0.3f));
        Dry = new AudioParameter(
            new ParameterMetadata("dry", "Dry", string.Empty, 0f, 1f, 1f));
        Width = new AudioParameter(
            new ParameterMetadata("width", "Stereo Width", string.Empty, 0f, 1f, 1f));
    }

    public AudioParameter PreDelayMilliseconds { get; }
    public AudioParameter RoomSize { get; }
    public AudioParameter Damping { get; }
    public AudioParameter Wet { get; }
    public AudioParameter Dry { get; }
    public AudioParameter Width { get; }

    protected override void OnPrepare(AudioFormat format)
    {
        int maximumPreDelayFrames = Math.Max(1, (int)Math.Ceiling(0.25 * format.SampleRate));
        _preDelay.Prepare(format.Channels, maximumPreDelayFrames);
        _combs = new FeedbackComb[format.Channels, CombTunings.Length];
        _allPasses = new DiffusionAllPass[format.Channels, AllPassTunings.Length];
        double scale = format.SampleRate / 44_100.0;

        for (int channel = 0; channel < format.Channels; channel++)
        {
            int spread = channel * 23;
            for (int i = 0; i < CombTunings.Length; i++)
            {
                int length = Math.Max(1, (int)Math.Round((CombTunings[i] + spread) * scale));
                _combs[channel, i] = new FeedbackComb(length);
            }

            for (int i = 0; i < AllPassTunings.Length; i++)
            {
                int length = Math.Max(1, (int)Math.Round((AllPassTunings[i] + spread) * scale));
                _allPasses[channel, i] = new DiffusionAllPass(length, 0.5f);
            }
        }

        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Prepare(format.SampleRate);
        }
    }

    public override void Reset()
    {
        _preDelay.Reset();
        foreach (FeedbackComb comb in _combs)
        {
            comb?.Reset();
        }

        foreach (DiffusionAllPass allPass in _allPasses)
        {
            allPass?.Reset();
        }

        foreach (AudioParameter parameter in Parameters())
        {
            parameter.Reset();
        }
    }

    protected override void ProcessCore(Span<float> samples, int frameCount)
    {
        int channels = Format.Channels;
        Span<float> inputFrame = stackalloc float[32];
        Span<float> wetFrame = stackalloc float[32];
        inputFrame = inputFrame[..channels];
        wetFrame = wetFrame[..channels];
        int index = 0;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float preDelayFrames = Math.Clamp(
                PreDelayMilliseconds.NextValue() * Format.SampleRate / 1_000f,
                0f,
                _preDelay.MaximumDelayFrames);
            float roomSize = Math.Clamp(RoomSize.NextValue(), 0f, 1f);
            float feedback = 0.2f + (roomSize * 0.76f);
            float damping = Math.Clamp(Damping.NextValue(), 0f, 0.99f);
            float wet = Math.Clamp(Wet.NextValue(), 0f, 1f);
            float dry = Math.Clamp(Dry.NextValue(), 0f, 1f);
            float width = Math.Clamp(Width.NextValue(), 0f, 1f);

            for (int channel = 0; channel < channels; channel++)
            {
                inputFrame[channel] = samples[index + channel];
            }

            for (int channel = 0; channel < channels; channel++)
            {
                float reverberatorInput = preDelayFrames < 0.5f
                    ? inputFrame[channel]
                    : _preDelay.Read(channel, preDelayFrames);
                reverberatorInput *= 0.08f;

                float accumulated = 0f;
                for (int comb = 0; comb < CombTunings.Length; comb++)
                {
                    accumulated += _combs[channel, comb].Process(reverberatorInput, feedback, damping);
                }

                accumulated *= 1f / CombTunings.Length;
                for (int allPass = 0; allPass < AllPassTunings.Length; allPass++)
                {
                    accumulated = _allPasses[channel, allPass].Process(accumulated);
                }

                wetFrame[channel] = accumulated;
            }

            _preDelay.WriteFrame(inputFrame);

            if (channels == 2)
            {
                float wetOne = wet * ((width * 0.5f) + 0.5f);
                float wetTwo = wet * ((1f - width) * 0.5f);
                float leftWet = (wetFrame[0] * wetOne) + (wetFrame[1] * wetTwo);
                float rightWet = (wetFrame[1] * wetOne) + (wetFrame[0] * wetTwo);
                samples[index] = (inputFrame[0] * dry) + leftWet;
                samples[index + 1] = (inputFrame[1] * dry) + rightWet;
                index += 2;
            }
            else
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    samples[index++] = (inputFrame[channel] * dry) + (wetFrame[channel] * wet);
                }
            }
        }
    }

    private IEnumerable<AudioParameter> Parameters()
    {
        yield return PreDelayMilliseconds;
        yield return RoomSize;
        yield return Damping;
        yield return Wet;
        yield return Dry;
        yield return Width;
    }

    private sealed class FeedbackComb
    {
        private readonly float[] _buffer;
        private int _index;
        private float _filterStore;

        public FeedbackComb(int length)
        {
            _buffer = new float[length];
        }

        public float Process(float input, float feedback, float damping)
        {
            float output = _buffer[_index];
            _filterStore = (output * (1f - damping)) + (_filterStore * damping);
            _buffer[_index] = input + (_filterStore * feedback);
            _index++;
            if (_index >= _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
            _filterStore = 0f;
        }
    }

    private sealed class DiffusionAllPass
    {
        private readonly float[] _buffer;
        private readonly float _feedback;
        private int _index;

        public DiffusionAllPass(int length, float feedback)
        {
            _buffer = new float[length];
            _feedback = feedback;
        }

        public float Process(float input)
        {
            float buffered = _buffer[_index];
            float output = -input + buffered;
            _buffer[_index] = input + (buffered * _feedback);
            _index++;
            if (_index >= _buffer.Length)
            {
                _index = 0;
            }

            return output;
        }

        public void Reset()
        {
            Array.Clear(_buffer);
            _index = 0;
        }
    }
}
