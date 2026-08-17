using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators.Planning;

public static class BuiltInQuestGrammarExporter
{
    public static async Task ExportPhase12Async(string directory, CancellationToken cancellationToken=default)
    {
        Directory.CreateDirectory(directory);
        foreach(var grammar in BuiltInQuestGrammars.All)
        {
            string safe=string.Concat(grammar.Id.Select(c=>char.IsLetterOrDigit(c)||c is '-' or '_'?c:'-'));
            await File.WriteAllTextAsync(Path.Combine(directory,safe+".playquest.json"),JsonSerializer.Serialize(grammar,RecipeJson.Options),cancellationToken).ConfigureAwait(false);
        }
    }
}
