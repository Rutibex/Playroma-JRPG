using Playroma.Sound.Core.Audio;

namespace Playroma.Sound.Dsp.Analysis;

/// <summary>
/// Allocation-free-after-prepare Hann-windowed spectrum analyzer.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private float[] _real = [];
    private float[] _imaginary = [];
    private float[] _window = [];
    private float[] _magnitudes = [];
    private AudioFormat _format;
    private bool _prepared;

    public int FftSize => _real.Length;
    public ReadOnlySpan<float> Magnitudes => _magnitudes;

    public void Prepare(AudioFormat format, int fftSize = 2048)
    {
        if (fftSize < 32 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fftSize), "FFT size must be a power of two of at least 32.");
        }

        _format = format;
        _real = new float[fftSize];
        _imaginary = new float[fftSize];
        _window = new float[fftSize];
        _magnitudes = new float[(fftSize / 2) + 1];

        for (int i = 0; i < fftSize; i++)
        {
            _window[i] = 0.5f - (0.5f * MathF.Cos(MathF.Tau * i / (fftSize - 1)));
        }

        _prepared = true;
    }

    public void Analyze(ReadOnlySpan<float> interleavedSamples, int frameCount, int channel = 0)
    {
        if (!_prepared)
        {
            throw new InvalidOperationException("The spectrum analyzer must be prepared before use.");
        }

        if ((uint)channel >= (uint)_format.Channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        int requiredSamples = checked(frameCount * _format.Channels);
        if (interleavedSamples.Length < requiredSamples)
        {
            throw new ArgumentException("The sample span is too small.", nameof(interleavedSamples));
        }

        Array.Clear(_real);
        Array.Clear(_imaginary);
        int copiedFrames = Math.Min(frameCount, _real.Length);
        int sourceStartFrame = Math.Max(0, frameCount - copiedFrames);
        int destinationStart = _real.Length - copiedFrames;

        for (int i = 0; i < copiedFrames; i++)
        {
            int sourceFrame = sourceStartFrame + i;
            _real[destinationStart + i] = interleavedSamples[(sourceFrame * _format.Channels) + channel] *
                                          _window[destinationStart + i];
        }

        FastFourierTransform.Forward(_real, _imaginary);
        float scale = 2f / _real.Length;
        for (int bin = 0; bin < _magnitudes.Length; bin++)
        {
            float real = _real[bin];
            float imaginary = _imaginary[bin];
            _magnitudes[bin] = MathF.Sqrt((real * real) + (imaginary * imaginary)) * scale;
        }
    }

    public float FrequencyForBin(int bin)
    {
        if ((uint)bin >= (uint)_magnitudes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bin));
        }

        return bin * _format.SampleRate / (float)_real.Length;
    }
}
