namespace Playroma.Text.Authoring;

public sealed record TextProjectLayout(string ProjectRoot)
{
    public string DataRoot => Path.Combine(ProjectRoot, "data", "PlayromaText");
    public string Recipes => Path.Combine(DataRoot, "recipes");
    public string Grammars => Path.Combine(DataRoot, "grammars");
    public string Lexicons => Path.Combine(DataRoot, "lexicons");
    public string Cultures => Path.Combine(DataRoot, "cultures");
    public string Corpora => Path.Combine(DataRoot, "corpora");
    public string Dialogue => Path.Combine(DataRoot, "dialogue");
    public string Voices => Path.Combine(DataRoot, "voices");
    public string VoiceAssignments => Path.Combine(Voices, "assignments.json");
    public string Storylets => Path.Combine(DataRoot, "storylets");
    public string Quests => Path.Combine(DataRoot, "quests");
    public string Styles => Path.Combine(DataRoot, "styles");
    public string Studio => Path.Combine(DataRoot, "studio");
    public string AiProviderSettings => Path.Combine(Studio, "ai-providers.json");
    public string ReleaseReports => Path.Combine(Studio, "release");
    public string CacheRoot => Path.Combine(ProjectRoot, ".playroma", "cache", "text");
    public string MarkovCache => Path.Combine(CacheRoot, "markov");

    public IEnumerable<string> SourceDirectories()
    {
        yield return Recipes; yield return Grammars; yield return Lexicons; yield return Cultures;
        yield return Corpora; yield return Dialogue; yield return Voices; yield return Storylets; yield return Quests; yield return Styles; yield return Studio; yield return ReleaseReports;
    }

    public void EnsureCreated()
    {
        foreach (var path in SourceDirectories()) Directory.CreateDirectory(path);
        Directory.CreateDirectory(CacheRoot);
    }
}
