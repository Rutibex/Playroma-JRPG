namespace Playroma.JRPG.Documents;

public interface IReadOnlyProjectFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateFiles(
        string directory,
        string searchPattern,
        SearchOption searchOption);

    IEnumerable<string> EnumerateDirectories(string directory);

    ValueTask<byte[]> ReadAllBytesAsync(
        string path,
        CancellationToken cancellationToken);

    FileMetadata GetMetadata(string path);

    FileAttributes GetAttributes(string path);

    string GetFullPath(string path);
}
