using System.Text.Json;

namespace Playroma.JRPG.Testing;

public sealed class RepositoryAuditEngine
{
    private static readonly string[] RequiredFiles =
    [
        "LICENSE", "README.md", "THIRD-PARTY-NOTICES.md", "TRADEMARKS.md",
        "CONTRIBUTING.md", "SECURITY.md", "CODE_OF_CONDUCT.md",
        "docs/COMPATIBILITY_CHARTER.md", "docs/PHASE0_ACCEPTANCE.md",
        "docs/PHASE1_ACCEPTANCE.md", "docs/PHASE1_DESIGN.md",
        "provenance/ASSET_PROVENANCE.json", "Playroma.JRPG.sln",
        "docs/PlayromaSprite/PHASE0_PRODUCT_CONSTITUTION.md",
        "docs/PlayromaSprite/CLEAN_ROOM_POLICY.md",
        "PlayromaSpriteAssets/profiles/RpgMakerMvImageProfiles.json"
    ];

    private static readonly string[] ForbiddenExtensions =
    [
        ".rpgmvp", ".rpgmvo", ".rpgmvm", ".pak"
    ];

    private static readonly string[] ForbiddenNames =
    [
        "Game.exe", "nw.pak", "example project.zip",
        "aseprite.exe", "aseprite.dll", "libaseprite.dll", "libaseprite.so", "libaseprite.dylib"
    ];

    private static readonly HashSet<string> AllowedSyntheticArtifactPaths = new(StringComparer.Ordinal)
    {
        "testdata/fixtures/phase1/encrypted-asset-layout/img/pictures/SyntheticEncrypted.rpgmvp"
    };

    private static readonly string[] BinaryExtensionsRequiringProvenance =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".wav", ".ogg", ".m4a",
        ".mp3", ".flac", ".ttf", ".otf", ".woff", ".woff2", ".dll", ".exe"
    ];

    public RepositoryAuditResult Audit(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        List<RepositoryAuditFinding> findings = [];

        foreach (string required in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(repositoryRoot, required)))
            {
                findings.Add(new("PJRPG-REPO-001", "error", required, "Required repository file is missing."));
            }
        }

        HashSet<string> registered = LoadRegisteredAssets(repositoryRoot);
        foreach (string file in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            if (IsBuildOutput(relative)) continue;

            string extension = Path.GetExtension(file);
            string name = Path.GetFileName(file);
            if ((ForbiddenExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                 ForbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                && !AllowedSyntheticArtifactPaths.Contains(relative))
            {
                findings.Add(new("PJRPG-REPO-002", "error", relative, "Known deployed/proprietary RPG Maker artifact is forbidden."));
            }

            if (BinaryExtensionsRequiringProvenance.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
                !registered.Contains(relative))
            {
                findings.Add(new("PJRPG-PROVENANCE-001", "error", relative, "Binary asset is not registered in provenance/ASSET_PROVENANCE.json."));
            }
        }

        AuditAdrs(repositoryRoot, findings);
        AuditSpecifications(repositoryRoot, findings);
        return new RepositoryAuditResult(findings.All(f => f.Severity != "error"), findings);
    }

    private static HashSet<string> LoadRegisteredAssets(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "provenance", "ASSET_PROVENANCE.json");
        if (!File.Exists(path)) return [];
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => asset.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AuditAdrs(string repositoryRoot, List<RepositoryAuditFinding> findings)
    {
        string adrRoot = Path.Combine(repositoryRoot, "docs", "architecture", "adr");
        foreach (string file in Directory.EnumerateFiles(adrRoot, "*.md"))
        {
            string content = File.ReadAllText(file);
            foreach (string heading in new[] { "- Status:", "- Date:", "## Context", "## Decision", "## Consequences" })
            {
                if (!content.Contains(heading, StringComparison.Ordinal))
                {
                    findings.Add(new("PJRPG-ADR-001", "error", Path.GetRelativePath(repositoryRoot, file), "ADR is missing: " + heading));
                }
            }
        }
    }

    private static void AuditSpecifications(string repositoryRoot, List<RepositoryAuditFinding> findings)
    {
        string specificationRoot = Path.Combine(repositoryRoot, "specifications");
        foreach (string file in Directory.EnumerateFiles(specificationRoot, "*.json"))
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(File.ReadAllBytes(file));
            }
            catch (JsonException exception)
            {
                findings.Add(new("PJRPG-SPEC-001", "error", Path.GetRelativePath(repositoryRoot, file), exception.Message));
            }
        }
    }

    private static bool IsBuildOutput(string relative)
    {
        string normalized = "/" + relative.Replace('\\', '/') + "/";
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/TestResults/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RepositoryAuditFinding(string Code, string Severity, string Path, string Message);

public sealed record RepositoryAuditResult(bool Success, IReadOnlyList<RepositoryAuditFinding> Findings);
