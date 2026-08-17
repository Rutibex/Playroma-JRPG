namespace Playroma.JRPG.Core;

public static class RepositoryRootLocator
{
    public static string Find(string? startDirectory = null)
    {
        DirectoryInfo? current = new(startDirectory ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Playroma.JRPG repository root from " +
            (startDirectory ?? AppContext.BaseDirectory));
    }
}
