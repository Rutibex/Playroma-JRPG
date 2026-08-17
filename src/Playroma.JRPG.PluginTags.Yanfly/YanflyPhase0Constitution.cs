namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed record YanflyConstitutionPromise(
    string Id,
    string Title,
    string Guarantee);

public static class YanflyPhase0Constitution
{
    public const int SchemaVersion = 1;
    public const string ProviderId = "YEP-MV";
    public const string RuntimeFamily = "RPG Maker MV";

    public static IReadOnlyList<YanflyConstitutionPromise> Promises { get; } =
    [
        new("notes-source-of-truth", "Notes remain authoritative", "Visual authoring never replaces the note field as the canonical project data."),
        new("lossless-unknown-data", "Unknown syntax is preserved", "Unsupported tags, comments, whitespace, casing, and handwritten prose must survive unchanged."),
        new("surface-correctness", "Syntax goes to the correct surface", "Database notes, map notes, event notes, page comments, commands, routes, and text codes are never conflated."),
        new("installed-version-truth", "Installed source wins", "Generated syntax is validated against the plugin version actually present in the user's project whenever available."),
        new("no-source-redistribution", "No YEP source redistribution", "Playroma ships independently written metadata, hashes, and short syntax schemas, never YEP JavaScript or copied help files."),
        new("deterministic-normalization", "Bundle normalization is deterministic", "The same source bundle produces the same canonical plugin selection and metadata apart from the report timestamp."),
        new("explicit-conflicts", "Conflicts are explicit", "Differing files with the same plugin filename require a recorded resolution and may not be silently collapsed."),
        new("complete-classification", "Every supplied plugin is classified", "Each unique plugin receives at least one coverage mode, even when its only role is configuration or runtime validation."),
        new("evidence-backed-dependencies", "Dependencies carry evidence", "Dependency edges record their source location and whether they are hard requirements or feature-specific integrations."),
        new("provider-neutral-future", "The framework stays provider-neutral", "Yanfly is the first provider; core contracts must not prevent later Olivia, Irina, VisuStella, or custom catalogs."),
        new("undoable-edits", "All future edits are undoable", "Every generated metadata change must integrate with Playroma's command and undo infrastructure."),
        new("safe-script-handling", "Lunatic code is not trusted", "JavaScript and formula blocks are never executed in the editor process and are validated only in an isolated sandbox.")
    ];

    public static IReadOnlyList<string> Phase0ExitConditions { get; } =
    [
        "The supplied archive is normalized into unique plugin records and source occurrences.",
        "Versions and SHA-256 fingerprints are recorded without copying plugin source.",
        "Exact and conflicting duplicates are distinguished.",
        "Every plugin has authoring-surface signals and at least one coverage mode.",
        "An initial evidence-backed dependency graph is generated.",
        "Known conflicting duplicates have explicit canonical resolutions.",
        "A machine-readable golden inventory and human-readable audit report are checked in.",
        "Automated tests enforce counts, resolution rules, classification completeness, and licensing boundaries."
    ];

    public static IReadOnlyList<string> ProhibitedRepositoryPatterns { get; } =
    [
        "YEP_*.js",
        "KELYEP_*.js",
        "copied full @help blocks",
        "embedded third-party plugin archives"
    ];
}
