using System.Buffers.Binary;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed class GifAnimation
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int LoopCount { get; init; }
    public List<AnimationFrameImage> Frames { get; } = [];
}

public static class GifCodec
{
    public static GifAnimation Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static GifAnimation Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        string signature = new(reader.ReadChars(6));
        if (signature is not ("GIF87a" or "GIF89a")) throw new InvalidDataException("The file is not a GIF image.");
        int width = reader.ReadUInt16();
        int height = reader.ReadUInt16();
        if (width <= 0 || height <= 0) throw new InvalidDataException("GIF dimensions must be positive.");
        byte packed = reader.ReadByte();
        bool hasGlobalTable = (packed & 0x80) != 0;
        int globalSize = 1 << ((packed & 0x07) + 1);
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        Rgba32[] globalPalette = hasGlobalTable ? ReadPalette(reader, globalSize) : [];

        GifAnimation result = new() { Width = width, Height = height };
        PixelBuffer canvas = new(width, height);
        GifGraphicControl control = new();
        GifGraphicControl previousControl = new();
        IntRect previousRect = default;
        PixelBuffer? previousRestore = null;
        bool hasPrevious = false;
        int loopCount = 0;

        while (stream.Position < stream.Length)
        {
            byte marker = reader.ReadByte();
            if (marker == 0) continue;
            if (marker == 0x3B) break;
            if (marker == 0x21)
            {
                byte label = reader.ReadByte();
                if (label == 0xF9) control = ReadGraphicControl(reader);
                else if (label == 0xFF) loopCount = ReadApplicationExtension(reader, loopCount);
                else SkipSubBlocks(reader);
                continue;
            }
            if (marker != 0x2C) throw new InvalidDataException($"Unsupported GIF block marker 0x{marker:X2}.");

            if (hasPrevious)
            {
                if (previousControl.DisposalMethod == 2) ClearRectangle(canvas, previousRect);
                else if (previousControl.DisposalMethod == 3 && previousRestore is not null) canvas.CopyFrom(previousRestore);
            }

            int left = reader.ReadUInt16();
            int top = reader.ReadUInt16();
            int imageWidth = reader.ReadUInt16();
            int imageHeight = reader.ReadUInt16();
            byte imagePacked = reader.ReadByte();
            bool localTable = (imagePacked & 0x80) != 0;
            bool interlaced = (imagePacked & 0x40) != 0;
            int localSize = 1 << ((imagePacked & 0x07) + 1);
            Rgba32[] palette = localTable ? ReadPalette(reader, localSize) : globalPalette;
            if (palette.Length == 0) throw new InvalidDataException("GIF image has no color table.");
            byte minimumCodeSize = reader.ReadByte();
            byte[] compressed = ReadSubBlocks(reader);
            byte[] indexes = DecodeLzw(compressed, minimumCodeSize, checked(imageWidth * imageHeight));
            if (interlaced) indexes = Deinterlace(indexes, imageWidth, imageHeight);

            PixelBuffer? restore = control.DisposalMethod == 3 ? canvas.Clone() : null;
            for (int y = 0; y < imageHeight; y++)
                for (int x = 0; x < imageWidth; x++)
                {
                    int targetX = left + x;
                    int targetY = top + y;
                    if (targetX < 0 || targetX >= width || targetY < 0 || targetY >= height) continue;
                    byte colorIndex = indexes[y * imageWidth + x];
                    if (control.Transparent && colorIndex == control.TransparentIndex) continue;
                    Rgba32 color = colorIndex < palette.Length ? palette[colorIndex] : Rgba32.Transparent;
                    canvas.SetPixel(targetX, targetY, color with { A = 255 });
                }

            result.Frames.Add(new AnimationFrameImage(canvas.Clone(), Math.Max(10, control.DelayHundredths * 10)));
            previousControl = control;
            previousRect = new IntRect(left, top, imageWidth, imageHeight);
            previousRestore = restore;
            hasPrevious = true;
            control = new GifGraphicControl();
        }
        GifAnimation completed = new() { Width = width, Height = height, LoopCount = loopCount };
        completed.Frames.AddRange(result.Frames);
        if (completed.Frames.Count == 0) throw new InvalidDataException("GIF file contains no image frames.");
        return completed;
    }

    public static void Save(IReadOnlyList<AnimationFrameImage> frames, string path, int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        int width = frames[0].Pixels.Width;
        int height = frames[0].Pixels.Height;
        if (frames.Any(frame => frame.Pixels.Width != width || frame.Pixels.Height != height)) throw new ArgumentException("All GIF frames must have the same dimensions.", nameof(frames));
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Save(frames, stream, loopCount);
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Save(IReadOnlyList<AnimationFrameImage> frames, Stream stream, int loopCount = 0)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(stream);
        if (frames.Count == 0) throw new ArgumentException("At least one frame is required.", nameof(frames));
        int width = frames[0].Pixels.Width;
        int height = frames[0].Pixels.Height;
        if (width is < 1 or > ushort.MaxValue || height is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frames), "GIF dimensions must be between 1 and 65535 pixels.");
        if (frames.Any(frame => frame.Pixels.Width != width || frame.Pixels.Height != height))
            throw new ArgumentException("All GIF frames must have the same dimensions.", nameof(frames));
        PaletteMap palette = BuildPalette(frames);
        using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)0xF7); writer.Write((byte)0); writer.Write((byte)0);
        foreach (Rgba32 color in palette.Colors) { writer.Write(color.R); writer.Write(color.G); writer.Write(color.B); }
        WriteLoopExtension(writer, loopCount);
        foreach (AnimationFrameImage frame in frames)
        {
            WriteGraphicControl(writer, frame.DurationMilliseconds);
            writer.Write((byte)0x2C);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)width); writer.Write((ushort)height); writer.Write((byte)0);
            writer.Write((byte)8);
            byte[] indexes = MapPixels(frame.Pixels, palette);
            WriteSubBlocks(writer, EncodeLiteralLzw(indexes));
        }
        writer.Write((byte)0x3B);
    }

    private static Rgba32[] ReadPalette(BinaryReader reader, int count)
    {
        Rgba32[] colors = new Rgba32[count];
        for (int index = 0; index < count; index++) colors[index] = new Rgba32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
        return colors;
    }

    private static GifGraphicControl ReadGraphicControl(BinaryReader reader)
    {
        int size = reader.ReadByte();
        if (size != 4) throw new InvalidDataException("Invalid GIF graphic-control extension.");
        byte packed = reader.ReadByte();
        int delay = reader.ReadUInt16();
        byte transparentIndex = reader.ReadByte();
        if (reader.ReadByte() != 0) throw new InvalidDataException("Invalid GIF graphic-control terminator.");
        return new GifGraphicControl
        {
            DisposalMethod = (packed >> 2) & 0x07,
            Transparent = (packed & 0x01) != 0,
            DelayHundredths = delay,
            TransparentIndex = transparentIndex
        };
    }

    private static int ReadApplicationExtension(BinaryReader reader, int currentLoopCount)
    {
        int length = reader.ReadByte();
        string identifier = new(reader.ReadChars(length));
        byte[] payload = ReadSubBlocks(reader);
        if ((identifier.StartsWith("NETSCAPE", StringComparison.Ordinal) || identifier.StartsWith("ANIMEXTS", StringComparison.Ordinal)) && payload.Length >= 3 && payload[0] == 1)
            return BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1, 2));
        return currentLoopCount;
    }

    private static byte[] ReadSubBlocks(BinaryReader reader)
    {
        using MemoryStream result = new();
        while (true)
        {
            int length = reader.ReadByte();
            if (length == 0) break;
            byte[] block = reader.ReadBytes(length);
            if (block.Length != length) throw new EndOfStreamException();
            result.Write(block);
        }
        return result.ToArray();
    }

    private static void SkipSubBlocks(BinaryReader reader) => _ = ReadSubBlocks(reader);

    private static byte[] DecodeLzw(byte[] data, int minimumCodeSize, int expectedPixels)
    {
        if (minimumCodeSize is < 2 or > 8) throw new InvalidDataException("Unsupported GIF LZW code size.");
        int clearCode = 1 << minimumCodeSize;
        int endCode = clearCode + 1;
        List<byte[]> dictionary = [];
        int codeSize = minimumCodeSize + 1;
        int bitPosition = 0;
        byte[] output = new byte[expectedPixels];
        int outputIndex = 0;
        byte[]? previous = null;

        void Reset()
        {
            dictionary.Clear();
            for (int index = 0; index < clearCode; index++) dictionary.Add([(byte)index]);
            dictionary.Add([]); dictionary.Add([]);
            codeSize = minimumCodeSize + 1;
            previous = null;
        }
        Reset();
        while (bitPosition + codeSize <= data.Length * 8)
        {
            int code = ReadCode(data, ref bitPosition, codeSize);
            if (code == clearCode) { Reset(); continue; }
            if (code == endCode) break;
            byte[] entry;
            if (code < dictionary.Count && dictionary[code].Length > 0) entry = dictionary[code];
            else if (code == dictionary.Count && previous is not null) entry = previous.Concat([previous[0]]).ToArray();
            else throw new InvalidDataException("Invalid GIF LZW stream.");
            foreach (byte value in entry)
            {
                if (outputIndex >= output.Length) break;
                output[outputIndex++] = value;
            }
            if (previous is not null && dictionary.Count < 4096)
            {
                dictionary.Add(previous.Concat([entry[0]]).ToArray());
                if (dictionary.Count == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            previous = entry;
            if (outputIndex >= output.Length) break;
        }
        if (outputIndex != expectedPixels) throw new InvalidDataException($"GIF frame decoded {outputIndex} pixels; expected {expectedPixels}.");
        return output;
    }

    private static int ReadCode(byte[] bytes, ref int bitPosition, int codeSize)
    {
        int value = 0;
        for (int bit = 0; bit < codeSize; bit++)
        {
            int absolute = bitPosition + bit;
            if ((bytes[absolute >> 3] & (1 << (absolute & 7))) != 0) value |= 1 << bit;
        }
        bitPosition += codeSize;
        return value;
    }

    private static byte[] Deinterlace(byte[] source, int width, int height)
    {
        byte[] result = new byte[source.Length];
        int sourceRow = 0;
        foreach ((int start, int step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            for (int y = start; y < height; y += step)
            {
                Buffer.BlockCopy(source, sourceRow * width, result, y * width, width);
                sourceRow++;
            }
        return result;
    }

    private static void ClearRectangle(PixelBuffer buffer, IntRect rect)
    {
        int right = Math.Min(buffer.Width, rect.Right);
        int bottom = Math.Min(buffer.Height, rect.Bottom);
        for (int y = Math.Max(0, rect.Y); y < bottom; y++)
            for (int x = Math.Max(0, rect.X); x < right; x++) buffer.SetPixel(x, y, Rgba32.Transparent);
    }

    private static PaletteMap BuildPalette(IReadOnlyList<AnimationFrameImage> frames)
    {
        Dictionary<int, int> frequency = [];
        foreach (AnimationFrameImage frame in frames)
            for (int y = 0; y < frame.Pixels.Height; y++)
                for (int x = 0; x < frame.Pixels.Width; x++)
                {
                    Rgba32 color = frame.Pixels.GetPixel(x, y);
                    if (color.A < 128) continue;
                    int key = (color.R << 16) | (color.G << 8) | color.B;
                    frequency[key] = frequency.GetValueOrDefault(key) + 1;
                }
        int[] keys = frequency.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(255).Select(pair => pair.Key).ToArray();
        Rgba32[] colors = new Rgba32[256];
        colors[0] = Rgba32.Transparent;
        for (int index = 0; index < keys.Length; index++) colors[index + 1] = new Rgba32((byte)(keys[index] >> 16), (byte)(keys[index] >> 8), (byte)keys[index]);
        for (int index = keys.Length + 1; index < colors.Length; index++) colors[index] = Rgba32.Black;
        Dictionary<int, byte> exact = [];
        for (int index = 1; index <= keys.Length; index++) exact[keys[index - 1]] = (byte)index;
        return new PaletteMap(colors, exact, keys.Length + 1);
    }

    private static byte[] MapPixels(PixelBuffer pixels, PaletteMap palette)
    {
        byte[] indexes = new byte[pixels.Width * pixels.Height];
        int cursor = 0;
        for (int y = 0; y < pixels.Height; y++)
            for (int x = 0; x < pixels.Width; x++)
            {
                Rgba32 color = pixels.GetPixel(x, y);
                if (color.A < 128) { indexes[cursor++] = 0; continue; }
                int key = (color.R << 16) | (color.G << 8) | color.B;
                if (palette.Exact.TryGetValue(key, out byte exact)) { indexes[cursor++] = exact; continue; }
                int bestDistance = int.MaxValue; byte best = 1;
                for (int index = 1; index < palette.UsedCount; index++)
                {
                    Rgba32 candidate = palette.Colors[index];
                    int dr = color.R - candidate.R, dg = color.G - candidate.G, db = color.B - candidate.B;
                    int distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance) { bestDistance = distance; best = (byte)index; }
                }
                indexes[cursor++] = best;
            }
        return indexes;
    }

    private static byte[] EncodeLiteralLzw(byte[] indexes)
    {
        const int clearCode = 256;
        const int endCode = 257;
        List<int> codes = [clearCode];
        int sinceClear = 0;
        foreach (byte index in indexes)
        {
            codes.Add(index);
            sinceClear++;
            if (sinceClear >= 250) { codes.Add(clearCode); sinceClear = 0; }
        }
        codes.Add(endCode);
        using MemoryStream output = new();
        int bitBuffer = 0;
        int bitCount = 0;
        foreach (int code in codes)
        {
            bitBuffer |= code << bitCount;
            bitCount += 9;
            while (bitCount >= 8)
            {
                output.WriteByte((byte)bitBuffer);
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }
        if (bitCount > 0) output.WriteByte((byte)bitBuffer);
        return output.ToArray();
    }

    private static void WriteLoopExtension(BinaryWriter writer, int loopCount)
    {
        writer.Write((byte)0x21); writer.Write((byte)0xFF); writer.Write((byte)11);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        writer.Write((byte)3); writer.Write((byte)1); writer.Write((ushort)Math.Clamp(loopCount, 0, ushort.MaxValue)); writer.Write((byte)0);
    }

    private static void WriteGraphicControl(BinaryWriter writer, int durationMilliseconds)
    {
        writer.Write((byte)0x21); writer.Write((byte)0xF9); writer.Write((byte)4);
        writer.Write((byte)0x05);
        writer.Write((ushort)Math.Clamp((int)Math.Round(durationMilliseconds / 10d), 1, ushort.MaxValue));
        writer.Write((byte)0); writer.Write((byte)0);
    }

    private static void WriteSubBlocks(BinaryWriter writer, byte[] data)
    {
        for (int offset = 0; offset < data.Length; offset += 255)
        {
            int length = Math.Min(255, data.Length - offset);
            writer.Write((byte)length); writer.Write(data, offset, length);
        }
        writer.Write((byte)0);
    }

    private sealed class GifGraphicControl
    {
        public int DisposalMethod { get; init; }
        public bool Transparent { get; init; }
        public int DelayHundredths { get; init; } = 10;
        public byte TransparentIndex { get; init; }
    }

    private sealed record PaletteMap(Rgba32[] Colors, Dictionary<int, byte> Exact, int UsedCount);
}
