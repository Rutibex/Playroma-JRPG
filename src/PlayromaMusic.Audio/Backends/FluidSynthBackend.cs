using System.Runtime.InteropServices;

namespace PlayromaMusic.Audio.Backends;

public sealed class FluidSynthBackend : ISynthBackend
{
    private readonly FluidNative _native;
    private IntPtr _settings;
    private IntPtr _synth;

    public FluidSynthBackend(int sampleRate, string? explicitLibraryPath = null)
    {
        SampleRate = sampleRate;
        _native = FluidNative.Load(explicitLibraryPath);
        _settings = _native.NewSettings();
        if (_settings == IntPtr.Zero)
        {
            throw new InvalidOperationException("FluidSynth could not create its settings object.");
        }

        _native.SettingsSetNum(_settings, "synth.sample-rate", sampleRate);
        _synth = _native.NewSynth(_settings);
        if (_synth == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("FluidSynth could not create a synthesizer.");
        }

        StatusMessage = $"FluidSynth loaded from {Path.GetFileName(_native.LibraryPath)}; waiting for a SoundFont.";
    }

    public SynthBackendKind Kind => SynthBackendKind.FluidSynth;
    public string DisplayName => "FluidSynth (native)";
    public int SampleRate { get; }
    public bool IsReady => _synth != IntPtr.Zero && !string.IsNullOrWhiteSpace(SoundFontPath);
    public string StatusMessage { get; private set; }
    public string SoundFontPath { get; private set; } = string.Empty;

    public static bool IsAvailable(string? explicitLibraryPath = null) =>
        FluidNative.CanLoad(explicitLibraryPath);

    public void LoadSoundFont(string path)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SoundFont file was not found.", fullPath);
        }

        int id = _native.SoundFontLoad(_synth, fullPath, 1);
        if (id < 0)
        {
            throw new InvalidOperationException($"FluidSynth rejected SoundFont '{fullPath}'.");
        }

        SoundFontPath = fullPath;
        StatusMessage = $"Loaded {Path.GetFileName(fullPath)} with FluidSynth.";
        Reset();
    }

    public void ProgramChange(int channel, int program)
    {
        EnsureAlive();
        _native.ProgramChange(_synth, Math.Clamp(channel, 0, 15), Math.Clamp(program, 0, 127));
    }

    public void ControlChange(int channel, int controller, int value)
    {
        EnsureAlive();
        _native.ControlChange(_synth, Math.Clamp(channel, 0, 15), Math.Clamp(controller, 0, 127), Math.Clamp(value, 0, 127));
    }

    public void NoteOn(int channel, int pitch, int velocity)
    {
        EnsureAlive();
        _native.NoteOn(_synth, Math.Clamp(channel, 0, 15), Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 0, 127));
    }

    public void NoteOff(int channel, int pitch)
    {
        EnsureAlive();
        _native.NoteOff(_synth, Math.Clamp(channel, 0, 15), Math.Clamp(pitch, 0, 127));
    }

    public void AllNotesOff()
    {
        if (_synth == IntPtr.Zero)
        {
            return;
        }

        for (int channel = 0; channel < 16; channel++)
        {
            _native.ControlChange(_synth, channel, 64, 0);
            _native.ControlChange(_synth, channel, 120, 0);
            _native.ControlChange(_synth, channel, 123, 0);
        }
    }

    public void Reset()
    {
        EnsureAlive();
        _native.SystemReset(_synth);
    }

    public unsafe void Render(Span<float> interleavedStereo, int frameCount)
    {
        EnsureAlive();
        if (frameCount < 0 || interleavedStereo.Length < frameCount * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        Span<float> destination = interleavedStereo[..(frameCount * 2)];
        destination.Clear();
        fixed (float* output = destination)
        {
            int result = _native.WriteFloat(
                _synth,
                frameCount,
                (IntPtr)output,
                0,
                2,
                (IntPtr)output,
                1,
                2);
            if (result != 0)
            {
                destination.Clear();
                throw new InvalidOperationException("FluidSynth failed while rendering an audio block.");
            }
        }
    }

    public void Dispose()
    {
        if (_synth != IntPtr.Zero)
        {
            _native.DeleteSynth(_synth);
            _synth = IntPtr.Zero;
        }

        if (_settings != IntPtr.Zero)
        {
            _native.DeleteSettings(_settings);
            _settings = IntPtr.Zero;
        }

        _native.Dispose();
    }

    private void EnsureAlive()
    {
        if (_synth == IntPtr.Zero)
        {
            throw new ObjectDisposedException(nameof(FluidSynthBackend));
        }
    }

    private sealed class FluidNative : IDisposable
    {
        private IntPtr _library;

        private FluidNative(IntPtr library, string libraryPath)
        {
            _library = library;
            LibraryPath = libraryPath;
            NewSettings = Get<NewSettingsDelegate>("new_fluid_settings");
            DeleteSettings = Get<DeleteSettingsDelegate>("delete_fluid_settings");
            SettingsSetNum = Get<SettingsSetNumDelegate>("fluid_settings_setnum");
            NewSynth = Get<NewSynthDelegate>("new_fluid_synth");
            DeleteSynth = Get<DeleteSynthDelegate>("delete_fluid_synth");
            SoundFontLoad = Get<SoundFontLoadDelegate>("fluid_synth_sfload");
            ProgramChange = Get<ProgramChangeDelegate>("fluid_synth_program_change");
            ControlChange = Get<ControlChangeDelegate>("fluid_synth_cc");
            NoteOn = Get<NoteOnDelegate>("fluid_synth_noteon");
            NoteOff = Get<NoteOffDelegate>("fluid_synth_noteoff");
            SystemReset = Get<SystemResetDelegate>("fluid_synth_system_reset");
            WriteFloat = Get<WriteFloatDelegate>("fluid_synth_write_float");
        }

        public string LibraryPath { get; }
        public NewSettingsDelegate NewSettings { get; }
        public DeleteSettingsDelegate DeleteSettings { get; }
        public SettingsSetNumDelegate SettingsSetNum { get; }
        public NewSynthDelegate NewSynth { get; }
        public DeleteSynthDelegate DeleteSynth { get; }
        public SoundFontLoadDelegate SoundFontLoad { get; }
        public ProgramChangeDelegate ProgramChange { get; }
        public ControlChangeDelegate ControlChange { get; }
        public NoteOnDelegate NoteOn { get; }
        public NoteOffDelegate NoteOff { get; }
        public SystemResetDelegate SystemReset { get; }
        public WriteFloatDelegate WriteFloat { get; }

        public static bool CanLoad(string? explicitPath)
        {
            foreach (string candidate in Candidates(explicitPath))
            {
                if (NativeLibrary.TryLoad(candidate, out IntPtr library))
                {
                    NativeLibrary.Free(library);
                    return true;
                }
            }

            return false;
        }

        public static FluidNative Load(string? explicitPath)
        {
            List<string> failures = new();
            foreach (string candidate in Candidates(explicitPath))
            {
                if (!NativeLibrary.TryLoad(candidate, out IntPtr library))
                {
                    failures.Add(candidate);
                    continue;
                }

                try
                {
                    return new FluidNative(library, candidate);
                }
                catch
                {
                    NativeLibrary.Free(library);
                    throw;
                }
            }

            throw new DllNotFoundException(
                "FluidSynth was not found. Tried: " + string.Join(", ", failures));
        }

        public void Dispose()
        {
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
                _library = IntPtr.Zero;
            }
        }

        private T Get<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

        private static IEnumerable<string> Candidates(string? explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                yield return Path.GetFullPath(explicitPath);
            }

            string? environmentPath = Environment.GetEnvironmentVariable("PROCEDURAL_MIDI_FLUIDSYNTH");
            if (!string.IsNullOrWhiteSpace(environmentPath))
            {
                yield return environmentPath;
            }

            if (OperatingSystem.IsWindows())
            {
                yield return "libfluidsynth-3.dll";
                yield return "fluidsynth.dll";
                yield return "libfluidsynth.dll";
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "libfluidsynth.3.dylib";
                yield return "libfluidsynth.dylib";
            }
            else
            {
                yield return "libfluidsynth.so.3";
                yield return "libfluidsynth.so";
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr NewSettingsDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DeleteSettingsDelegate(IntPtr settings);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SettingsSetNumDelegate(
            IntPtr settings,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            double value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr NewSynthDelegate(IntPtr settings);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DeleteSynthDelegate(IntPtr synth);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SoundFontLoadDelegate(
            IntPtr synth,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
            int resetPresets);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ProgramChangeDelegate(IntPtr synth, int channel, int program);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int ControlChangeDelegate(IntPtr synth, int channel, int controller, int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NoteOnDelegate(IntPtr synth, int channel, int pitch, int velocity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NoteOffDelegate(IntPtr synth, int channel, int pitch);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SystemResetDelegate(IntPtr synth);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int WriteFloatDelegate(
            IntPtr synth,
            int frameCount,
            IntPtr leftOutput,
            int leftOffset,
            int leftIncrement,
            IntPtr rightOutput,
            int rightOffset,
            int rightIncrement);
    }
}
