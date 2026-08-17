using Playroma.JRPG.Core;

namespace Playroma.JRPG.Documents;

public sealed class ProjectPathPolicy
{
    private readonly string _rootWithSeparator;
    private readonly StringComparison _comparison;

    public ProjectPathPolicy(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        _rootWithSeparator = Root + Path.DirectorySeparatorChar;
        _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string Root { get; }

    public bool IsWithinRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        return string.Equals(fullPath, Root, _comparison)
            || fullPath.StartsWith(_rootWithSeparator, _comparison);
    }

    public string ResolveRelative(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ProjectPathException(
                DiagnosticCodes.PathOutsideProjectRoot,
                $"A relative project path was required: {relativePath}");
        }

        string fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!IsWithinRoot(fullPath))
        {
            throw new ProjectPathException(
                DiagnosticCodes.PathOutsideProjectRoot,
                $"Path escapes the project root: {relativePath}");
        }

        return fullPath;
    }


    public bool ContainsReparsePoint(
        IReadOnlyProjectFileSystem fileSystem,
        string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!IsWithinRoot(fullPath))
        {
            throw new ProjectPathException(
                DiagnosticCodes.PathOutsideProjectRoot,
                $"Path is outside the project root: {path}");
        }

        if ((fileSystem.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        string relative = Path.GetRelativePath(Root, fullPath);
        if (relative == ".") return false;
        string current = Root;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!fileSystem.FileExists(current) && !fileSystem.DirectoryExists(current))
            {
                break;
            }

            if ((fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public string GetRelativePath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!IsWithinRoot(fullPath))
        {
            throw new ProjectPathException(
                DiagnosticCodes.PathOutsideProjectRoot,
                $"Path is outside the project root: {fullPath}");
        }

        return Path.GetRelativePath(Root, Path.GetFullPath(fullPath)).Replace('\\', '/');
    }
}

public sealed class ProjectPathException : IOException
{
    public ProjectPathException(string diagnosticCode, string message)
        : base(message)
    {
        DiagnosticCode = diagnosticCode;
    }

    public string DiagnosticCode { get; }
}
