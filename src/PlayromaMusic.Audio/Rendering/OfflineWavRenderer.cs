using System.Diagnostics;
using PlayromaMusic.Audio.Backends;
using PlayromaMusic.Audio.Engine;
using PlayromaMusic.Audio.Scheduling;
using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Audio.Rendering;

public sealed record OfflineRenderResult(
    string OutputPath,
    string BackendName,
    string SoundFontPath,
    int SampleRate,
    long FrameCount,
    TimeSpan AudioDuration,
    TimeSpan RenderTime,
    double PeakLevel);

public static class OfflineWavRenderer
{
    public static OfflineRenderResult Render(
        ProjectDocument project,
        string outputPath,
        string? fluidSynthLibraryPath = null,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (string.IsNullOrWhiteSpace(project.Audio.SoundFontPath))
        {
            throw new InvalidOperationException("Choose a SoundFont before rendering a WAV file.");
        }

        SynthBackendCreationResult creation = SynthBackendFactory.Create(project.Audio, fluidSynthLibraryPath);
        using ISynthBackend backend = creation.Backend;
        if (!backend.IsReady)
        {
            throw new InvalidOperationException("No synthesis backend could load the selected SoundFont: " + creation.Message);
        }

        SongPlaybackSnapshot snapshot = SongPlaybackSnapshot.Create(
            project.Song,
            project.Audio.SampleRate,
            project.Audio.RenderTailSeconds);
        SnapshotSynthesizer player = new(backend);
        player.SetSnapshot(snapshot);

        string fullPath = Path.GetFullPath(outputPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        double peak = 0;
        int bufferFrames = Math.Clamp(project.Audio.BufferFrames, 128, 16384);
        float[] samples = new float[bufferFrames * 2];
        byte[] pcm = new byte[bufferFrames * 2 * sizeof(short)];
        long framesWritten = 0;

        using (WaveFileWriter writer = new(fullPath, project.Audio.SampleRate))
        {
            while (framesWritten < snapshot.TotalLengthSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int frames = checked((int)Math.Min(bufferFrames, snapshot.TotalLengthSamples - framesWritten));
                samples.AsSpan().Clear();
                player.Render(samples, bufferFrames);
                double blockPeak = Pcm16Converter.ConvertInterleavedFloatToPcm16(
                    samples.AsSpan(0, frames * 2),
                    pcm.AsSpan(0, frames * 2 * sizeof(short)),
                    project.Audio.MasterVolume);
                peak = Math.Max(peak, blockPeak);
                writer.Write(pcm.AsSpan(0, frames * 2 * sizeof(short)));
                framesWritten += frames;
                progress?.Report(snapshot.TotalLengthSamples == 0
                    ? 1.0
                    : framesWritten / (double)snapshot.TotalLengthSamples);
            }
        }

        stopwatch.Stop();
        return new OfflineRenderResult(
            fullPath,
            backend.DisplayName,
            backend.SoundFontPath,
            project.Audio.SampleRate,
            framesWritten,
            TimeSpan.FromSeconds(framesWritten / (double)project.Audio.SampleRate),
            stopwatch.Elapsed,
            peak);
    }
}
