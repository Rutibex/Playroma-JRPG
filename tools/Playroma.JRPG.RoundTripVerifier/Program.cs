using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Workspaces;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Playroma.JRPG.RoundTripVerifier <project>");
    return 3;
}

OperationResult<MvProjectWorkspace> opened = await MvProjectWorkspace.OpenAsync(args[0]).ConfigureAwait(false);
if (opened.Value is null)
{
    foreach (ProjectDiagnostic diagnostic in opened.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
    }
    return 2;
}

await using MvProjectWorkspace workspace = opened.Value;
int checkedCount = 0;
int failures = 0;
foreach (ProjectDocumentHandle handle in workspace.Documents)
{
    IMutableProjectDocument document = await workspace.OpenDocumentAsync(handle.RelativePath).ConfigureAwait(false);
    ReadOnlyMemory<byte> generated = await document.GenerateBytesAsync(CancellationToken.None).ConfigureAwait(false);
    checkedCount++;
    if (document.GetOriginalBytes().Span.SequenceEqual(generated.Span)) continue;
    failures++;
    Console.WriteLine("DIFFERENT " + handle.RelativePath);
}

Console.WriteLine($"Verified {checkedCount:N0} documents; {failures:N0} exact-byte failures.");
return failures == 0 ? 0 : 1;
