namespace Playroma.JRPG.Documents.Json;

public enum JsonEditKind
{
    ReplaceValue,
    InsertProperty,
    RemoveProperty,
    InsertArrayElement,
    RemoveArrayElement,
    ReplaceSubtree
}

public sealed record JsonTextEdit(
    TextSpan OriginalSpan,
    byte[] Replacement,
    JsonPath TargetPath,
    JsonEditKind Kind)
{
    public bool IsInsertion => OriginalSpan.Length == 0;
}
