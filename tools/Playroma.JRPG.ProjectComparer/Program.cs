using Playroma.JRPG.Documents.Saving;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Playroma.JRPG.ProjectComparer <left-project> <right-project>");
    return 3;
}

IReadOnlyList<ProjectFileComparison> results = await new ProjectComparer()
    .CompareAsync(args[0], args[1])
    .ConfigureAwait(false);
int differences = 0;
foreach (ProjectFileComparison result in results)
{
    if (result.Kind == ProjectFileComparisonKind.IdenticalBytes) continue;
    differences++;
    Console.WriteLine($"{result.Kind,-18} {result.RelativePath}");
}

Console.WriteLine($"Compared {results.Count:N0} paths; {differences:N0} differences.");
return differences == 0 ? 0 : 1;
