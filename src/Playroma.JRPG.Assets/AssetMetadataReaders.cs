using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Playroma.JRPG.Assets;

public static class AssetMetadataReaders
{
    public static async ValueTask<AssetMetadataSummary> ReadAsync(
        string fullPath,
        AssetMediaKind mediaKind,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return mediaKind switch
            {
                AssetMediaKind.Image => new AssetMetadataSummary(Image: await ReadImageAsync(fullPath, cancellationToken).ConfigureAwait(false), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Audio => new AssetMetadataSummary(Audio: await ReadAudioAsync(fullPath, cancellationToken).ConfigureAwait(false), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Movie => new AssetMetadataSummary(Movie: await ReadMovieAsync(fullPath, cancellationToken).ConfigureAwait(false), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Font => new AssetMetadataSummary(Font: await ReadFontAsync(fullPath, cancellationToken).ConfigureAwait(false), AnalysedUtc: DateTimeOffset.UtcNow),
                _ => AssetMetadataSummary.Empty
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return mediaKind switch
            {
                AssetMediaKind.Image => new AssetMetadataSummary(Image: new ImageAssetMetadata(0, 0, 0, false, Path.GetExtension(fullPath), false, exception.Message), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Audio => new AssetMetadataSummary(Audio: new AudioAssetMetadata(null, null, null, null, null, null, null, Path.GetExtension(fullPath), false, Detail: exception.Message), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Movie => new AssetMetadataSummary(Movie: new MovieAssetMetadata(null, null, null, null, Path.GetExtension(fullPath), null, null, false, exception.Message), AnalysedUtc: DateTimeOffset.UtcNow),
                AssetMediaKind.Font => new AssetMetadataSummary(Font: new FontAssetMetadata(null, null, null, Path.GetExtension(fullPath), false, exception.Message), AnalysedUtc: DateTimeOffset.UtcNow),
                _ => AssetMetadataSummary.Empty
            };
        }
    }

    public static async ValueTask<ImageAssetMetadata> ReadImageAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = OpenShared(fullPath);
        byte[] header = new byte[32];
        int read = await ReadAtLeastAsync(stream, header, header.Length, cancellationToken).ConfigureAwait(false);
        ReadOnlySpan<byte> bytes = header.AsSpan(0, read);
        if (bytes.Length >= 26 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            int width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            int bitDepth = bytes[24];
            byte colourType = bytes[25];
            bool alpha = colourType is 4 or 6;
            return new ImageAssetMetadata(width, height, bitDepth, alpha, "PNG", width > 0 && height > 0);
        }

        if (bytes.Length >= 26 && bytes[..2].SequenceEqual("BM"u8))
        {
            int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4));
            int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4)));
            int bits = bytes.Length >= 30 ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) : 0;
            return new ImageAssetMetadata(width, height, bits, bits == 32, "BMP", width > 0 && height > 0);
        }

        if (bytes.Length >= 10 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
            return new ImageAssetMetadata(width, height, 8, true, "GIF", width > 0 && height > 0);
        }

        if (bytes.Length >= 22
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]) == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2)) == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4, 2)) > 0)
        {
            int width = bytes[6] == 0 ? 256 : bytes[6];
            int height = bytes[7] == 0 ? 256 : bytes[7];
            int bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(12, 2));
            return new ImageAssetMetadata(width, height, bits, bits == 32, "ICO", width > 0 && height > 0, "Dimensions describe the first icon image.");
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            stream.Position = 0;
            byte[] all = await ReadPrefixAsync(stream, 64, cancellationToken).ConfigureAwait(false);
            if (all.Length >= 30 && all.AsSpan(12, 4).SequenceEqual("VP8X"u8))
            {
                int width = 1 + all[24] + (all[25] << 8) + (all[26] << 16);
                int height = 1 + all[27] + (all[28] << 8) + (all[29] << 16);
                bool alpha = (all[20] & 0x10) != 0;
                return new ImageAssetMetadata(width, height, 8, alpha, "WebP", true);
            }

            return new ImageAssetMetadata(0, 0, 0, false, "WebP", true, "Dimensions require a decoder for this WebP subtype.");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            stream.Position = 2;
            return await ReadJpegAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return new ImageAssetMetadata(0, 0, 0, false, Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant(), false, "Unsupported or corrupt image header.");
    }

    public static async ValueTask<AudioAssetMetadata> ReadAudioAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(fullPath);
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadWaveAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }

        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadOggAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }

        FileInfo file = new(fullPath);
        return new AudioAssetMetadata(null, null, null, null, null, null, null, extension.TrimStart('.').ToUpperInvariant(), file.Length > 0, Detail: "Metadata inspection for this codec is limited without a platform decoder.");
    }

    public static async ValueTask<MovieAssetMetadata> ReadMovieAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = OpenShared(fullPath);
        byte[] header = await ReadPrefixAsync(stream, 64, cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(fullPath);
        if (header.Length >= 12 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            string brand = Encoding.ASCII.GetString(header, 8, Math.Min(4, header.Length - 8));
            return new MovieAssetMetadata(null, null, null, null, "MP4/ISO BMFF", brand, null, true, "Detailed stream metadata requires a media decoder.");
        }

        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return new MovieAssetMetadata(null, null, null, null, "WebM/Matroska", null, null, true, "Detailed stream metadata requires a media decoder.");
        }

        return new MovieAssetMetadata(null, null, null, null, extension.TrimStart('.').ToUpperInvariant(), null, null, new FileInfo(fullPath).Length > 0, "Container recognized by extension only.");
    }

    public static async ValueTask<FontAssetMetadata> ReadFontAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = OpenShared(fullPath);
        byte[] header = await ReadPrefixAsync(stream, 12, cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();
        bool sfnt = header.Length >= 4 && (header.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 1, 0, 0 }) || header.AsSpan(0, 4).SequenceEqual("OTTO"u8));
        string? family = sfnt ? await TryReadSfntFamilyAsync(stream, cancellationToken).ConfigureAwait(false) : null;
        return new FontAssetMetadata(family, null, family, extension, header.Length > 0, sfnt ? null : "Preview support depends on the Windows private-font loader.");
    }

    public static async ValueTask<WaveformEnvelope> AnalyseWaveformAsync(
        string fullPath,
        int bucketCount = 5_000,
        CancellationToken cancellationToken = default)
    {
        if (bucketCount <= 0) throw new ArgumentOutOfRangeException(nameof(bucketCount));
        WaveInfo info = await ReadWaveInfoAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (info.AudioFormat != 1 && info.AudioFormat != 3)
        {
            throw new NotSupportedException("Waveform analysis currently supports PCM and IEEE-float WAV files.");
        }

        long frameCount = info.DataLength / info.BlockAlign;
        int effectiveBuckets = checked((int)Math.Min(bucketCount, Math.Max(1, frameCount)));
        WaveformBucket[] buckets = new WaveformBucket[effectiveBuckets];
        float[] mins = Enumerable.Repeat(1f, effectiveBuckets).ToArray();
        float[] maxes = Enumerable.Repeat(-1f, effectiveBuckets).ToArray();
        await using FileStream stream = OpenShared(fullPath);
        stream.Position = info.DataOffset;
        byte[] frame = new byte[info.BlockAlign];
        for (long frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(frame, cancellationToken).ConfigureAwait(false);
            if (read < frame.Length) break;
            int bucket = checked((int)Math.Min(effectiveBuckets - 1, frameIndex * effectiveBuckets / Math.Max(1, frameCount)));
            int bytesPerSample = info.BlockAlign / info.Channels;
            for (int channel = 0; channel < info.Channels; channel++)
            {
                float sample = DecodeWaveSample(frame.AsSpan(channel * bytesPerSample, bytesPerSample), info);
                mins[bucket] = Math.Min(mins[bucket], sample);
                maxes[bucket] = Math.Max(maxes[bucket], sample);
            }
        }

        for (int index = 0; index < effectiveBuckets; index++)
        {
            float min = mins[index] > maxes[index] ? 0 : mins[index];
            float max = mins[index] > maxes[index] ? 0 : maxes[index];
            buckets[index] = new WaveformBucket(min, max);
        }

        TimeSpan duration = TimeSpan.FromSeconds(frameCount / (double)info.SampleRate);
        return new WaveformEnvelope(duration, info.SampleRate, info.Channels, buckets);
    }

    private static async ValueTask<ImageAssetMetadata> ReadJpegAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBytes = new byte[2];
        byte[] sof = new byte[5];
        while (stream.Position < stream.Length)
        {
            int prefix = stream.ReadByte();
            if (prefix < 0) break;
            if (prefix != 0xFF) continue;

            int marker;
            do { marker = stream.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker == 0xD9) break;
            if (marker is 0x00 or 0x01 or >= 0xD0 and <= 0xD8) continue;

            await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2 || stream.Position + length - 2 > stream.Length) break;
            bool isStartOfFrame = marker is >= 0xC0 and <= 0xC3
                or >= 0xC5 and <= 0xC7
                or >= 0xC9 and <= 0xCB
                or >= 0xCD and <= 0xCF;
            if (!isStartOfFrame)
            {
                stream.Position += length - 2;
                continue;
            }

            if (length < 7) break;
            await ReadExactlyAsync(stream, sof, cancellationToken).ConfigureAwait(false);
            int bitDepth = sof[0];
            int height = BinaryPrimitives.ReadUInt16BigEndian(sof.AsSpan(1, 2));
            int width = BinaryPrimitives.ReadUInt16BigEndian(sof.AsSpan(3, 2));
            return new ImageAssetMetadata(width, height, bitDepth, false, "JPEG", width > 0 && height > 0);
        }

        return new ImageAssetMetadata(0, 0, 0, false, "JPEG", false, "JPEG dimensions could not be located.");
    }

    private static async ValueTask<AudioAssetMetadata> ReadWaveAsync(string fullPath, CancellationToken cancellationToken)
    {
        WaveInfo info = await ReadWaveInfoAsync(fullPath, cancellationToken).ConfigureAwait(false);
        long frames = info.DataLength / Math.Max(1, (int)info.BlockAlign);
        TimeSpan duration = info.SampleRate > 0 ? TimeSpan.FromSeconds(frames / (double)info.SampleRate) : TimeSpan.Zero;
        long bitRate = checked((long)info.SampleRate * info.BlockAlign * 8);
        return new AudioAssetMetadata(duration, info.Channels, info.SampleRate, info.BitsPerSample, bitRate, null, null, "WAV", true);
    }

    private static async ValueTask<WaveInfo> ReadWaveInfoAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenShared(fullPath);
        byte[] header = new byte[12];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("The file is not a RIFF/WAVE stream.");
        }

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bits = 0;
        long dataOffset = -1;
        long dataLength = 0;
        byte[] chunkHeader = new byte[8];
        while (stream.Position + 8 <= stream.Length)
        {
            await ReadExactlyAsync(stream, chunkHeader, cancellationToken).ConfigureAwait(false);
            string id = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            int length = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            if (length < 0 || stream.Position + length > stream.Length + 1) throw new InvalidDataException("Invalid WAV chunk length.");
            if (id == "fmt ")
            {
                byte[] fmt = new byte[Math.Min(length, 64)];
                await ReadExactlyAsync(stream, fmt, cancellationToken).ConfigureAwait(false);
                if (fmt.Length < 16) throw new InvalidDataException("The WAV fmt chunk is incomplete.");
                format = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(0, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4, 4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(12, 2));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14, 2));
                stream.Position += length - fmt.Length;
            }
            else if (id == "data")
            {
                dataOffset = stream.Position;
                dataLength = length;
                stream.Position += length;
            }
            else
            {
                stream.Position += length;
            }

            if ((length & 1) != 0 && stream.Position < stream.Length) stream.Position++;
        }

        if (format == 0 || channels == 0 || sampleRate <= 0 || blockAlign == 0 || dataOffset < 0)
        {
            throw new InvalidDataException("The WAV stream is missing required format or data chunks.");
        }

        return new WaveInfo(format, channels, sampleRate, blockAlign, bits, dataOffset, dataLength);
    }

    private static async ValueTask<AudioAssetMetadata> ReadOggAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenShared(fullPath);
        byte[] first = await ReadPrefixAsync(stream, 64 * 1024, cancellationToken).ConfigureAwait(false);
        if (first.Length < 32 || !first.AsSpan(0, 4).SequenceEqual("OggS"u8))
        {
            throw new InvalidDataException("The file is not an Ogg stream.");
        }

        int channels = 0;
        int sampleRate = 0;
        int vorbis = IndexOf(first, new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
        if (vorbis >= 0 && vorbis + 16 <= first.Length)
        {
            channels = first[vorbis + 11];
            sampleRate = BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(vorbis + 12, 4));
        }

        Dictionary<string, string> comments = ParseOggComments(first);
        long? loopStart = TryParseLong(comments, "LOOPSTART");
        long? loopLength = TryParseLong(comments, "LOOPLENGTH");
        if (!loopLength.HasValue)
        {
            long? loopEnd = TryParseLong(comments, "LOOPEND");
            if (loopStart.HasValue && loopEnd.HasValue && loopEnd.Value >= loopStart.Value) loopLength = loopEnd.Value - loopStart.Value;
        }

        long lastGranule = await ReadLastOggGranuleAsync(stream, cancellationToken).ConfigureAwait(false);
        TimeSpan? duration = sampleRate > 0 && lastGranule >= 0 ? TimeSpan.FromSeconds(lastGranule / (double)sampleRate) : null;
        long? bitRate = duration.HasValue && duration.Value.TotalSeconds > 0
            ? checked((long)(new FileInfo(fullPath).Length * 8 / duration.Value.TotalSeconds))
            : null;
        return new AudioAssetMetadata(duration, channels > 0 ? channels : null, sampleRate > 0 ? sampleRate : null, null, bitRate, loopStart, loopLength, "Ogg Vorbis", true, comments);
    }

    private static Dictionary<string, string> ParseOggComments(byte[] bytes)
    {
        Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);
        int marker = IndexOf(bytes, new byte[] { 3, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' });
        if (marker < 0 || marker + 11 >= bytes.Length) return comments;
        int position = marker + 7;
        if (position + 4 > bytes.Length) return comments;
        int vendorLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
        position += 4 + Math.Max(0, vendorLength);
        if (position + 4 > bytes.Length) return comments;
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
        position += 4;
        for (int index = 0; index < count && position + 4 <= bytes.Length; index++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
            position += 4;
            if (length < 0 || position + length > bytes.Length) break;
            string value = Encoding.UTF8.GetString(bytes, position, length);
            position += length;
            int equals = value.IndexOf('=');
            if (equals > 0) comments[value[..equals]] = value[(equals + 1)..];
        }

        return comments;
    }

    private static async ValueTask<long> ReadLastOggGranuleAsync(FileStream stream, CancellationToken cancellationToken)
    {
        const int tailSize = 256 * 1024;
        long start = Math.Max(0, stream.Length - tailSize);
        stream.Position = start;
        byte[] tail = new byte[checked((int)(stream.Length - start))];
        await ReadExactlyAsync(stream, tail, cancellationToken).ConfigureAwait(false);
        for (int index = tail.Length - 27; index >= 0; index--)
        {
            if (!tail.AsSpan(index, 4).SequenceEqual("OggS"u8)) continue;
            return BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(index + 6, 8));
        }

        return -1;
    }

    private static long? TryParseLong(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out string? text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : null;

    private static async ValueTask<string?> TryReadSfntFamilyAsync(FileStream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        byte[] offset = new byte[12];
        await ReadExactlyAsync(stream, offset, cancellationToken).ConfigureAwait(false);
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(offset.AsSpan(4, 2));
        byte[] record = new byte[16];
        int nameOffset = -1;
        int nameLength = 0;
        for (int index = 0; index < tableCount; index++)
        {
            await ReadExactlyAsync(stream, record, cancellationToken).ConfigureAwait(false);
            if (!record.AsSpan(0, 4).SequenceEqual("name"u8)) continue;
            nameOffset = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(8, 4));
            nameLength = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(12, 4));
        }

        if (nameOffset < 0 || nameLength < 6 || nameOffset + nameLength > stream.Length) return null;
        stream.Position = nameOffset;
        byte[] nameHeader = new byte[6];
        await ReadExactlyAsync(stream, nameHeader, cancellationToken).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadUInt16BigEndian(nameHeader.AsSpan(2, 2));
        int storageOffset = BinaryPrimitives.ReadUInt16BigEndian(nameHeader.AsSpan(4, 2));
        byte[] nameRecord = new byte[12];
        for (int index = 0; index < count; index++)
        {
            await ReadExactlyAsync(stream, nameRecord, cancellationToken).ConfigureAwait(false);
            int platform = BinaryPrimitives.ReadUInt16BigEndian(nameRecord.AsSpan(0, 2));
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(nameRecord.AsSpan(6, 2));
            int length = BinaryPrimitives.ReadUInt16BigEndian(nameRecord.AsSpan(8, 2));
            int relative = BinaryPrimitives.ReadUInt16BigEndian(nameRecord.AsSpan(10, 2));
            if (nameId != 1 || length <= 0) continue;
            long returnPosition = stream.Position;
            stream.Position = nameOffset + storageOffset + relative;
            byte[] text = new byte[length];
            await ReadExactlyAsync(stream, text, cancellationToken).ConfigureAwait(false);
            stream.Position = returnPosition;
            return platform is 0 or 3 ? Encoding.BigEndianUnicode.GetString(text).TrimEnd('\0') : Encoding.UTF8.GetString(text).TrimEnd('\0');
        }

        return null;
    }

    private static float DecodeWaveSample(ReadOnlySpan<byte> sampleBytes, WaveInfo info)
    {
        if (info.AudioFormat == 3 && info.BitsPerSample == 32 && sampleBytes.Length >= 4)
        {
            return Math.Clamp(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sampleBytes[..4])), -1f, 1f);
        }

        return info.BitsPerSample switch
        {
            8 when sampleBytes.Length >= 1 => (sampleBytes[0] - 128) / 128f,
            16 when sampleBytes.Length >= 2 => BinaryPrimitives.ReadInt16LittleEndian(sampleBytes[..2]) / 32768f,
            24 when sampleBytes.Length >= 3 => DecodeInt24(sampleBytes[..3]) / 8388608f,
            32 when sampleBytes.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(sampleBytes[..4]) / 2147483648f,
            _ => 0f
        };
    }

    private static int DecodeInt24(ReadOnlySpan<byte> bytes)
    {
        int value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async ValueTask<int> ReadAtLeastAsync(Stream stream, byte[] buffer, int minimum, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < minimum && total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        return total;
    }

    private static async ValueTask<byte[]> ReadPrefixAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[checked((int)Math.Min(maximum, stream.Length - stream.Position))];
        await ReadExactlyAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            total += read;
        }
    }

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        for (int index = 0; index <= source.Length - pattern.Length; index++)
        {
            if (source.AsSpan(index, pattern.Length).SequenceEqual(pattern)) return index;
        }

        return -1;
    }

    private sealed record WaveInfo(ushort AudioFormat, ushort Channels, int SampleRate, ushort BlockAlign, ushort BitsPerSample, long DataOffset, long DataLength);
}
