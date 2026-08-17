using Playroma.Text.Core.Tracery;

namespace Playroma.Text.Generators;

public static class BuiltInTraceryGrammarExporter
{
    public static async Task ExportPhase3Async(string directory, bool alsoExportClassic = true, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        foreach (var grammar in BuiltInPhase3Grammars.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safe = string.Concat(grammar.Id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
            await File.WriteAllTextAsync(Path.Combine(directory, safe + ".playtracery.json"), TraceryGrammarJson.SerializePlayroma(grammar), cancellationToken).ConfigureAwait(false);
            if (alsoExportClassic)
                await File.WriteAllTextAsync(Path.Combine(directory, safe + ".tracery.json"), TraceryGrammarJson.SerializeClassic(grammar), cancellationToken).ConfigureAwait(false);
        }
    }
}
