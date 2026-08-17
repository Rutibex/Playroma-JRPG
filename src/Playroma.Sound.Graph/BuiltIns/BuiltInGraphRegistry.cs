using Playroma.Sound.Core.Envelopes;
using Playroma.Sound.Core.Generation;
using Playroma.Sound.Core.Nodes;
using Playroma.Sound.Dsp.Delay;
using Playroma.Sound.Dsp.Distortion;
using Playroma.Sound.Dsp.Dynamics;
using Playroma.Sound.Dsp.Filters;
using Playroma.Sound.Dsp.Parameters;
using Playroma.Sound.Dsp.Reverb;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Graph.Utility;

namespace Playroma.Sound.Graph.BuiltIns;

public static class BuiltInGraphRegistry
{
    private static readonly GraphPortDescriptor AudioInput = new("in", "Audio In", GraphPortDirection.Input, GraphPortType.AudioAnyChannels, true);
    private static readonly GraphPortDescriptor AudioOutput = new("out", "Audio Out", GraphPortDirection.Output, GraphPortType.AudioAnyChannels);
    private static readonly GraphPortDescriptor MixerInput = new("in", "Audio Inputs", GraphPortDirection.Input, GraphPortType.AudioAnyChannels, true, GraphPortCardinality.Multiple);

    public static GraphNodeRegistry CreateDefault()
    {
        var registry = new GraphNodeRegistry();
        RegisterCore(registry);
        RegisterFilters(registry);
        RegisterDelay(registry);
        RegisterDistortion(registry);
        RegisterDynamics(registry);
        RegisterReverb(registry);
        return registry;
    }

    private static void RegisterCore(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.oscillator", "Oscillator", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("frequencyHz", "Frequency", 440, 0, 40_000, "Hz"),
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Enum<Waveform>("waveform", "Waveform", Waveform.Sine),
                Number("pulseWidth", "Pulse Width", 0.5, 0.01, 0.99),
                Number("durationSeconds", "Duration", 0, 0, 600, "s", false),
                Number("attackSeconds", "Attack", 0.005, 0, 60, "s", false),
                Number("releaseSeconds", "Release", 0.05, 0, 60, "s", false)),
            (definition, context) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.oscillator").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var oscillator = new OscillatorNode(
                    ParseEnum(reader.GetString("waveform"), Waveform.Sine),
                    reader.GetDouble("frequencyHz"),
                    reader.GetFloat("amplitude"),
                    pulseWidth: reader.GetFloat("pulseWidth"));
                double duration = reader.GetDouble("durationSeconds");
                IAudioNode source = WrapFinite(oscillator, duration, reader.GetDouble("attackSeconds"), reader.GetDouble("releaseSeconds"));
                var runtime = new AudioSourceRuntimeNode(descriptor, source, duration <= 0);
                runtime.AddParameter("frequencyHz", 0, 40_000, value => oscillator.FrequencyHz = value);
                runtime.AddParameter("amplitude", 0, 4, value => oscillator.Amplitude = value);
                runtime.AddParameter("pulseWidth", 0.01f, 0.99f, value => oscillator.PulseWidth = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.gesture-oscillator", "Gesture Oscillator", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("rootFrequencyHz", "Root Frequency", 440, 0, 40_000, "Hz"),
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Enum<Waveform>("waveform", "Waveform", Waveform.Sine),
                Number("pulseWidth", "Pulse Width", 0.5, 0.01, 0.99),
                Number("durationSeconds", "Duration", 0.5, 0, 600, "s", false),
                Number("attackSeconds", "Attack", 0.002, 0, 60, "s", false),
                Number("releaseSeconds", "Release", 0.05, 0, 60, "s", false),
                Text("pitchContour", "Pitch Contour", "0:0:SmoothStep;1:0:SmoothStep"),
                Text("amplitudeContour", "Amplitude Contour", "0:1:SmoothStep;1:1:SmoothStep"),
                Number("vibratoDepthSemitones", "Vibrato Depth", 0, 0, 12, "st"),
                Number("vibratoRateHz", "Vibrato Rate", 5.5, 0.01, 40, "Hz"),
                Number("vibratoDelaySeconds", "Vibrato Delay", 0, 0, 60, "s", false),
                Number("tremoloDepth", "Tremolo Depth", 0, 0, 1),
                Number("tremoloRateHz", "Tremolo Rate", 5, 0.01, 40, "Hz"),
                Number("driftDepthSemitones", "Organic Drift", 0, 0, 12, "st"),
                Number("driftRateHz", "Drift Rate", 1.4, 0.01, 20, "Hz"),
                Enum<PitchQuantizationMode>("quantization", "Pitch Quantization", PitchQuantizationMode.None),
                Seed("seed", "Seed")),
            (definition, context) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.gesture-oscillator").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                ulong configuredSeed = definition.Parameters.TryGetValue("seed", out GraphValue seedValue)
                    ? seedValue.AsUInt64()
                    : 0;
                ulong seed = definition.LocalSeed ?? (configuredSeed == 0 ? context.DerivedSeed : configuredSeed);
                double duration = reader.GetDouble("durationSeconds");
                var oscillator = new GestureOscillatorNode(
                    ParseEnum(reader.GetString("waveform"), Waveform.Sine),
                    reader.GetDouble("rootFrequencyHz"),
                    reader.GetFloat("amplitude"),
                    duration,
                    GestureCurve.Parse(reader.GetString("pitchContour")),
                    GestureCurve.Parse(reader.GetString("amplitudeContour"), 1),
                    seed)
                {
                    PulseWidth = reader.GetFloat("pulseWidth"),
                    AttackSeconds = reader.GetDouble("attackSeconds"),
                    ReleaseSeconds = reader.GetDouble("releaseSeconds"),
                    VibratoDepthSemitones = reader.GetFloat("vibratoDepthSemitones"),
                    VibratoRateHz = reader.GetFloat("vibratoRateHz"),
                    VibratoDelaySeconds = reader.GetDouble("vibratoDelaySeconds"),
                    TremoloDepth = reader.GetFloat("tremoloDepth"),
                    TremoloRateHz = reader.GetFloat("tremoloRateHz"),
                    DriftDepthSemitones = reader.GetFloat("driftDepthSemitones"),
                    DriftRateHz = reader.GetFloat("driftRateHz"),
                    Quantization = ParseEnum(reader.GetString("quantization"), PitchQuantizationMode.None)
                };
                var runtime = new AudioSourceRuntimeNode(descriptor, oscillator, duration <= 0);
                runtime.AddParameter("rootFrequencyHz", 0, 40_000, value => oscillator.RootFrequencyHz = value);
                runtime.AddParameter("amplitude", 0, 4, value => oscillator.Amplitude = value);
                runtime.AddParameter("pulseWidth", 0.01f, 0.99f, value => oscillator.PulseWidth = value);
                runtime.AddParameter("vibratoDepthSemitones", 0, 12, value => oscillator.VibratoDepthSemitones = value);
                runtime.AddParameter("vibratoRateHz", 0.01f, 40, value => oscillator.VibratoRateHz = value);
                runtime.AddParameter("tremoloDepth", 0, 1, value => oscillator.TremoloDepth = value);
                runtime.AddParameter("tremoloRateHz", 0.01f, 40, value => oscillator.TremoloRateHz = value);
                runtime.AddParameter("driftDepthSemitones", 0, 12, value => oscillator.DriftDepthSemitones = value);
                runtime.AddParameter("driftRateHz", 0.01f, 20, value => oscillator.DriftRateHz = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.hybrid-synth", "Hybrid Synth", GraphNodeCategory.Generator, [], [AudioOutput],
                Enum<HybridSynthesisMode>("mode", "Synthesis Mode", HybridSynthesisMode.Basic),
                Enum<Waveform>("waveform", "Waveform", Waveform.Sine),
                Number("frequencyHz", "Frequency", 220, 5, 40_000, "Hz"),
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Number("durationSeconds", "Duration", 0.5, 0, 600, "s", false),
                Number("attackSeconds", "Attack", 0.002, 0, 60, "s", false),
                Number("releaseSeconds", "Release", 0.08, 0, 60, "s", false),
                Number("brightness", "Brightness", 0.55, 0, 1),
                Number("timbre", "Timbre", 0.5, 0, 1),
                Number("modulation", "Modulation", 0.35, 0, 1),
                Number("feedback", "Feedback", 0.25, 0, 1),
                Number("harmonics", "Harmonics / Voices", 8, 1, 32),
                Number("damping", "Damping", 0.45, 0, 1),
                Number("spread", "Spread", 0.2, 0, 1),
                Number("pulseWidth", "Pulse Width", 0.5, 0.01, 0.99),
                Number("pitchSweepSemitones", "Pitch Sweep", 0, -48, 48, "st"),
                Seed("seed", "Seed")),
            (definition, context) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.hybrid-synth").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                ulong configuredSeed = definition.Parameters.TryGetValue("seed", out GraphValue seedValue)
                    ? seedValue.AsUInt64()
                    : 0;
                ulong seed = definition.LocalSeed ?? (configuredSeed == 0 ? context.DerivedSeed : configuredSeed);
                double duration = reader.GetDouble("durationSeconds");
                var synth = new HybridSynthNode(
                    ParseEnum(reader.GetString("mode"), HybridSynthesisMode.Basic),
                    ParseEnum(reader.GetString("waveform"), Waveform.Sine),
                    reader.GetDouble("frequencyHz"),
                    reader.GetFloat("amplitude"),
                    duration,
                    seed)
                {
                    AttackSeconds = reader.GetDouble("attackSeconds"),
                    ReleaseSeconds = reader.GetDouble("releaseSeconds"),
                    Brightness = reader.GetFloat("brightness"),
                    Timbre = reader.GetFloat("timbre"),
                    Modulation = reader.GetFloat("modulation"),
                    Feedback = reader.GetFloat("feedback"),
                    Harmonics = reader.GetInt32("harmonics"),
                    Damping = reader.GetFloat("damping"),
                    Spread = reader.GetFloat("spread"),
                    PulseWidth = reader.GetFloat("pulseWidth"),
                    PitchSweepSemitones = reader.GetFloat("pitchSweepSemitones")
                };
                var runtime = new AudioSourceRuntimeNode(descriptor, synth, duration <= 0);
                runtime.AddParameter("frequencyHz", 5, 40_000, value => synth.FrequencyHz = value);
                runtime.AddParameter("amplitude", 0, 4, value => synth.Amplitude = value);
                runtime.AddParameter("brightness", 0, 1, value => synth.Brightness = value);
                runtime.AddParameter("timbre", 0, 1, value => synth.Timbre = value);
                runtime.AddParameter("modulation", 0, 1, value => synth.Modulation = value);
                runtime.AddParameter("feedback", 0, 1, value => synth.Feedback = value);
                runtime.AddParameter("harmonics", 1, 32, value => synth.Harmonics = Math.Clamp((int)Math.Round(value), 1, 32));
                runtime.AddParameter("damping", 0, 1, value => synth.Damping = value);
                runtime.AddParameter("spread", 0, 1, value => synth.Spread = value);
                runtime.AddParameter("pulseWidth", 0.01f, 0.99f, value => synth.PulseWidth = value);
                runtime.AddParameter("pitchSweepSemitones", -48, 48, value => synth.PitchSweepSemitones = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.tone-sequence", "Tone Sequence", GraphNodeCategory.Generator, [], [AudioOutput],
                Text("program", "Note Program", "0|0.15|0|1;0.15|0.15|4|0.9"),
                Enum<HybridSynthesisMode>("synthesisMode", "Synthesis Mode", HybridSynthesisMode.Basic),
                Enum<Waveform>("waveform", "Waveform", Waveform.Sine),
                Number("rootFrequencyHz", "Root Frequency", 440, 5, 40_000, "Hz"),
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Number("attackSeconds", "Attack", 0.002, 0, 10, "s", false),
                Number("releaseSeconds", "Release", 0.04, 0, 10, "s", false),
                Number("pulseWidth", "Pulse Width", 0.5, 0.01, 0.99),
                Number("vibratoDepthSemitones", "Vibrato Depth", 0, 0, 12, "st"),
                Number("vibratoRateHz", "Vibrato Rate", 5.5, 0.01, 40, "Hz"),
                Number("quantizeSemitones", "Pitch Quantization", 0, 0, 12, "st", false),
                Number("brightness", "Brightness", 0.55, 0, 1),
                Number("timbre", "Timbre", 0.5, 0, 1),
                Number("modulation", "Modulation", 0.35, 0, 1),
                Number("damping", "Damping", 0.45, 0, 1),
                Number("harmonics", "Harmonics", 8, 1, 16)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.tone-sequence").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var sequence = new ToneSequenceNode(
                    reader.GetString("program"),
                    ParseEnum(reader.GetString("synthesisMode"), HybridSynthesisMode.Basic),
                    ParseEnum(reader.GetString("waveform"), Waveform.Sine),
                    reader.GetDouble("rootFrequencyHz"),
                    reader.GetFloat("amplitude"),
                    reader.GetDouble("attackSeconds"),
                    reader.GetDouble("releaseSeconds"),
                    reader.GetFloat("pulseWidth"))
                {
                    VibratoDepthSemitones = reader.GetFloat("vibratoDepthSemitones"),
                    VibratoRateHz = reader.GetFloat("vibratoRateHz"),
                    QuantizeSemitones = reader.GetFloat("quantizeSemitones"),
                    Brightness = reader.GetFloat("brightness"),
                    Timbre = reader.GetFloat("timbre"),
                    Modulation = reader.GetFloat("modulation"),
                    Damping = reader.GetFloat("damping"),
                    Harmonics = reader.GetInt32("harmonics")
                };
                var runtime = new AudioSourceRuntimeNode(descriptor, sequence, false);
                runtime.AddParameter("rootFrequencyHz", 5, 40_000, value => sequence.RootFrequencyHz = value);
                runtime.AddParameter("amplitude", 0, 4, value => sequence.Amplitude = value);
                runtime.AddParameter("pulseWidth", 0.01f, 0.99f, value => sequence.PulseWidth = value);
                runtime.AddParameter("vibratoDepthSemitones", 0, 12, value => sequence.VibratoDepthSemitones = value);
                runtime.AddParameter("vibratoRateHz", 0.01f, 40, value => sequence.VibratoRateHz = value);
                runtime.AddParameter("quantizeSemitones", 0, 12, value => sequence.QuantizeSemitones = value);
                runtime.AddParameter("brightness", 0, 1, value => sequence.Brightness = value);
                runtime.AddParameter("timbre", 0, 1, value => sequence.Timbre = value);
                runtime.AddParameter("modulation", 0, 1, value => sequence.Modulation = value);
                runtime.AddParameter("damping", 0, 1, value => sequence.Damping = value);
                runtime.AddParameter("harmonics", 1, 16, value => sequence.Harmonics = Math.Clamp((int)Math.Round(value), 1, 16));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.procedural-texture", "Procedural Texture", GraphNodeCategory.Generator, [], [AudioOutput],
                Enum<ProceduralTextureKind>("kind", "Texture Kind", ProceduralTextureKind.Wind),
                Number("durationSeconds", "Duration", 2, 0, 600, "s", false),
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Number("intensity", "Intensity", 0.5, 0, 1),
                Number("density", "Density", 0.5, 0, 1),
                Number("brightness", "Brightness", 0.5, 0, 1),
                Number("movement", "Movement", 0.5, 0, 1),
                Number("baseFrequencyHz", "Base Frequency", 120, 5, 20_000, "Hz"),
                Number("attackSeconds", "Attack", 0.05, 0, 60, "s", false),
                Number("releaseSeconds", "Release", 0.2, 0, 60, "s", false),
                Seed("seed", "Seed")),
            (definition, context) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.procedural-texture").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                ulong configuredSeed = definition.Parameters.TryGetValue("seed", out GraphValue seedValue)
                    ? seedValue.AsUInt64()
                    : 0;
                ulong seed = definition.LocalSeed ?? (configuredSeed == 0 ? context.DerivedSeed : configuredSeed);
                double duration = reader.GetDouble("durationSeconds");
                var texture = new ProceduralTextureNode(
                    ParseEnum(reader.GetString("kind"), ProceduralTextureKind.Wind),
                    duration,
                    seed)
                {
                    Amplitude = reader.GetFloat("amplitude"),
                    Intensity = reader.GetFloat("intensity"),
                    Density = reader.GetFloat("density"),
                    Brightness = reader.GetFloat("brightness"),
                    Movement = reader.GetFloat("movement"),
                    BaseFrequencyHz = reader.GetDouble("baseFrequencyHz"),
                    AttackSeconds = reader.GetDouble("attackSeconds"),
                    ReleaseSeconds = reader.GetDouble("releaseSeconds")
                };
                var runtime = new AudioSourceRuntimeNode(descriptor, texture, duration <= 0);
                runtime.AddParameter("amplitude", 0, 4, value => texture.Amplitude = value);
                runtime.AddParameter("intensity", 0, 1, value => texture.Intensity = value);
                runtime.AddParameter("density", 0, 1, value => texture.Density = value);
                runtime.AddParameter("brightness", 0, 1, value => texture.Brightness = value);
                runtime.AddParameter("movement", 0, 1, value => texture.Movement = value);
                runtime.AddParameter("baseFrequencyHz", 5, 20_000, value => texture.BaseFrequencyHz = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.handheld-pulse", "Classic Handheld Pulse", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("period", "11-bit Period", 1_600, 0, 2_047),
                Number("amplitude", "4-bit Volume", 0.8, 0, 1),
                Enum<HandheldPulseDuty>("duty", "Duty Pattern", HandheldPulseDuty.Duty50),
                Number("durationSeconds", "Duration", 0.25, 0, 600, "s", false),
                Number("envelopeSweep", "Envelope Sweep", -2, -7, 7, "64 Hz ticks", false),
                Number("periodSlide", "Period Slide", 0, -2_047, 2_047),
                Number("sweepRateHz", "Sweep Clock", 128, 1, 128, "Hz", false)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.handheld-pulse").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                double duration = reader.GetDouble("durationSeconds");
                var pulse = new HandheldPulseNode(
                    reader.GetInt32("period"),
                    reader.GetFloat("amplitude"),
                    ParseEnum(reader.GetString("duty"), HandheldPulseDuty.Duty50),
                    duration,
                    reader.GetInt32("envelopeSweep"),
                    reader.GetInt32("periodSlide"),
                    reader.GetDouble("sweepRateHz"));
                var runtime = new AudioSourceRuntimeNode(descriptor, pulse, duration <= 0);
                runtime.AddParameter("period", 0, 2_047, value => pulse.Period = (int)Math.Round(value));
                runtime.AddParameter("amplitude", 0, 1, value => pulse.Amplitude = value);
                runtime.AddParameter("periodSlide", -2_047, 2_047, value => pulse.PeriodSlide = (int)Math.Round(value));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.handheld-noise", "Classic Handheld LFSR Noise", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("polynomial", "NR43 Polynomial", 0x35, 0, 255, "byte", false),
                Number("amplitude", "4-bit Volume", 0.5, 0, 1),
                Number("durationSeconds", "Duration", 0.2, 0, 600, "s", false),
                Number("envelopeSweep", "Envelope Sweep", -2, -7, 7, "64 Hz ticks", false)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.handheld-noise").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                double duration = reader.GetDouble("durationSeconds");
                var noise = new HandheldNoiseNode(
                    (byte)Math.Clamp(reader.GetInt32("polynomial"), 0, 255),
                    reader.GetFloat("amplitude"),
                    duration,
                    reader.GetInt32("envelopeSweep"));
                var runtime = new AudioSourceRuntimeNode(descriptor, noise, duration <= 0);
                runtime.AddParameter("amplitude", 0, 1, value => noise.Amplitude = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.handheld-cry-sequence", "Classic Handheld Cry Program", GraphNodeCategory.Generator, [], [AudioOutput],
                Text("pulse1Program", "Pulse 1 Program", "0.25,1600,1600,15,0,Duty50"),
                Text("pulse2Program", "Pulse 2 Program", "0.25,1500,1500,10,0,Duty25"),
                Text("noiseProgram", "Noise Program", "0.25,53,7,0"),
                Number("masterAmplitude", "Master Amplitude", 0.9, 0, 2),
                Number("noiseMix", "Noise Mix", 0.55, 0, 2),
                Number("pitchShiftSemitones", "Pitch Shift", 0, -24, 24, "st")),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.handheld-cry-sequence").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var sequence = new HandheldCrySequenceNode(
                    reader.GetString("pulse1Program"),
                    reader.GetString("pulse2Program"),
                    reader.GetString("noiseProgram"),
                    reader.GetFloat("masterAmplitude"),
                    reader.GetFloat("noiseMix"),
                    reader.GetFloat("pitchShiftSemitones"));
                var runtime = new AudioSourceRuntimeNode(descriptor, sequence, false);
                runtime.AddParameter("masterAmplitude", 0, 2, value => sequence.MasterAmplitude = value);
                runtime.AddParameter("noiseMix", 0, 2, value => sequence.NoiseMix = value);
                runtime.AddParameter("pitchShiftSemitones", -24, 24, value => sequence.PitchShiftSemitones = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.noise", "Noise", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("amplitude", "Amplitude", 0.5, 0, 4),
                Enum<NoiseColor>("color", "Colour", NoiseColor.White),
                Seed("seed", "Seed"),
                Number("durationSeconds", "Duration", 0, 0, 600, "s", false),
                Number("attackSeconds", "Attack", 0.001, 0, 60, "s", false),
                Number("releaseSeconds", "Release", 0.05, 0, 60, "s", false)),
            (definition, context) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.noise").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                ulong configuredSeed = definition.Parameters.TryGetValue("seed", out GraphValue seedValue)
                    ? seedValue.AsUInt64()
                    : 0;
                ulong seed = definition.LocalSeed ?? (configuredSeed == 0 ? context.DerivedSeed : configuredSeed);
                var noise = new NoiseNode(ParseEnum(reader.GetString("color"), NoiseColor.White), seed, reader.GetFloat("amplitude"));
                double duration = reader.GetDouble("durationSeconds");
                IAudioNode source = WrapFinite(noise, duration, reader.GetDouble("attackSeconds"), reader.GetDouble("releaseSeconds"));
                var runtime = new AudioSourceRuntimeNode(descriptor, source, duration <= 0);
                runtime.AddParameter("amplitude", 0, 4, value => noise.Amplitude = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.impulse", "Impulse", GraphNodeCategory.Generator, [], [AudioOutput],
                Number("amplitude", "Amplitude", 1, -4, 4)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.impulse").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var impulse = new ImpulseAudioNode { Amplitude = reader.GetFloat("amplitude") };
                var runtime = new AudioSourceRuntimeNode(descriptor, impulse, false);
                runtime.AddParameter("amplitude", -4, 4, value => impulse.Amplitude = value);
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.silence", "Silence", GraphNodeCategory.Generator, [], [AudioOutput]),
            (_, _) => new AudioSourceRuntimeNode(registry.GetRequiredFactory("core.silence").Descriptor, new SilenceNode(), true)));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.gain", "Gain", GraphNodeCategory.Router, [AudioInput], [AudioOutput],
                Number("gain", "Gain", 1, 0, 8)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.gain").Descriptor;
                return new GainRuntimeNode(descriptor, new GraphParameterReader(definition, descriptor).GetFloat("gain"));
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.pan", "Stereo Pan", GraphNodeCategory.Router, [AudioInput], [AudioOutput],
                Number("pan", "Pan", 0, -1, 1)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.pan").Descriptor;
                return new PanRuntimeNode(descriptor, new GraphParameterReader(definition, descriptor).GetFloat("pan"));
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.mixer", "Mixer", GraphNodeCategory.Router, [MixerInput], [AudioOutput],
                Number("gain", "Master Gain", 1, 0, 8)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.mixer").Descriptor;
                return new MixerRuntimeNode(descriptor, new GraphParameterReader(definition, descriptor).GetFloat("gain"));
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.offset", "Time Offset", GraphNodeCategory.Router, [AudioInput], [AudioOutput],
                Number("delaySeconds", "Delay", 0, 0, 600, "s", false)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("core.offset").Descriptor;
                return new OffsetRuntimeNode(descriptor, new GraphParameterReader(definition, descriptor).GetDouble("delaySeconds"));
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("core.output", "Master Output", GraphNodeCategory.Output, [AudioInput], [AudioOutput]),
            (_, _) => new PassThroughRuntimeNode(registry.GetRequiredFactory("core.output").Descriptor)));
    }

    private static void RegisterFilters(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.biquad", "Biquad Filter", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Enum<BiquadFilterType>("filterType", "Mode", BiquadFilterType.LowPass),
                Number("frequencyHz", "Frequency", 4_000, 10, 40_000, "Hz"),
                Number("q", "Q", 0.707, 0.05, 50),
                Number("gainDecibels", "Gain", 0, -60, 60, "dB")),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.biquad").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new BiquadFilterProcessor(ParseEnum(reader.GetString("filterType"), BiquadFilterType.LowPass));
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "frequencyHz", processor.Frequency, reader.GetFloat("frequencyHz"));
                Bind(runtime, "q", processor.Q, reader.GetFloat("q"));
                Bind(runtime, "gainDecibels", processor.GainDecibels, reader.GetFloat("gainDecibels"));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.state-variable", "State Variable Filter", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Enum<StateVariableFilterMode>("mode", "Mode", StateVariableFilterMode.LowPass),
                Number("frequencyHz", "Frequency", 4_000, 10, 40_000, "Hz"),
                Number("resonance", "Resonance", 0.707, 0.1, 20),
                Number("drive", "Drive", 0, 0, 4)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.state-variable").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new StateVariableFilterProcessor(ParseEnum(reader.GetString("mode"), StateVariableFilterMode.LowPass));
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "frequencyHz", processor.Frequency, reader.GetFloat("frequencyHz"));
                Bind(runtime, "resonance", processor.Resonance, reader.GetFloat("resonance"));
                Bind(runtime, "drive", processor.Drive, reader.GetFloat("drive"));
                return runtime;
            }));
    }

    private static void RegisterDelay(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.delay", "Delay", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("maximumDelaySeconds", "Maximum Delay", 4, 0.01, 30, "s", false),
                Number("delayMilliseconds", "Delay", 250, 0, 30_000, "ms"),
                Number("feedback", "Feedback", 0.25, -0.99, 0.99),
                Number("wet", "Wet", 0.3, 0, 1),
                Number("dry", "Dry", 1, 0, 1),
                Number("tailSeconds", "Tail Limit", 4, 0, 60, "s", false)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.delay").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                float requestedDelayMilliseconds = reader.GetFloat("delayMilliseconds");
                double maximumDelaySeconds = Math.Max(reader.GetDouble("maximumDelaySeconds"), requestedDelayMilliseconds / 1_000.0);
                var processor = new DelayProcessor(maximumDelaySeconds);
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor, reader.GetDouble("tailSeconds"));
                Bind(runtime, "delayMilliseconds", processor.DelayMilliseconds, requestedDelayMilliseconds);
                Bind(runtime, "feedback", processor.Feedback, reader.GetFloat("feedback"));
                Bind(runtime, "wet", processor.Wet, reader.GetFloat("wet"));
                Bind(runtime, "dry", processor.Dry, reader.GetFloat("dry"));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.chorus", "Chorus / Flanger", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("baseDelayMilliseconds", "Base Delay", 8, 0.1, 50, "ms"),
                Number("depthMilliseconds", "Depth", 3, 0, 30, "ms"),
                Number("rateHz", "Rate", 0.3, 0.01, 20, "Hz"),
                Number("feedback", "Feedback", 0.1, -0.95, 0.95),
                Number("wet", "Wet", 0.4, 0, 1),
                Number("dry", "Dry", 1, 0, 1)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.chorus").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new ModulatedDelayProcessor();
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor, 0.25);
                Bind(runtime, "baseDelayMilliseconds", processor.BaseDelayMilliseconds, reader.GetFloat("baseDelayMilliseconds"));
                Bind(runtime, "depthMilliseconds", processor.DepthMilliseconds, reader.GetFloat("depthMilliseconds"));
                Bind(runtime, "rateHz", processor.RateHz, reader.GetFloat("rateHz"));
                Bind(runtime, "feedback", processor.Feedback, reader.GetFloat("feedback"));
                Bind(runtime, "wet", processor.Wet, reader.GetFloat("wet"));
                Bind(runtime, "dry", processor.Dry, reader.GetFloat("dry"));
                return runtime;
            }));
    }

    private static void RegisterDistortion(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.waveshaper", "Waveshaper", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Enum<WaveshaperMode>("mode", "Mode", WaveshaperMode.HyperbolicTangent),
                Number("driveDecibels", "Drive", 6, -24, 48, "dB"),
                Number("bias", "Bias", 0, -1, 1),
                Number("mix", "Mix", 1, 0, 1),
                Number("outputDecibels", "Output", 0, -48, 24, "dB")),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.waveshaper").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new WaveshaperProcessor(ParseEnum(reader.GetString("mode"), WaveshaperMode.HyperbolicTangent));
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "driveDecibels", processor.DriveDecibels, reader.GetFloat("driveDecibels"));
                Bind(runtime, "bias", processor.Bias, reader.GetFloat("bias"));
                Bind(runtime, "mix", processor.Mix, reader.GetFloat("mix"));
                Bind(runtime, "outputDecibels", processor.OutputDecibels, reader.GetFloat("outputDecibels"));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.bitcrusher", "Bit Crusher", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("bitDepth", "Bit Depth", 8, 2, 24, "bits"),
                Number("sampleRateReduction", "Rate Reduction", 4, 1, 128),
                Number("mix", "Mix", 1, 0, 1)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.bitcrusher").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new BitCrusherProcessor();
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "bitDepth", processor.BitDepth, reader.GetFloat("bitDepth"));
                Bind(runtime, "sampleRateReduction", processor.SampleRateReduction, reader.GetFloat("sampleRateReduction"));
                Bind(runtime, "mix", processor.Mix, reader.GetFloat("mix"));
                return runtime;
            }));
    }

    private static void RegisterDynamics(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.compressor", "Compressor", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("thresholdDecibels", "Threshold", -18, -80, 0, "dB"),
                Number("ratio", "Ratio", 4, 1, 100),
                Number("kneeDecibels", "Knee", 6, 0, 24, "dB"),
                Number("attackMilliseconds", "Attack", 10, 0.01, 500, "ms"),
                Number("releaseMilliseconds", "Release", 100, 1, 5_000, "ms"),
                Number("makeupGainDecibels", "Makeup", 0, -24, 36, "dB"),
                Number("mix", "Mix", 1, 0, 1)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.compressor").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new CompressorProcessor();
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "thresholdDecibels", processor.ThresholdDecibels, reader.GetFloat("thresholdDecibels"));
                Bind(runtime, "ratio", processor.Ratio, reader.GetFloat("ratio"));
                Bind(runtime, "kneeDecibels", processor.KneeDecibels, reader.GetFloat("kneeDecibels"));
                Bind(runtime, "attackMilliseconds", processor.AttackMilliseconds, reader.GetFloat("attackMilliseconds"));
                Bind(runtime, "releaseMilliseconds", processor.ReleaseMilliseconds, reader.GetFloat("releaseMilliseconds"));
                Bind(runtime, "makeupGainDecibels", processor.MakeupGainDecibels, reader.GetFloat("makeupGainDecibels"));
                Bind(runtime, "mix", processor.Mix, reader.GetFloat("mix"));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.limiter", "Limiter", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("ceilingDecibels", "Ceiling", -0.8, -24, 0, "dB"),
                Number("releaseMilliseconds", "Release", 80, 1, 5_000, "ms")),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.limiter").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new LimiterProcessor();
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor);
                Bind(runtime, "ceilingDecibels", processor.CeilingDecibels, reader.GetFloat("ceilingDecibels"));
                Bind(runtime, "releaseMilliseconds", processor.ReleaseMilliseconds, reader.GetFloat("releaseMilliseconds"));
                return runtime;
            }));

        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.lookahead-limiter", "Look-Ahead Limiter", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("lookAheadMilliseconds", "Look Ahead", 5, 0.1, 100, "ms", false),
                Number("ceilingDecibels", "Ceiling", -0.8, -24, 0, "dB"),
                Number("releaseMilliseconds", "Release", 80, 1, 5_000, "ms")),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.lookahead-limiter").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new LookAheadLimiterProcessor(reader.GetDouble("lookAheadMilliseconds"));
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor, reader.GetDouble("lookAheadMilliseconds") / 1000.0);
                Bind(runtime, "ceilingDecibels", processor.CeilingDecibels, reader.GetFloat("ceilingDecibels"));
                Bind(runtime, "releaseMilliseconds", processor.ReleaseMilliseconds, reader.GetFloat("releaseMilliseconds"));
                return runtime;
            }));
    }

    private static void RegisterReverb(GraphNodeRegistry registry)
    {
        registry.Register(new DelegateGraphNodeFactory(
            Descriptor("dsp.reverb", "Algorithmic Reverb", GraphNodeCategory.Processor, [AudioInput], [AudioOutput],
                Number("preDelayMilliseconds", "Pre-delay", 12, 0, 250, "ms"),
                Number("roomSize", "Room Size", 0.7, 0, 1),
                Number("damping", "Damping", 0.4, 0, 1),
                Number("wet", "Wet", 0.3, 0, 1),
                Number("dry", "Dry", 1, 0, 1),
                Number("width", "Width", 0.9, 0, 1),
                Number("tailSeconds", "Tail Limit", 6, 0, 60, "s", false)),
            (definition, _) =>
            {
                NodeTypeDescriptor descriptor = registry.GetRequiredFactory("dsp.reverb").Descriptor;
                var reader = new GraphParameterReader(definition, descriptor);
                var processor = new AlgorithmicReverbProcessor();
                var runtime = new AudioProcessorRuntimeNode(descriptor, processor, reader.GetDouble("tailSeconds"));
                Bind(runtime, "preDelayMilliseconds", processor.PreDelayMilliseconds, reader.GetFloat("preDelayMilliseconds"));
                Bind(runtime, "roomSize", processor.RoomSize, reader.GetFloat("roomSize"));
                Bind(runtime, "damping", processor.Damping, reader.GetFloat("damping"));
                Bind(runtime, "wet", processor.Wet, reader.GetFloat("wet"));
                Bind(runtime, "dry", processor.Dry, reader.GetFloat("dry"));
                Bind(runtime, "width", processor.Width, reader.GetFloat("width"));
                return runtime;
            }));
    }

    private static NodeTypeDescriptor Descriptor(
        string typeId,
        string displayName,
        GraphNodeCategory category,
        IReadOnlyList<GraphPortDescriptor> inputs,
        IReadOnlyList<GraphPortDescriptor> outputs,
        params GraphParameterDescriptor[] parameters) =>
        new(typeId, displayName, category, 1, [.. inputs, .. outputs], parameters);

    private static GraphParameterDescriptor Number(
        string id,
        string name,
        double defaultValue,
        double minimum,
        double maximum,
        string unit = "",
        bool macroAddressable = true) =>
        new(id, name, GraphParameterType.Number, defaultValue, minimum, maximum, unit, null, macroAddressable);

    private static GraphParameterDescriptor Text(string id, string name, string defaultValue) =>
        new(id, name, GraphParameterType.Text, defaultValue, null, null, "", null, false);

    private static GraphParameterDescriptor Seed(string id, string name) =>
        new(id, name, GraphParameterType.Seed, 0, 0, double.MaxValue, "", null, false);

    private static GraphParameterDescriptor Enum<T>(string id, string name, T defaultValue)
        where T : struct, Enum =>
        new(id, name, GraphParameterType.Enumeration, defaultValue.ToString(), null, null, "", System.Enum.GetNames<T>(), false);

    private static T ParseEnum<T>(string value, T fallback)
        where T : struct, Enum =>
        System.Enum.TryParse(value, true, out T result) ? result : fallback;

    private static IAudioNode WrapFinite(IAudioNode source, double duration, double attack, double release)
    {
        if (duration <= 0)
        {
            return source;
        }

        attack = Math.Clamp(attack, 0, duration);
        release = Math.Clamp(release, 0, duration - attack);
        double sustain = Math.Max(0, duration - attack - release);
        return new EnvelopeNode(source, new AdsrEnvelope(attack, 0, 1, sustain, release));
    }

    private static void Bind(AudioProcessorRuntimeNode runtime, string id, AudioParameter parameter, float initialValue)
    {
        parameter.SetValue(initialValue);
        runtime.AddParameter(id, parameter.Metadata.Minimum, parameter.Metadata.Maximum, parameter.SetValue);
    }
}
