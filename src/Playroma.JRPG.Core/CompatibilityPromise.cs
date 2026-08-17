namespace Playroma.JRPG.Core;

public sealed record CompatibilityPromise(
    string Id,
    string Title,
    string Description,
    CompatibilityGuarantee Guarantee);
