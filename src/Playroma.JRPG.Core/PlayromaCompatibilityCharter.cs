namespace Playroma.JRPG.Core;

public static class PlayromaCompatibilityCharter
{
    public static IReadOnlyList<CompatibilityPromise> Promises { get; } =
    [
        new("source-projects", "Source-project compatibility",
            "Edit complete RPG Maker MV source projects; do not promise decryption or decompilation of protected deployments.",
            CompatibilityGuarantee.Required),
        new("runtime-preservation", "Preserve existing runtimes",
            "Do not replace CoreScript, JavaScript libraries, NW.js, index.html, package.json, or plugins during ordinary editing.",
            CompatibilityGuarantee.Required),
        new("unknown-data", "Unknown data remains valid",
            "Preserve unknown JSON fields, command codes, notes, scripts, files, parameter strings, duplicates, and null slots.",
            CompatibilityGuarantee.Required),
        new("no-silent-repair", "No silent repair",
            "Validation reports problems; repairs require preview, consent, transaction support, and undo.",
            CompatibilityGuarantee.Required),
        new("strict-mode", "Strict MV Mode",
            "Keep Playroma-only metadata outside standard MV JSON and preserve interoperability.",
            CompatibilityGuarantee.Required),
        new("explicit-execution", "Project code is untrusted",
            "Loading does not execute project JavaScript; playtesting is explicit.",
            CompatibilityGuarantee.Required),
        new("proprietary-content", "No proprietary distribution",
            "Official repositories and starter projects include only original or clearly redistributable material.",
            CompatibilityGuarantee.Required),
        new("runtime-migration", "Runtime migration",
            "Runtime upgrades are explicit, separately tested operations.",
            CompatibilityGuarantee.ExplicitOptIn),
        new("deployment-recovery", "Protected deployment recovery",
            "Decrypting or reconstructing protected deployments is outside the compatibility promise.",
            CompatibilityGuarantee.OutOfScope)
    ];
}
