namespace Playroma.JRPG.Runtime.MV;

public enum RuntimeLayoutKind
{
    None,
    SourceRuntime,
    BrowserRuntime,
    NwjsDeployment,
    PartialRuntime
}

public sealed record RuntimeFileFingerprint(
    string RelativePath,
    long Size,
    string Sha256);

public sealed record MvRuntimeSnapshot(
    RuntimeLayoutKind Layout,
    string? CoreScriptVersion,
    string? NwjsVersion,
    bool HasIndexHtml,
    bool HasPackageJson,
    bool HasEmbeddedExecutable,
    bool AppearsBrowserCompatible,
    bool AppearsNodeDependent,
    string? RuntimeSignature,
    IReadOnlyList<RuntimeFileFingerprint> Files,
    IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics)
{
    public static MvRuntimeSnapshot Empty { get; } = new(
        RuntimeLayoutKind.None,
        null,
        null,
        false,
        false,
        false,
        false,
        false,
        null,
        [],
        []);
}
