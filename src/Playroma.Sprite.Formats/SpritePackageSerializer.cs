using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Formats;

public sealed class SpritePackageSerializer
{
    public Task SaveAsync(SpriteDocument document, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        SpritePackageData package = new(document);
        foreach (SpritePixelContentDescriptor descriptor in document.PixelContents)
            package.PixelBuffers[descriptor.Id] = new PixelBuffer(descriptor.Width, descriptor.Height);
        return SaveAsync(package, path, cancellationToken);
    }

    public async Task SaveAsync(SpritePackageData package, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SpriteDocument document = package.Document;
        SpriteDocumentValidator.ThrowIfInvalid(document);
        ValidatePixelPayloads(package);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteTextEntryAsync(archive, SpriteFormatVersion.DocumentEntryName, SpriteDocumentJsonSerializer.Serialize(document), cancellationToken);
                PackageManifest manifest = new() { SchemaVersion = SpriteFormatVersion.Current, DocumentId = document.Id, DocumentName = document.Name };
                await WriteTextEntryAsync(archive, SpriteFormatVersion.ManifestEntryName, JsonSerializer.Serialize(manifest, SpriteDocumentJsonSerializer.Options), cancellationToken);
                foreach (SpritePixelContentDescriptor descriptor in document.PixelContents.OrderBy(item => item.Id))
                {
                    PixelBuffer buffer = package.GetRequired(descriptor.Id);
                    ZipArchiveEntry entry = archive.CreateEntry(descriptor.EntryName, CompressionLevel.Optimal);
                    await using Stream target = entry.Open();
                    await target.WriteAsync(buffer.Bytes, cancellationToken);
                }
            }
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<SpriteDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        (await LoadPackageAsync(path, cancellationToken)).Document;

    public async Task<SpritePackageData> LoadPackageAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
        PackageManifest manifest = await ReadManifestAsync(archive, cancellationToken);
        if (manifest.SchemaVersion <= 0 || manifest.SchemaVersion > SpriteFormatVersion.Current)
            throw new InvalidDataException($"Unsupported package schema version {manifest.SchemaVersion}.");
        ZipArchiveEntry entry = archive.GetEntry(SpriteFormatVersion.DocumentEntryName) ?? throw new InvalidDataException("The package has no document.json entry.");
        SpriteDocument document = SpriteDocumentJsonSerializer.Deserialize(await ReadTextEntryAsync(entry, cancellationToken));
        if (manifest.DocumentId != document.Id) throw new InvalidDataException("Package manifest and document identifiers do not match.");
        SpritePackageData package = new(document);
        foreach (SpritePixelContentDescriptor descriptor in document.PixelContents)
        {
            ZipArchiveEntry pixelEntry = archive.GetEntry(descriptor.EntryName) ?? throw new InvalidDataException($"Package pixel entry '{descriptor.EntryName}' is missing.");
            int expected = checked(descriptor.Width * descriptor.Height * 4);
            byte[] bytes = new byte[expected];
            await using Stream source = pixelEntry.Open();
            int read = 0;
            while (read < bytes.Length)
            {
                int count = await source.ReadAsync(bytes.AsMemory(read), cancellationToken);
                if (count == 0) break;
                read += count;
            }
            if (read != expected || source.ReadByte() != -1) throw new InvalidDataException($"Pixel entry '{descriptor.EntryName}' has an invalid length.");
            package.PixelBuffers[descriptor.Id] = PixelBuffer.FromRgbaBytes(descriptor.Width, descriptor.Height, bytes);
        }
        return package;
    }

    private static void ValidatePixelPayloads(SpritePackageData package)
    {
        foreach (SpritePixelContentDescriptor descriptor in package.Document.PixelContents)
        {
            PixelBuffer buffer = package.GetRequired(descriptor.Id);
            if (buffer.Width != descriptor.Width || buffer.Height != descriptor.Height)
                throw new InvalidDataException($"Pixel content '{descriptor.Id}' dimensions do not match its descriptor.");
        }
    }

    private static async Task<PackageManifest> ReadManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.GetEntry(SpriteFormatVersion.ManifestEntryName) ?? throw new InvalidDataException("The package has no manifest.json entry.");
        string json = await ReadTextEntryAsync(entry, cancellationToken);
        return JsonSerializer.Deserialize<PackageManifest>(json, SpriteDocumentJsonSerializer.Options) ?? throw new InvalidDataException("The package manifest is empty.");
    }

    private static async Task<string> ReadTextEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task WriteTextEntryAsync(ZipArchive archive, string name, string content, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await using StreamWriter writer = new(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private sealed class PackageManifest
    {
        public int SchemaVersion { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
    }
}
