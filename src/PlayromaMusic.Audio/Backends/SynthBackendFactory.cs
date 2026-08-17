using PlayromaMusic.Core.Audio;

namespace PlayromaMusic.Audio.Backends;

public sealed record SynthBackendCreationResult(
    ISynthBackend Backend,
    bool UsedFallback,
    string Message);

public static class SynthBackendFactory
{
    public static SynthBackendCreationResult Create(AudioProjectSettings settings, string? fluidSynthLibraryPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        List<string> diagnostics = new();

        IEnumerable<SynthBackendPreference> order = settings.PreferredBackend switch
        {
            SynthBackendPreference.FluidSynth => [SynthBackendPreference.FluidSynth, SynthBackendPreference.MeltySynth],
            SynthBackendPreference.MeltySynth => [SynthBackendPreference.MeltySynth, SynthBackendPreference.FluidSynth],
            _ => [SynthBackendPreference.FluidSynth, SynthBackendPreference.MeltySynth]
        };

        bool first = true;
        foreach (SynthBackendPreference preference in order)
        {
            if (preference == SynthBackendPreference.FluidSynth &&
                !FluidSynthBackend.IsAvailable(fluidSynthLibraryPath))
            {
                diagnostics.Add("FluidSynth: native library was not found; using the next available backend.");
                first = false;
                continue;
            }

            ISynthBackend? backend = null;
            try
            {
                backend = preference switch
                {
                    SynthBackendPreference.FluidSynth => new FluidSynthBackend(settings.SampleRate, fluidSynthLibraryPath),
                    SynthBackendPreference.MeltySynth => new MeltySynthBackend(settings.SampleRate),
                    _ => throw new InvalidOperationException("Unsupported backend preference.")
                };

                if (!string.IsNullOrWhiteSpace(settings.SoundFontPath))
                {
                    backend.LoadSoundFont(settings.SoundFontPath);
                }

                return new SynthBackendCreationResult(
                    backend,
                    UsedFallback: !first,
                    Message: !first
                        ? $"Fallback selected: {backend.DisplayName}. {string.Join(" ", diagnostics)}"
                        : backend.StatusMessage);
            }
            catch (Exception ex)
            {
                backend?.Dispose();
                diagnostics.Add($"{preference}: {ex.Message}");
            }

            first = false;
        }

        string message = diagnostics.Count == 0
            ? "No synthesis backend could be initialized."
            : string.Join(" ", diagnostics);
        return new SynthBackendCreationResult(
            new SilentSynthBackend(settings.SampleRate, message),
            UsedFallback: true,
            Message: message);
    }
}
