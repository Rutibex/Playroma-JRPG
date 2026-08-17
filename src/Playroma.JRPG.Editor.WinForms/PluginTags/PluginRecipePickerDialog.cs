using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class PluginRecipePickerDialog : Form
{
    private readonly MvProjectSession projectSession;
    private readonly PluginTagCatalog catalog;
    private readonly PluginTextEditingSession session;
    private readonly PluginTagRecipeCatalog recipes;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability;
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search recipes…" };
    private readonly ListBox list = new() { Dock = DockStyle.Fill };
    private readonly TextBox description = new() { Dock = DockStyle.Top, Height = 76, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly TextBox preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly Label warnings = new() { Dock = DockStyle.Bottom, Height = 44, ForeColor = Color.DarkGoldenrod };
    private readonly Button customize = new() { Text = "Customize Step…", AutoSize = true };
    private readonly Button apply = new() { Text = "Use Recipe", DialogResult = DialogResult.OK, AutoSize = true };
    private readonly Dictionary<PluginFeatureKey, PluginFeatureValues> overrides = [];
    private IReadOnlyList<PluginTagRecipeDefinition> visible = [];

    public PluginRecipePickerDialog(
        MvProjectSession projectSession,
        PluginTagCatalog catalog,
        PluginTextEditingSession session,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability>? availability = null)
    {
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.availability = availability ?? new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
        recipes = YanflyBeginnerRecipes.Create(catalog);
        Text = "Plugin Tag Recipes";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, 520);
        Size = new Size(980, 680);
        MinimizeBox = false;
        MaximizeBox = false;

        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 300 };
        Panel left = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(list);
        left.Controls.Add(search);
        split.Panel1.Controls.Add(left);
        Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        GroupBox syntax = new() { Text = "Recipe result", Dock = DockStyle.Fill, Padding = new Padding(8) };
        syntax.Controls.Add(preview);
        syntax.Controls.Add(warnings);
        right.Controls.Add(syntax);
        right.Controls.Add(description);
        split.Panel2.Controls.Add(right);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(customize);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = apply;
        CancelButton = cancel;

        search.TextChanged += (_, _) => RefreshList();
        list.SelectedIndexChanged += (_, _) => RefreshPreview();
        customize.Click += (_, _) => CustomizeStep();
        RefreshList();
    }

    public PluginTagRecipePreview SelectedPreview => BuildPreview()
        ?? throw new InvalidOperationException("No recipe is selected.");

    private void RefreshList()
    {
        string? selectedId = SelectedRecipe()?.RecipeId;
        visible = recipes.Query(session.Context, search.Text);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (PluginTagRecipeDefinition recipe in visible) list.Items.Add(recipe.DisplayName);
        list.EndUpdate();
        int index = visible.ToList().FindIndex(recipe => recipe.RecipeId == selectedId);
        list.SelectedIndex = index >= 0 ? index : visible.Count > 0 ? 0 : -1;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        PluginTagRecipePreview? value = BuildPreview();
        if (value is null)
        {
            description.Text = "No recipes match this record type.";
            preview.Text = string.Empty;
            warnings.Text = string.Empty;
            apply.Enabled = customize.Enabled = false;
            return;
        }
        description.Text = value.Recipe.Summary + Environment.NewLine +
            string.Join(Environment.NewLine, value.Recipe.Steps.Select(step => "• " + catalog.GetRequiredFeature(step.Feature).DisplayName));
        preview.Text = value.After;
        warnings.Text = value.Warnings.Count == 0 ? string.Empty : string.Join(Environment.NewLine, value.Warnings);
        bool canUse = value.Recipe.Steps.All(step => IsAuthorable(step.Feature));
        apply.Enabled = value.HasChanges && canUse;
        customize.Enabled = value.Recipe.Steps.Count > 0 && IsAuthorable(value.Recipe.Steps[0].Feature);
    }

    private void CustomizeStep()
    {
        PluginTagRecipeDefinition? recipe = SelectedRecipe();
        if (recipe is null || recipe.Steps.Count == 0) return;
        PluginTagRecipeStep step = recipe.Steps[0];
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(step.Feature);
        PluginFeatureValues values = overrides.TryGetValue(step.Feature, out PluginFeatureValues? current)
            ? current : step.SuggestedValues;
        using PluginFeatureEditorDialog editor = new(projectSession, definition, values, isEditing: true);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        overrides[step.Feature] = editor.Values;
        RefreshPreview();
    }

    private PluginTagRecipeDefinition? SelectedRecipe() =>
        list.SelectedIndex >= 0 && list.SelectedIndex < visible.Count ? visible[list.SelectedIndex] : null;

    private PluginTagRecipePreview? BuildPreview()
    {
        PluginTagRecipeDefinition? recipe = SelectedRecipe();
        if (recipe is null) return null;
        PluginTagRecipePreview value = recipes.Preview(session, recipe, overrides);
        string[] blocked = recipe.Steps
            .Where(step => !IsAuthorable(step.Feature))
            .Select(step => availability[step.Feature].Message)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return blocked.Length == 0 ? value : value with { Warnings = value.Warnings.Concat(blocked).ToArray() };
    }

    private bool IsAuthorable(PluginFeatureKey key) =>
        !availability.TryGetValue(key, out PluginFeatureAvailability? state) || state.CanAuthor;
}
