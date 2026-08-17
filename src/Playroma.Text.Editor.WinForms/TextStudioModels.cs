using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;
using Playroma.Text.RpgMakerMV;

namespace Playroma.Text.Editor.WinForms;

public sealed record TextStudioOptions(
    string ProjectRoot,
    string ProjectTitle,
    Action? RequestHostClose = null,
    ITextBatchHost? BatchHost = null);

public sealed record TextStudioRecipeEntry(
    TextRecipe Recipe,
    string Category,
    string Source,
    string? Path = null,
    bool IsBuiltIn = false)
{
    public string DisplayName => Recipe.Name;
    public override string ToString() => Recipe.Name;
}

public sealed record TextStudioResultItem(
    Guid Id,
    GeneratedTextBundle Bundle,
    ulong Seed,
    DateTimeOffset CreatedAtUtc,
    bool IsLocked = false,
    bool IsFavorite = false,
    int Variation = 0)
{
    public string Text => Bundle.PrimaryText ?? string.Empty;
}

public sealed record TextStudioFavorite(
    Guid Id,
    string RecipeId,
    string RecipeName,
    string Text,
    ulong Seed,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyDictionary<string, string>? Tags = null,
    IReadOnlyDictionary<string, SemanticFeatureValue>? Features = null);

public sealed record TextStudioHistoryItem(
    Guid Id,
    string RecipeId,
    string RecipeName,
    string Text,
    ulong Seed,
    DateTimeOffset CreatedAtUtc);

public sealed record TextStudioProjectState(
    int Version,
    IReadOnlyList<TextStudioFavorite> Favorites,
    IReadOnlyList<TextStudioHistoryItem> History)
{
    public static TextStudioProjectState Empty { get; } = new(1, Array.Empty<TextStudioFavorite>(), Array.Empty<TextStudioHistoryItem>());
}

public sealed record TextStudioImportRequestedEventArgs(
    TextRecordTargetDescriptor Target,
    TextStudioResultItem Result);
