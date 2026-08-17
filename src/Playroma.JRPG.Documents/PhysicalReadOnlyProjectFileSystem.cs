namespace Playroma.JRPG.Documents;

public sealed class PhysicalReadOnlyProjectFileSystem : IReadOnlyProjectFileSystem
{
    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    public IEnumerable<string> EnumerateFiles(
        string directory,
        string searchPattern,
        SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        return Directory.EnumerateFiles(directory, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.EnumerateDirectories(directory);
    }

    public async ValueTask<byte[]> ReadAllBytesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > int.MaxValue)
        {
            throw new IOException($"File is too large to load into memory: {path}");
        }

        byte[] bytes = new byte[(int)stream.Length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected end of file while reading: {path}");
            }

            offset += read;
        }

        return bytes;
    }

    public FileMetadata GetMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo info = new(path);
        return new FileMetadata(info.Length, info.LastWriteTimeUtc, info.Attributes);
    }

    public FileAttributes GetAttributes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.GetAttributes(path);
    }

    public string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
