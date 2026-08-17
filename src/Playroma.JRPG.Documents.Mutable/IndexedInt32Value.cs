namespace Playroma.JRPG.Documents.Mutable;

public readonly record struct IndexedInt32Value(int Index, int Value, int? ExpectedBefore = null);
