using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PlayromaMusic.Core.Orchestration;

namespace PlayromaMusic.Audio.SoundFonts;

public sealed class SoundFontPresetCatalogReader
{
    public InstrumentCatalogDocument Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("SoundFont file was not found.", fullPath);
        if (!SoundFontManager.HasSoundFontHeader(fullPath)) throw new InvalidDataException("The file is not a valid RIFF SoundFont bank.");

        using FileStream stream = File.OpenRead(fullPath);
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        string riff = ReadFourCc(reader);
        uint riffSize = reader.ReadUInt32();
        string form = ReadFourCc(reader);
        if (riff != "RIFF" || form != "sfbk") throw new InvalidDataException("The file is not an SF2/SF3 SoundFont.");

        string name = Path.GetFileNameWithoutExtension(fullPath);
        List<PresetHeader> headers = new();
        long riffEnd = Math.Min(stream.Length, 8L + riffSize);
        while (stream.Position + 8 <= riffEnd)
        {
            string id = ReadFourCc(reader);
            uint size = reader.ReadUInt32();
            long chunkStart = stream.Position;
            long chunkEnd = Math.Min(stream.Length, chunkStart + size);
            if (id == "LIST" && size >= 4)
            {
                string listType = ReadFourCc(reader);
                if (listType == "INFO") name = ReadInfoList(reader, chunkEnd, name);
                else if (listType == "pdta") headers.AddRange(ReadPresetData(reader, chunkEnd));
            }
            stream.Position = AlignEven(chunkEnd);
        }

        string fingerprint = ComputeFingerprint(fullPath);
        string soundFontId = "sf-" + fingerprint[..16];
        InstrumentCatalogDocument catalog = new()
        {
            SoundFontId = soundFontId,
            SoundFontName = name,
            SourcePath = fullPath,
            SourceFingerprint = fingerprint,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            IsComplete = headers.Count > 0
        };
        foreach (PresetHeader header in headers.Where(item => !item.Name.Equals("EOP", StringComparison.OrdinalIgnoreCase)))
        {
            bool drums = header.Bank == 128;
            catalog.Presets.Add(PresetCatalogFactory.Classify(
                soundFontId,
                name,
                header.Bank,
                header.Program,
                header.Name,
                drums));
        }
        if (catalog.Presets.Count == 0)
        {
            catalog.Diagnostics.Add("No preset headers were found in the SoundFont pdta/phdr chunk.");
            catalog.IsComplete = false;
        }
        else
        {
            catalog.Diagnostics.Add($"Enumerated {catalog.Presets.Count} presets from the SoundFont preset-header table.");
        }
        return catalog;
    }

    private static string ReadInfoList(BinaryReader reader, long end, string fallback)
    {
        string name = fallback;
        while (reader.BaseStream.Position + 8 <= end)
        {
            string id = ReadFourCc(reader);
            uint size = reader.ReadUInt32();
            long dataStart = reader.BaseStream.Position;
            long dataEnd = Math.Min(end, dataStart + size);
            if (id == "INAM")
            {
                byte[] bytes = reader.ReadBytes(checked((int)Math.Min(size, int.MaxValue)));
                string parsed = Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n', '\t');
                if (!string.IsNullOrWhiteSpace(parsed)) name = parsed;
            }
            reader.BaseStream.Position = AlignEven(dataEnd);
        }
        return name;
    }

    private static IEnumerable<PresetHeader> ReadPresetData(BinaryReader reader, long end)
    {
        List<PresetHeader> result = new();
        while (reader.BaseStream.Position + 8 <= end)
        {
            string id = ReadFourCc(reader);
            uint size = reader.ReadUInt32();
            long dataStart = reader.BaseStream.Position;
            long dataEnd = Math.Min(end, dataStart + size);
            if (id == "phdr")
            {
                const int recordSize = 38;
                int count = checked((int)Math.Min(size / recordSize, 1_000_000));
                for (int index = 0; index < count && reader.BaseStream.Position + recordSize <= dataEnd; index++)
                {
                    string name = Encoding.ASCII.GetString(reader.ReadBytes(20)).TrimEnd('\0', ' ');
                    int program = reader.ReadUInt16();
                    int bank = reader.ReadUInt16();
                    _ = reader.ReadUInt16();
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    result.Add(new PresetHeader(name, bank, program));
                }
            }
            reader.BaseStream.Position = AlignEven(dataEnd);
        }
        return result;
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length != 4) throw new EndOfStreamException("Unexpected end of RIFF chunk header.");
        return Encoding.ASCII.GetString(bytes);
    }

    private static long AlignEven(long position) => (position + 1L) & ~1L;

    private static string ComputeFingerprint(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private readonly record struct PresetHeader(string Name, int Bank, int Program);
}
