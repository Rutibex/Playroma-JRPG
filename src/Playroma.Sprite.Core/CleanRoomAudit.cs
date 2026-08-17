using System.Xml.Linq;

namespace Playroma.Sprite.Core;

public sealed record CleanRoomFinding(string Code, string Path, string Message);
public sealed record CleanRoomAuditResult(IReadOnlyList<CleanRoomFinding> Findings)
{
    public bool Success => Findings.Count == 0;
}

public sealed class CleanRoomAudit
{
    private static readonly HashSet<string> ForbiddenFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "aseprite.exe", "aseprite.dll", "aseprite.app", "libaseprite.dll", "libaseprite.so", "libaseprite.dylib"
    };

    private static readonly HashSet<string> RedistributableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".app", ".zip", ".7z", ".rar", ".tar", ".gz", ".png", ".ico", ".aseprite", ".ase"
    };

    private static readonly string[] ForbiddenPathFragments =
    {
        "/vendor/aseprite/", "/third_party/aseprite/", "/third-party/aseprite/", "/extern/aseprite/", "/resources/aseprite/"
    };

    public CleanRoomAuditResult Audit(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string fullRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);
        List<CleanRoomFinding> findings = [];
        foreach (string file in EnumerateAuditableFiles(fullRoot))
        {
            string relative = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            string normalized = "/" + relative + "/";
            string fileName = Path.GetFileName(file);
            if (ForbiddenFileNames.Contains(fileName))
                findings.Add(new("PSPR-CLEAN-001", relative, "Aseprite executable or library redistribution is forbidden."));
            if (ForbiddenPathFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("PSPR-CLEAN-002", relative, "Vendored Aseprite source or resources are forbidden."));
            if (Path.GetFileNameWithoutExtension(fileName).Contains("aseprite", StringComparison.OrdinalIgnoreCase) && RedistributableExtensions.Contains(Path.GetExtension(fileName)))
                findings.Add(new("PSPR-CLEAN-003", relative, "Aseprite-named binary, archive, artwork, or native document redistribution is forbidden."));
            if (Path.GetExtension(file).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                AuditProjectReferences(file, relative, findings);
        }
        return new(findings.Distinct().ToArray());
    }

    private static IEnumerable<string> EnumerateAuditableFiles(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (name is ".git" or "bin" or "obj" or ".vs") continue;
                pending.Push(child);
            }
            foreach (string file in Directory.EnumerateFiles(directory)) yield return file;
        }
    }

    private static void AuditProjectReferences(string file, string relative, List<CleanRoomFinding> findings)
    {
        try
        {
            XDocument document = XDocument.Load(file);
            foreach (XElement reference in document.Descendants().Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference"))
            {
                string include = reference.Attribute("Include")?.Value ?? string.Empty;
                if (include.Contains("aseprite", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new("PSPR-CLEAN-004", relative, "Aseprite code or binary references are forbidden."));
            }
        }
        catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
        {
            findings.Add(new("PSPR-CLEAN-005", relative, "Project file could not be audited: " + exception.Message));
        }
    }
}
