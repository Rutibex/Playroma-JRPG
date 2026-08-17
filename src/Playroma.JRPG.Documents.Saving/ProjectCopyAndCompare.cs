using System.Security.Cryptography;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Documents.Saving;

public enum ProjectFileComparisonKind
{
    IdenticalBytes,
    EquivalentJson,
    DifferentContent,
    MissingFromLeft,
    MissingFromRight
}

public sealed record ProjectFileComparison(
    string RelativePath,
    ProjectFileComparisonKind Kind,
    string? LeftSha256,
    string? RightSha256);

public sealed class ProjectCopyService
{
    public async ValueTask CopyAsync(
        string sourceRoot,
        string destinationRoot,
        IReadOnlyCollection<IMutableProjectDocument> modifiedDocuments,
        ProjectCopyOptions options,
        CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(sourceRoot);
        string destination = Path.GetFullPath(destinationRoot);
        if (string.Equals(destination, source, PathComparison)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidOperationException("The destination must be outside the source project.");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new IOException("The project-copy destination must be empty.");
        }

        Dictionary<string, IMutableProjectDocument> modified = modifiedDocuments.ToDictionary(
            document => Normalize(document.RelativePath),
            StringComparerForPaths);
        HashSet<string> written = new(StringComparerForPaths);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Normalize(Path.GetRelativePath(source, file));
            if (ShouldExclude(relative, options)) continue;
            string target = ResolveDestination(destination, relative);
            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            if (modified.TryGetValue(relative, out var document))
            {
                ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(target, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Copy(file, target, overwrite: false);
            }

            written.Add(relative);
        }

        // Future phases may create new standard MV documents. Include modified documents
        // that did not exist in the source tree rather than silently omitting them.
        foreach (KeyValuePair<string, IMutableProjectDocument> pair in modified.Where(item => !written.Contains(item.Key)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = pair.Key;
            IMutableProjectDocument document = pair.Value;
            if (ShouldExclude(relative, options)) continue;
            string target = ResolveDestination(destination, relative);
            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            ReadOnlyMemory<byte> bytes = await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(target, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            written.Add(relative);
        }

        if (options.VerifyEveryCopiedFile)
        {
            ProjectComparer comparer = new();
            IReadOnlyList<ProjectFileComparison> differences = await comparer.CompareAsync(source, destination, cancellationToken).ConfigureAwait(false);
            string[] unexpected = differences
                .Where(item => !ShouldExclude(item.RelativePath, options))
                .Where(item => !modified.ContainsKey(item.RelativePath))
                .Where(item => item.Kind != ProjectFileComparisonKind.IdenticalBytes)
                .Select(item => item.RelativePath)
                .ToArray();
            if (unexpected.Length > 0)
            {
                throw new IOException("Project copy verification failed for: " + string.Join(", ", unexpected));
            }

            foreach (KeyValuePair<string, IMutableProjectDocument> pair in modified)
            {
                string relative = pair.Key;
                IMutableProjectDocument document = pair.Value;
                if (ShouldExclude(relative, options)) continue;
                string target = ResolveDestination(destination, relative);
                byte[] expected = (await document.GenerateBytesAsync(cancellationToken).ConfigureAwait(false)).ToArray();
                byte[] actual = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    throw new IOException("Modified project-copy output failed verification: " + relative);
                }
            }
        }
    }

    private static bool ShouldExclude(string relative, ProjectCopyOptions options)
    {
        if (!options.IncludePlayromaMetadata && relative.StartsWith(".playroma/", StringComparison.OrdinalIgnoreCase)) return true;
        if (!options.IncludeSaveGames && relative.StartsWith("save/", StringComparison.OrdinalIgnoreCase)) return true;
        return !options.IncludeDeploymentExecutables && Path.GetExtension(relative).Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDestination(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException("A project-copy path escaped the destination root: " + relative);
        }

        return full;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
    private static StringComparer StringComparerForPaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public sealed class ProjectComparer
{
    public async ValueTask<IReadOnlyList<ProjectFileComparison>> CompareAsync(
        string leftRoot,
        string rightRoot,
        CancellationToken cancellationToken = default)
    {
        string left = Path.GetFullPath(leftRoot);
        string right = Path.GetFullPath(rightRoot);
        Dictionary<string, string> leftFiles = Enumerate(left);
        Dictionary<string, string> rightFiles = Enumerate(right);
        List<ProjectFileComparison> result = [];
        foreach (string relative in leftFiles.Keys.Union(rightFiles.Keys, StringComparerForPaths).OrderBy(value => value, StringComparerForPaths))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool hasLeft = leftFiles.TryGetValue(relative, out string? leftPath);
            bool hasRight = rightFiles.TryGetValue(relative, out string? rightPath);
            if (!hasLeft)
            {
                result.Add(new ProjectFileComparison(relative, ProjectFileComparisonKind.MissingFromLeft, null, await HashAsync(rightPath!, cancellationToken).ConfigureAwait(false)));
                continue;
            }
            if (!hasRight)
            {
                result.Add(new ProjectFileComparison(relative, ProjectFileComparisonKind.MissingFromRight, await HashAsync(leftPath!, cancellationToken).ConfigureAwait(false), null));
                continue;
            }

            string leftHash = await HashAsync(leftPath!, cancellationToken).ConfigureAwait(false);
            string rightHash = await HashAsync(rightPath!, cancellationToken).ConfigureAwait(false);
            if (string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ProjectFileComparison(relative, ProjectFileComparisonKind.IdenticalBytes, leftHash, rightHash));
            }
            else if (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && JsonEquivalent(leftPath!, rightPath!))
            {
                result.Add(new ProjectFileComparison(relative, ProjectFileComparisonKind.EquivalentJson, leftHash, rightHash));
            }
            else
            {
                result.Add(new ProjectFileComparison(relative, ProjectFileComparisonKind.DifferentContent, leftHash, rightHash));
            }
        }

        return result;
    }

    private static Dictionary<string, string> Enumerate(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), path => path, StringComparerForPaths);

    private static async ValueTask<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static bool JsonEquivalent(string left, string right)
    {
        try
        {
            byte[] leftBytes = File.ReadAllBytes(left);
            byte[] rightBytes = File.ReadAllBytes(right);
            return Playroma.JRPG.Documents.Json.JsonSemanticDiffer.Compare(leftBytes, rightBytes).Count == 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static StringComparer StringComparerForPaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
