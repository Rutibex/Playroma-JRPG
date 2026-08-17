using PlayromaMusic.Audio.Backends;
using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Audio.SoundFonts;

public sealed class PresetAudioProfiler
{
    public InstrumentCatalogDocument Profile(
        string soundFontPath,
        InstrumentCatalogDocument catalog,
        int maximumPresets = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundFontPath);
        ArgumentNullException.ThrowIfNull(catalog);
        const int sampleRate = 22050;
        using MeltySynthBackend synth = new(sampleRate);
        synth.LoadSoundFont(soundFontPath);
        int profiled = 0;
        foreach (InstrumentPresetDescriptor preset in catalog.Presets.Where(item => item.IsEnabled && !item.IsExcluded).Take(Math.Max(0, maximumPresets)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (preset.IsDrumKit)
            {
                ProfileDrumKit(synth, preset, sampleRate);
            }
            else
            {
                ProfileMelodicPreset(synth, preset, sampleRate);
            }
            profiled++;
        }
        catalog.Diagnostics.Add($"Rendered diagnostic phrases and profiled {profiled} preset(s) with MeltySynth at {sampleRate} Hz.");
        return catalog;
    }

    private static void ProfileMelodicPreset(MeltySynthBackend synth, InstrumentPresetDescriptor preset, int sampleRate)
    {
        synth.Reset();
        SelectPreset(synth, 0, preset.Bank, preset.Program);
        int pitch = Math.Clamp((preset.SuggestedLowNote + preset.SuggestedHighNote) / 2, 48, 84);
        synth.NoteOn(0, pitch, 100);
        float[] attackAndSustain = Render(synth, sampleRate, 1.1);
        synth.NoteOff(0, pitch);
        float[] release = Render(synth, sampleRate, 0.6);
        ApplyMeasurements(preset, attackAndSustain, release, sampleRate);
    }

    private static void ProfileDrumKit(MeltySynthBackend synth, InstrumentPresetDescriptor preset, int sampleRate)
    {
        synth.Reset();
        SelectPreset(synth, 9, preset.Bank, preset.Program);
        foreach (int note in new[] { 36, 38, 42, 46, 49 }) synth.NoteOn(9, note, 100);
        float[] signal = Render(synth, sampleRate, 0.8);
        foreach (int note in new[] { 36, 38, 42, 46, 49 }) synth.NoteOff(9, note);
        float[] release = Render(synth, sampleRate, 0.25);
        ApplyMeasurements(preset, signal, release, sampleRate);
        preset.Timbre.Percussiveness = Math.Max(0.85, preset.Timbre.Percussiveness);
    }

    private static void SelectPreset(ISynthBackend synth, int channel, int bank, int program)
    {
        synth.ControlChange(channel, 0, Math.Clamp(bank / 128, 0, 127));
        synth.ControlChange(channel, 32, Math.Clamp(bank % 128, 0, 127));
        synth.ProgramChange(channel, Math.Clamp(program, 0, 127));
    }

    private static float[] Render(ISynthBackend synth, int sampleRate, double seconds)
    {
        int frames = Math.Max(1, (int)Math.Round(sampleRate * seconds));
        float[] interleaved = new float[frames * 2];
        synth.Render(interleaved, frames);
        float[] mono = new float[frames];
        for (int index = 0; index < frames; index++) mono[index] = (interleaved[index * 2] + interleaved[index * 2 + 1]) * 0.5f;
        return mono;
    }

    private static void ApplyMeasurements(InstrumentPresetDescriptor preset, float[] signal, float[] release, int sampleRate)
    {
        double peak = signal.Select(value => Math.Abs(value)).DefaultIfEmpty(0).Max();
        double early = Rms(signal, 0, Math.Min(signal.Length, sampleRate / 10));
        double middle = Rms(signal, Math.Min(signal.Length, sampleRate / 3), Math.Min(signal.Length, sampleRate * 2 / 3));
        double late = Rms(signal, Math.Min(signal.Length, sampleRate * 4 / 5), signal.Length);
        double releaseEnergy = Rms(release, 0, release.Length);
        int attackSamples = FirstAbove(signal, peak * 0.8);
        double attackSpeed = peak <= 1e-8 ? 0.5 : 1 - Math.Clamp(attackSamples / (sampleRate * 0.3), 0, 1);
        double sustain = peak <= 1e-8 ? 0 : Math.Clamp((middle + late) / Math.Max(1e-8, early * 2), 0, 1);
        double percussive = Math.Clamp(early / Math.Max(1e-8, early + middle + late), 0, 1);
        double brightness = Math.Clamp(ZeroCrossingRate(signal) * 8.0, 0, 1);
        double rms = Rms(signal, 0, signal.Length);
        double crest = peak <= 1e-8 ? 0 : rms / peak;
        double density = Math.Clamp(crest * 2.5, 0, 1);
        double pitchStability = Math.Clamp(1 - WindowedZeroCrossVariance(signal, sampleRate / 20) * 5, 0, 1);
        preset.Timbre = new TimbreProfile
        {
            Attack = attackSpeed,
            Sustain = Math.Clamp(sustain + releaseEnergy * 0.2, 0, 1),
            Brightness = brightness,
            Density = density,
            Percussiveness = percussive,
            PitchStability = pitchStability,
            Confidence = peak > 1e-5 ? 0.92 : 0.25
        };
        preset.WasAudioProfiled = true;
        UpdateProfileTags(preset);
    }

    private static void UpdateProfileTags(InstrumentPresetDescriptor preset)
    {
        HashSet<string> tags = preset.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (preset.Timbre.Attack >= 0.72) tags.Add("short-attack");
        if (preset.Timbre.Sustain >= 0.68) tags.Add("sustaining");
        if (preset.Timbre.Brightness >= 0.72) tags.Add("very-bright");
        if (preset.Timbre.Brightness <= 0.28) tags.Add("dark");
        if (preset.Timbre.Density >= 0.75) tags.Add("very-dense");
        if (preset.Timbre.Percussiveness >= 0.7) tags.Add("percussive");
        if (preset.Timbre.PitchStability <= 0.48) tags.Add("unstable-pitch");
        preset.Tags = tags.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int FirstAbove(float[] values, double threshold)
    {
        for (int index = 0; index < values.Length; index++) if (Math.Abs(values[index]) >= threshold) return index;
        return values.Length;
    }

    private static double Rms(float[] values, int start, int end)
    {
        start = Math.Clamp(start, 0, values.Length);
        end = Math.Clamp(end, start, values.Length);
        if (end <= start) return 0;
        double sum = 0;
        for (int index = start; index < end; index++) sum += values[index] * values[index];
        return Math.Sqrt(sum / (end - start));
    }

    private static double ZeroCrossingRate(float[] values)
    {
        if (values.Length < 2) return 0;
        int crossings = 0;
        for (int index = 1; index < values.Length; index++)
            if ((values[index - 1] < 0 && values[index] >= 0) || (values[index - 1] >= 0 && values[index] < 0)) crossings++;
        return crossings / (double)(values.Length - 1);
    }

    private static double WindowedZeroCrossVariance(float[] values, int window)
    {
        if (values.Length < 4 || window < 2) return 0;
        List<double> rates = new();
        for (int start = 0; start < values.Length; start += window)
        {
            int end = Math.Min(values.Length, start + window);
            int crossings = 0;
            for (int index = start + 1; index < end; index++)
                if ((values[index - 1] < 0 && values[index] >= 0) || (values[index - 1] >= 0 && values[index] < 0)) crossings++;
            if (end - start > 1) rates.Add(crossings / (double)(end - start - 1));
        }
        if (rates.Count < 2) return 0;
        double mean = rates.Average();
        return rates.Sum(value => Math.Pow(value - mean, 2)) / rates.Count;
    }
}
