namespace Playroma.Sprite.Automation;

public sealed class SafeAutomationPaths
{
    private readonly string workspaceRoot;
    private readonly StringComparison comparison;

    public SafeAutomationPaths(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        this.workspaceRoot = EnsureTrailingSeparator(Path.GetFullPath(workspaceRoot));
        comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public string WorkspaceRoot => workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string ResolveRead(string path)
    {
        string resolved = Resolve(path);
        if (!File.Exists(resolved) && !Directory.Exists(resolved)) throw new FileNotFoundException("Automation input does not exist inside the workspace.", resolved);
        return resolved;
    }

    public string ResolveWrite(string path)
    {
        string resolved = Resolve(path);
        string? parent = Path.GetDirectoryName(resolved);
        if (string.IsNullOrWhiteSpace(parent)) throw new InvalidOperationException("The output path has no parent directory.");
        return resolved;
    }

    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new InvalidDataException("The automation path contains invalid characters.");
        string candidate = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(WorkspaceRoot, path));
        string guarded = EnsureTrailingSeparator(candidate);
        if (!guarded.StartsWith(workspaceRoot, comparison) && !string.Equals(candidate, WorkspaceRoot, comparison))
            throw new UnauthorizedAccessException($"Automation path escapes the workspace: {path}");
        RejectReparseTraversal(candidate);
        return candidate;
    }

    private void RejectReparseTraversal(string candidate)
    {
        string current = WorkspaceRoot;
        string relative = Path.GetRelativePath(WorkspaceRoot, candidate);
        foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) continue;
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException($"Automation paths cannot traverse symbolic links or junctions: {current}");
        }
    }

    private static string EnsureTrailingSeparator(string path) => path.EndsWith(Path.DirectorySeparatorChar)
        ? path
        : path + Path.DirectorySeparatorChar;
}

public static class CapabilityGuard
{
    public static void Demand(SpriteAutomationContext context, SpriteAutomationCapability required, string operation)
    {
        if ((context.GrantedCapabilities & required) != required)
            throw new UnauthorizedAccessException($"Automation operation '{operation}' requires capability {required}.");
    }
}
