using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed record DatabaseTextGenerationRequestedEventArgs(
    MvDocumentKind Kind,
    string RelativePath,
    int RecordId,
    string? FieldKey = null,
    bool OpenBatchBuilder = false);

public sealed record DirectTextGenerationRequestedEventArgs(TextRecordTargetDescriptor Target);
