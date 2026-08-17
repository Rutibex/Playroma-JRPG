using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms;

/// <summary>
/// Platform-neutral snapshot supplied by a host editor. Text Studio only consumes this data;
/// it never mutates the host project through the context bridge.
/// </summary>
public sealed record TextStudioProjectContextSnapshot(
    string ProjectRoot,
    string ProjectTitle,
    long Revision,
    IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<TextStudioProjectContextItem>? Summary = null)
{
    public IReadOnlyList<TextStudioProjectContextItem> EffectiveSummary => Summary ?? [];

    public static TextStudioProjectContextSnapshot CreateBasic(string projectRoot, string projectTitle) =>
        new(projectRoot, projectTitle, 0,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["project.root"] = projectRoot,
                ["project.title"] = projectTitle
            },
            [new("Project", projectTitle), new("Root", projectRoot)]);
}

public sealed record TextStudioProjectContextItem(string Name, string Value, string? Group = null);

public enum TextStudioWorkspaceMode
{
    Playground,
    RecipeDesigner,
    CorpusLab,
    VoiceLab,
    StoryletLab,
    QuestLab,
    BatchBuilder,
    AiProviders,
    ProjectDoctor
}

public sealed record TextStudioWorkspaceState(
    int Version = 1,
    string? SelectedRecipeId = null,
    string? SearchText = null,
    string? Category = null,
    TextStudioWorkspaceMode Mode = TextStudioWorkspaceMode.Playground,
    ulong? Seed = null,
    bool SeedLocked = false);

public sealed record RecipeDesignerRecoveryState(
    TextRecipe Original,
    TextRecipe Working,
    bool IsBuiltIn,
    string? Path);

public sealed record CorpusLabRecoveryState(
    string CorpusJson,
    string? Path);

public sealed record DialogueVoiceLabRecoveryState(
    string ProfileJson,
    string? Path,
    string? BaselineProfileJson = null);

public sealed record StoryletLabRecoveryState(
    string StoryletJson,
    string? Path,
    string? BaselineStoryletJson = null,
    Playroma.Text.Core.Narrative.StoryletRuntimeSnapshot? Runtime = null);

public sealed record QuestLabRecoveryState(
    string QuestJson,
    string? Path,
    string? BaselineQuestJson = null,
    Playroma.Text.Core.Planning.QuestWorldSnapshot? InitialState = null);

public sealed record AiProviderLabRecoveryState(string SettingsJson);

public sealed record TextStudioLocalRecoveryState(
    int Version,
    RecipeDesignerRecoveryState? Recipe,
    CorpusLabRecoveryState? Corpus,
    TextStudioWorkspaceState Workspace,
    DialogueVoiceLabRecoveryState? Voice = null,
    StoryletLabRecoveryState? Storylet = null,
    QuestLabRecoveryState? Quest = null,
    AiProviderLabRecoveryState? AiProviders = null)
{
    public static int CurrentVersion => 5;
}

public sealed class TextStudioStatusChangedEventArgs : EventArgs
{
    public TextStudioStatusChangedEventArgs(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }

    public string Message { get; }
    public bool IsError { get; }
}
