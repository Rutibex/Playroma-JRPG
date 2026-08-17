using Playroma.JRPG.Core;
using Playroma.JRPG.Testing;

string repositoryRoot = RepositoryRootLocator.Find();
string committedRoot = Path.Combine(repositoryRoot, "testdata", "fixtures");
FixtureCorpusGenerator generator = new();

if (args.Contains("--check", StringComparer.Ordinal))
{
    FixtureCheckResult result = generator.Check(committedRoot, repositoryRoot);
    if (!result.Success)
    {
        Console.Error.WriteLine("Fixture corpus differs from deterministic generator output:");
        foreach (string difference in result.Differences) Console.Error.WriteLine("  - " + difference);
        return 1;
    }

    Console.WriteLine("Fixture corpus is deterministic and current.");
    return 0;
}

string output = committedRoot;
int outputIndex = Array.IndexOf(args, "--output");
if (outputIndex >= 0)
{
    if (outputIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("--output requires a directory.");
        return 2;
    }
    output = Path.GetFullPath(args[outputIndex + 1]);
}

generator.Generate(output, repositoryRoot);
Console.WriteLine("Generated synthetic fixtures at: " + output);
return 0;
