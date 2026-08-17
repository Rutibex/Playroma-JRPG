using NAudio.Wave;
using OggVorbisEncoder;
using OggVorbisEncoder.Setup;
using PlayromaMusic.Audio.Rendering;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Midi;

namespace Playroma.JRPG.Editor.WinForms.Music;

public enum MvAudioDatabaseKind
{
    Bgm,
    Bgs,
    Me,
    Se
}

[Flags]
public enum MvAudioExportFormats
{
    None = 0,
    Ogg = 1,
    M4A = 2,
    Both = Ogg | M4A
}

public sealed record MvMusicExportRequest(
    ProjectDocument Project,
    string ProjectRoot,
    string FileName,
    MvAudioDatabaseKind DatabaseKind,
    MvAudioExportFormats Formats,
    bool SaveAuthoringProject = true,
    int AacBitRate = 192000,
    float VorbisQuality = 0.55f);

public sealed record MvMusicExportResult(
    IReadOnlyList<string> AudioFiles,
    string? AuthoringProject,
    OfflineRenderResult RenderResult);

public sealed class MusicExportService
{
    public MvMusicExportResult ExportToMvDatabase(
        MvMusicExportRequest request,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Formats == MvAudioExportFormats.None) throw new ArgumentException("Choose at least one MV audio format.", nameof(request));
        string root = Path.GetFullPath(request.ProjectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"RPG Maker MV project folder not found: {root}");
        string safeName = SanitizeFileName(request.FileName);
        if (safeName.Length == 0) safeName = SanitizeFileName(request.Project.Title);
        if (safeName.Length == 0) safeName = "PlayromaMusic";
        string category = request.DatabaseKind.ToString().ToLowerInvariant();
        string targetDirectory = Path.Combine(root, "audio", category);
        Directory.CreateDirectory(targetDirectory);

        string tempDirectory = Path.Combine(Path.GetTempPath(), "PlayromaMusic", "MVExport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string wavePath = Path.Combine(tempDirectory, safeName + ".wav");
        List<string> outputFiles = [];
        try
        {
            OfflineRenderResult render = OfflineWavRenderer.Render(
                request.Project,
                wavePath,
                cancellationToken: cancellationToken,
                progress: progress);

            if (request.Formats.HasFlag(MvAudioExportFormats.Ogg))
            {
                string ogg = Path.Combine(targetDirectory, safeName + ".ogg");
                EncodeVorbis(wavePath, ogg, request.VorbisQuality, cancellationToken);
                outputFiles.Add(ogg);
            }
            if (request.Formats.HasFlag(MvAudioExportFormats.M4A))
            {
                string m4a = Path.Combine(targetDirectory, safeName + ".m4a");
                EncodeM4A(wavePath, m4a, request.AacBitRate);
                outputFiles.Add(m4a);
            }

            string? authoring = null;
            if (request.SaveAuthoringProject)
            {
                string authoringDirectory = Path.Combine(root, "data", "PlayromaMusic");
                Directory.CreateDirectory(authoringDirectory);
                authoring = Path.Combine(authoringDirectory, category + "-" + safeName + ".playroma-music.json");
                ProjectSerializer.Save(request.Project, authoring);
            }
            return new MvMusicExportResult(outputFiles, authoring, render);
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

    public OfflineRenderResult ExportWave(ProjectDocument project, string outputPath, CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
        OfflineWavRenderer.Render(project, outputPath, cancellationToken: cancellationToken, progress: progress);

    public MidiExportResult ExportMidi(ProjectDocument project, string outputPath) => new MidiExportService().Export(project, outputPath);

    private static void EncodeM4A(string wavePath, string outputPath, int bitRate)
    {
        string temporary = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(outputPath) + ".tmp" + Path.GetExtension(outputPath));
        try
        {
            using WaveFileReader reader = new(wavePath);
            MediaFoundationEncoder.EncodeToAac(reader, temporary, Math.Clamp(bitRate, 64000, 320000));
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
        comments.AddTag("ENCODER", "Playroma Music for Playroma jRPG");
        OggStream oggStream = new(Random.Shared.Next());
        oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));

        string temporary = outputPath + ".tmp";
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
            throw new NotSupportedException("Playroma Music's MV exporter expects the renderer's 16-bit PCM WAV output.");
        int channels = reader.WaveFormat.Channels;
        int bytesPerFrame = channels * 2;
        long frameCountLong = reader.Length / bytesPerFrame;
        if (frameCountLong > int.MaxValue) throw new IOException("The rendered wave is too large to encode in one pass.");
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

    public static string SanitizeFileName(string text)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string cleaned = new((text ?? string.Empty).Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return cleaned.Trim(' ', '.', '_');
    }
}
