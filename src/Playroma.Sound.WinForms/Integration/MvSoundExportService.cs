using System.Text.Json;
using NAudio.Wave;
using OggVorbisEncoder;
using OggVorbisEncoder.Setup;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.IO;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.WinForms.Integration;

public enum MvSoundDatabaseKind
{
    Bgm,
    Bgs,
    Me,
    Se
}

[Flags]
public enum MvSoundExportFormats
{
    None = 0,
    Ogg = 1,
    M4A = 2,
    Both = Ogg | M4A
}

public sealed record MvSoundExportRequest(
    string RpgMakerProjectRoot,
    string FileName,
    MvSoundDatabaseKind DatabaseKind,
    MvSoundExportFormats Formats,
    double MaximumDurationSeconds = 10,
    bool SaveAuthoringSource = true,
    int AacBitRate = 192000,
    float VorbisQuality = 0.55f);

public sealed record MvSoundExportResult(
    IReadOnlyList<string> AudioFiles,
    string? AuthoringSource,
    string ManifestPath,
    TimeSpan RenderedDuration,
    AuthoringCompilationResult Compilation);

/// <summary>
/// Compiles the selected Playroma Sound document with the real authoring pipeline, renders it
/// offline, and writes the two audio containers supported by RPG Maker MV.
/// </summary>
public sealed class MvSoundExportService
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<MvSoundExportResult> ExportAsync(
        AuthoringWorkspace workspace,
        IAuthoringDocument document,
        MvSoundExportRequest request,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Formats == MvSoundExportFormats.None)
            throw new ArgumentException("Choose at least one RPG Maker MV audio format.", nameof(request));
        if (!double.IsFinite(request.MaximumDurationSeconds) || request.MaximumDurationSeconds <= 0 || request.MaximumDurationSeconds > 600)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum render duration must be greater than 0 and no more than 600 seconds.");

        string root = Path.GetFullPath(request.RpgMakerProjectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"RPG Maker MV project folder not found: {root}");
        string safeName = SanitizeFileName(request.FileName);
        if (safeName.Length == 0) safeName = SanitizeFileName(document.DisplayName);
        if (safeName.Length == 0) safeName = "PlayromaSound";

        var format = new AudioFormat(48_000, 2, 512);
        progress?.Report(0.02);
        AuthoringCompilationResult compilation = await document.CompileAsync(
            workspace.CreateCompilationContext(format),
            cancellationToken).ConfigureAwait(false);
        if (!compilation.Succeeded)
        {
            string diagnostics = string.Join(Environment.NewLine, compilation.Validation.Diagnostics.Select(item => item.Message));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(diagnostics)
                ? "The selected Playroma Sound document could not be compiled."
                : diagnostics);
        }

        progress?.Report(0.12);
        RenderedSound rendered = RenderArtifact(compilation.Artifact, format, request.MaximumDurationSeconds, cancellationToken, progress);
        string category = request.DatabaseKind.ToString().ToLowerInvariant();
        string targetDirectory = Path.Combine(root, "audio", category);
        Directory.CreateDirectory(targetDirectory);

        string tempDirectory = Path.Combine(Path.GetTempPath(), "PlayromaSound", "MVExport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string wavePath = Path.Combine(tempDirectory, safeName + ".wav");
        List<string> outputFiles = [];
        string? authoringSource = null;
        string manifestPath = Path.Combine(root, "data", "PlayromaSound", "Exports", category, safeName + ".playroma-sound-export.json");
        try
        {
            WavWriter.Write(wavePath, rendered, WavEncoding.Pcm16);
            progress?.Report(0.62);

            if (request.Formats.HasFlag(MvSoundExportFormats.Ogg))
            {
                string ogg = Path.Combine(targetDirectory, safeName + ".ogg");
                EncodeVorbis(wavePath, ogg, request.VorbisQuality, cancellationToken);
                outputFiles.Add(ogg);
            }
            progress?.Report(request.Formats.HasFlag(MvSoundExportFormats.M4A) ? 0.78 : 0.90);

            if (request.Formats.HasFlag(MvSoundExportFormats.M4A))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string m4a = Path.Combine(targetDirectory, safeName + ".m4a");
                EncodeM4A(wavePath, m4a, request.AacBitRate);
                outputFiles.Add(m4a);
            }

            string sourceDirectory = Path.Combine(root, "data", "PlayromaSound", "Exports", category);
            Directory.CreateDirectory(sourceDirectory);
            if (request.SaveAuthoringSource)
            {
                string extension = document.Kind switch
                {
                    AuthoringDocumentKind.SoundGraph => ".playsound",
                    AuthoringDocumentKind.GeneratorRequest => ".playgen",
                    AuthoringDocumentKind.StreamingScene => ".playstream",
                    _ => ".json"
                };
                authoringSource = Path.Combine(sourceDirectory, safeName + extension);
                AtomicWriteText(authoringSource, document.SerializeContent());
            }

            var manifest = new
            {
                formatVersion = 1,
                generator = "Playroma Sound for Playroma jRPG",
                sourceDocumentId = document.DocumentId,
                sourceDisplayName = document.DisplayName,
                sourceKind = document.Kind.ToString(),
                sourcePath = authoringSource is null ? null : Path.GetRelativePath(root, authoringSource).Replace('\\', '/'),
                databaseKind = request.DatabaseKind.ToString(),
                audioFiles = outputFiles.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray(),
                renderedDurationSeconds = rendered.Duration.TotalSeconds,
                maximumDurationSeconds = request.MaximumDurationSeconds,
                sampleRate = format.SampleRate,
                channels = format.Channels,
                exportedAtUtc = DateTimeOffset.UtcNow
            };
            AtomicWriteText(manifestPath, JsonSerializer.Serialize(manifest, ManifestJson));
            progress?.Report(1.0);
            return new MvSoundExportResult(outputFiles, authoringSource, manifestPath, rendered.Duration, compilation);
        }
        catch
        {
            foreach (string output in outputFiles)
            {
                try { if (File.Exists(output)) File.Delete(output); }
                catch { }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true); }
            catch { }
        }
    }

    private static RenderedSound RenderArtifact(
        object? artifact,
        AudioFormat format,
        double maximumSeconds,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        CompiledSoundGraph? graph = artifact switch
        {
            CompiledSoundGraph direct => direct,
            GeneratedSoundArtifact generated => generated.CompiledGraph,
            _ => null
        };
        if (graph is not null) return RenderGraph(graph, format, maximumSeconds, cancellationToken, progress);
        if (artifact is PreparedSoundScene scene) return RenderScene(scene, format, maximumSeconds, cancellationToken, progress);
        throw new NotSupportedException("The selected document did not compile to a renderable Playroma Sound artifact.");
    }

    private static RenderedSound RenderGraph(
        CompiledSoundGraph graph,
        AudioFormat format,
        double maximumSeconds,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        int maximumFrames = format.FramesFromSeconds(maximumSeconds);
        float[] samples = new float[checked(maximumFrames * format.Channels)];
        graph.Reset();
        int position = 0;
        while (position < maximumFrames && !graph.IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(format.BlockFrames, maximumFrames - position);
            graph.Process(samples.AsSpan(position * format.Channels, frames * format.Channels), frames);
            position += frames;
            progress?.Report(0.12 + (0.48 * position / Math.Max(1d, maximumFrames)));
        }
        if (graph.State == GraphActivityState.Infinite) position = maximumFrames;
        Array.Resize(ref samples, checked(position * format.Channels));
        return new RenderedSound(format, samples);
    }

    private static RenderedSound RenderScene(
        PreparedSoundScene scene,
        AudioFormat format,
        double seconds,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        int totalFrames = format.FramesFromSeconds(seconds);
        float[] samples = new float[checked(totalFrames * format.Channels)];
        using StreamingSceneInstance instance = scene.CreateInstance();
        instance.Start();
        int position = 0;
        while (position < totalFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(format.BlockFrames, totalFrames - position);
            instance.Render(samples.AsSpan(position * format.Channels, frames * format.Channels), frames);
            position += frames;
            progress?.Report(0.12 + (0.48 * position / Math.Max(1d, totalFrames)));
        }
        return new RenderedSound(format, samples);
    }

    private static void EncodeM4A(string wavePath, string outputPath, int bitRate)
    {
        string temporary = outputPath + ".tmp.m4a";
        try
        {
            using WaveFileReader reader = new(wavePath);
            MediaFoundationEncoder.EncodeToAac(reader, temporary, Math.Clamp(bitRate, 64_000, 320_000));
            File.Move(temporary, outputPath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private static void EncodeVorbis(string wavePath, string outputPath, float quality, CancellationToken cancellationToken)
    {
        (float[][] samples, int sampleRate) = ReadStereoWave(wavePath, cancellationToken);
        VorbisInfo info = VorbisInfo.InitVariableBitRate(2, sampleRate, Math.Clamp(quality, -0.1f, 1f));
        Comments comments = new();
        comments.AddTag("ENCODER", "Playroma Sound for Playroma jRPG");
        OggStream oggStream = new(Random.Shared.Next());
        oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));

        string temporary = outputPath + ".tmp.ogg";
        try
        {
            using FileStream output = File.Create(temporary);
            FlushPages(oggStream, output, true);
            ProcessingState processing = ProcessingState.Create(info);
            const int writeBufferSize = 4096;
            for (int readIndex = 0; readIndex < samples[0].Length; readIndex += writeBufferSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = Math.Min(writeBufferSize, samples[0].Length - readIndex);
                processing.WriteData(samples, length, readIndex);
                while (!oggStream.Finished && processing.PacketOut(out OggPacket packet))
                {
                    oggStream.PacketIn(packet);
                    FlushPages(oggStream, output, false);
                }
            }
            processing.WriteEndOfStream();
            while (!oggStream.Finished && processing.PacketOut(out OggPacket packet))
            {
                oggStream.PacketIn(packet);
                FlushPages(oggStream, output, false);
            }
            FlushPages(oggStream, output, true);
            File.Move(temporary, outputPath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private static (float[][] Samples, int SampleRate) ReadStereoWave(string wavePath, CancellationToken cancellationToken)
    {
        using WaveFileReader reader = new(wavePath);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
            throw new NotSupportedException("The RPG Maker MV exporter expects 16-bit PCM WAV input.");
        int channels = reader.WaveFormat.Channels;
        int bytesPerFrame = channels * 2;
        long frameCountLong = reader.Length / bytesPerFrame;
        if (frameCountLong > int.MaxValue) throw new IOException("The rendered sound is too large to encode in one pass.");
        int frameCount = checked((int)frameCountLong);
        float[][] result = [new float[frameCount], new float[frameCount]];
        byte[] buffer = new byte[Math.Max(4096, bytesPerFrame * 2048)];
        int frameIndex = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = read / bytesPerFrame;
            for (int frame = 0; frame < frames && frameIndex < frameCount; frame++, frameIndex++)
            {
                int offset = frame * bytesPerFrame;
                short left = BitConverter.ToInt16(buffer, offset);
                short right = channels > 1 ? BitConverter.ToInt16(buffer, offset + 2) : left;
                result[0][frameIndex] = left / 32768f;
                result[1][frameIndex] = right / 32768f;
            }
        }
        return (result, reader.WaveFormat.SampleRate);
    }

    private static void FlushPages(OggStream oggStream, Stream output, bool force)
    {
        while (oggStream.PageOut(out OggPage page, force))
        {
            output.Write(page.Header, 0, page.Header.Length);
            output.Write(page.Body, 0, page.Body.Length);
        }
    }

    private static void AtomicWriteText(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, fullPath, true);
    }

    public static string SanitizeFileName(string text)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string cleaned = new((text ?? string.Empty).Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return cleaned.Trim(' ', '.', '_');
    }
}
