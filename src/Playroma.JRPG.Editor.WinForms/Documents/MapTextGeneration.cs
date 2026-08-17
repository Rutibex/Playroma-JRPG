namespace Playroma.JRPG.Editor.WinForms.Documents;

public sealed record MapTextGenerationRequestedEventArgs(int MapId, string RelativePath, string FieldKey);
