namespace Playroma.JRPG.Specifications;

public sealed record DatabaseFileSpecification(
    string Name,
    string Root,
    string RecordType,
    bool Required = true,
    string? DocumentKind = null);
