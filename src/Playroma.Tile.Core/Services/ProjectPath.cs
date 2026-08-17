using Playroma.Tile.Core.Models;

namespace Playroma.Tile.Core.Services;

public static class ProjectPath
{
    public static string Resolve(TileProject project, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.ProjectDirectory);

        var root = Path.GetFullPath(project.ProjectDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A project path escaped the project directory.");
        }

        return fullPath;
    }

    public static string MakeRelative(TileProject project, string fullPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ToPortableRelativePath(Path.GetRelativePath(project.ProjectDirectory, Path.GetFullPath(fullPath)));
    }


    public static string ToPortableRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("The path must be relative.", nameof(relativePath));
        return relativePath.Replace('\\', '/');
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Tileset" : sanitized;
    }
}
