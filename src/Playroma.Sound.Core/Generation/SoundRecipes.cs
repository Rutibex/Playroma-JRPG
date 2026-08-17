using Playroma.Sound.Core.Envelopes;
using Playroma.Sound.Core.Nodes;

namespace Playroma.Sound.Core.Generation;

/// <summary>
/// Small code-defined recipes used by demos and as examples for integrators.
/// These are intentionally code-defined; use the Graph project for serializable definitions.
/// </summary>
public static class SoundRecipes
{
    public static IAudioNode CreateUiConfirm()
    {
        var root = new MixerNode();

        root.Add(
            new EnvelopeNode(
                new OscillatorNode(Waveform.Sine, 880, 0.45f),
                new AdsrEnvelope(0.002, 0.025, 0.45f, 0.035, 0.07)),
            1f);

        root.Add(
            new OffsetNode(
                new EnvelopeNode(
                    new OscillatorNode(Waveform.Sine, 1320, 0.32f),
                    new AdsrEnvelope(0.002, 0.018, 0.35f, 0.02, 0.06)),
                0.045),
            1f);

        return root;
    }

    public static IAudioNode CreateImpact(ulong seed = 1234UL)
    {
        var root = new MixerNode();

        var lowBodyEnvelope = new SegmentEnvelope(
        [
            new EnvelopePoint(0.000, 0f, EnvelopeCurve.SmoothStep),
            new EnvelopePoint(0.003, 1f, EnvelopeCurve.Exponential),
            new EnvelopePoint(0.080, 0.42f, EnvelopeCurve.Exponential),
            new EnvelopePoint(0.450, 0f)
        ]);

        var noiseEnvelope = new SegmentEnvelope(
        [
            new EnvelopePoint(0.000, 1f, EnvelopeCurve.Exponential),
            new EnvelopePoint(0.018, 0.33f, EnvelopeCurve.Exponential),
            new EnvelopePoint(0.180, 0f)
        ]);

        root.Add(
            new EnvelopeNode(
                new OscillatorNode(Waveform.Sine, 74, 0.95f),
                lowBodyEnvelope),
            0.85f);

        root.Add(
            new EnvelopeNode(
                new NoiseNode(NoiseColor.Brown, seed, 0.75f),
                noiseEnvelope),
            0.60f);

        root.Add(
            new EnvelopeNode(
                new NoiseNode(NoiseColor.White, unchecked(seed + 1UL), 0.45f),
                new AdsrEnvelope(0.0, 0.012, 0.08f, 0.010, 0.055)),
            0.35f);

        return root;
    }

    public static IAudioNode CreateEngineDrone(ulong seed = 9001UL)
    {
        var root = new MixerNode();

        root.Add(new PanNode(new OscillatorNode(Waveform.Saw, 48, 0.18f), -0.2f));
        root.Add(new PanNode(new OscillatorNode(Waveform.Saw, 48.7, 0.16f), 0.2f));
        root.Add(new OscillatorNode(Waveform.Sine, 24, 0.28f));
        root.Add(new NoiseNode(NoiseColor.Brown, seed, 0.05f));

        return root;
    }
}
