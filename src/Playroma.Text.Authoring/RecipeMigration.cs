using Playroma.Text.Core.Generation;

namespace Playroma.Text.Authoring;

public interface ITextRecipeMigrator
{
    string RecipeId { get; }
    int FromVersion { get; }
    int ToVersion { get; }
    TextRecipe Migrate(TextRecipe recipe);
}

public sealed class TextRecipeMigrationRegistry
{
    private readonly List<ITextRecipeMigrator> _migrators = new();
    public void Register(ITextRecipeMigrator migrator) => _migrators.Add(migrator ?? throw new ArgumentNullException(nameof(migrator)));

    public TextRecipe MigrateToLatest(TextRecipe recipe)
    {
        var current = recipe;
        while (true)
        {
            var next = _migrators.Where(m => string.Equals(m.RecipeId, current.Id, StringComparison.OrdinalIgnoreCase) && m.FromVersion == current.Version)
                .OrderBy(m => m.ToVersion).FirstOrDefault();
            if (next is null) return current;
            if (next.ToVersion <= next.FromVersion) throw new InvalidOperationException("Recipe migrators must increase the version.");
            current = next.Migrate(current);
        }
    }
}
