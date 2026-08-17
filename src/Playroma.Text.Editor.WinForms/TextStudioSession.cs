using System.Collections.ObjectModel;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators;
using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Corpora;
using Playroma.Text.Generators.Markov;
using Playroma.Text.Generators.Candidates;
using Playroma.Text.RpgMakerMV;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Authoring.Dialogue;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Authoring.Narrative;
using Playroma.Text.Generators.Narrative;
using Playroma.Text.Core.Planning;
using Playroma.Text.Authoring.Planning;
using Playroma.Text.Generators.Planning;
using Playroma.Text.Core.AI;
using Playroma.Text.Authoring.AI;
using Playroma.Text.Generators.AI;

namespace Playroma.Text.Editor.WinForms;

public sealed class TextStudioSession : IDisposable
{
    private readonly TextStudioRecipeCatalog _catalog;
    private readonly TextStudioStateStore _stateStore;
    private readonly TextGenerationEngine _engine;
    private readonly CandidateGenerationEngine _candidateEngine;
    private readonly NeuralTextProviderRegistry _neuralProviders = new();
    private readonly NeuralProviderSettingsStore _neuralSettingsStore;
    private readonly JsonStoryletStore _storyletStore;
    private readonly CompositeStoryletResolver _storyletResolver;
    private readonly StoryletEngine _storyletEngine = new();
    private readonly StoryletNarrativeEngine _storyletNarrativeEngine;
    private readonly List<StoryletDefinition> _storylets = [];
    private readonly JsonQuestGrammarStore _questStore;
    private readonly CompositeQuestGrammarResolver _questResolver;
    private readonly QuestPlanner _questPlanner = new();
    private readonly QuestRealizationEngine _questRealizationEngine;
    private readonly List<QuestGrammarDefinition> _quests = [];
    private readonly List<TextStudioRecipeEntry> _recipes = [];
    private readonly List<TextStudioResultItem> _results = [];
    private readonly List<TextStudioFavorite> _favorites = [];
    private readonly List<TextStudioHistoryItem> _history = [];
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _generationCts;
    private TextStudioProjectContextSnapshot _projectContext;
    private DialogueVoiceContext? _dialogueContext;
    private bool _disposed;

    public TextStudioSession(TextStudioOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _catalog = new TextStudioRecipeCatalog(options.ProjectRoot);
        _stateStore = new TextStudioStateStore(options.ProjectRoot);
        var layout = new TextProjectLayout(options.ProjectRoot);
        layout.EnsureCreated();
        var corpora = new TextCorpusLibrary(layout.Corpora);
        var markovCache = new JsonMarkovModelCache(layout.MarkovCache);
        var voices = new JsonDialogueVoiceProfileStore(layout.Voices);
        _neuralSettingsStore = new NeuralProviderSettingsStore(options.ProjectRoot);
        _engine = new TextGenerationEngine(GeneratorCatalog.CreatePhase14Registry(corpora: corpora, markovCache: markovCache, voices: voices, neuralProviders: _neuralProviders));
        _candidateEngine = new CandidateGenerationEngine(_engine);
        _storyletStore = new JsonStoryletStore(layout.Storylets);
        _storyletResolver = new CompositeStoryletResolver(BuiltInStorylets.All, _storyletStore);
        _storyletNarrativeEngine = new StoryletNarrativeEngine(_candidateEngine, id => _recipes.FirstOrDefault(x => x.Recipe.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Recipe);
        _questStore = new JsonQuestGrammarStore(layout.Quests);
        _questResolver = new CompositeQuestGrammarResolver(BuiltInQuestGrammars.All, _questStore);
        _questRealizationEngine = new QuestRealizationEngine(_candidateEngine, id => _recipes.FirstOrDefault(x => x.Recipe.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Recipe);
        _projectContext = TextStudioProjectContextSnapshot.CreateBasic(options.ProjectRoot, options.ProjectTitle);
        Seed = SeedFactory.CreateSeed();
    }

    public TextStudioOptions Options { get; }
    public IReadOnlyList<TextStudioRecipeEntry> Recipes => _recipes;
    public IReadOnlyList<TextStudioResultItem> Results => _results;
    public IReadOnlyList<TextStudioFavorite> Favorites => _favorites;
    public IReadOnlyList<TextStudioHistoryItem> History => _history;
    public IReadOnlyList<StoryletDefinition> Storylets => _storylets;
    public IReadOnlyList<QuestGrammarDefinition> QuestGrammars => _quests;
    public IReadOnlyDictionary<string, object?> Parameters => new ReadOnlyDictionary<string, object?>(_parameters);
    public TextStudioRecipeEntry? SelectedRecipe { get; private set; }
    public ulong Seed { get; set; }
    public bool SeedLocked { get; set; }
    public bool IsGenerating { get; private set; }
    public TextStudioProjectContextSnapshot ProjectContext => _projectContext;
    public TextRecordTargetDescriptor? ActiveTarget { get; private set; }
    public DialogueVoiceContext? DialogueContext => _dialogueContext;
    public IReadOnlyList<INeuralTextProvider> NeuralProviders => _neuralProviders.Providers;

    public event EventHandler? RecipesChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ResultsChanged;
    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ReloadNeuralProvidersAsync(cancellationToken).ConfigureAwait(false);
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        _favorites.Clear(); _favorites.AddRange(state.Favorites ?? Array.Empty<TextStudioFavorite>());
        _history.Clear(); _history.AddRange(state.History ?? Array.Empty<TextStudioHistoryItem>());
        await ReloadRecipesAsync(cancellationToken).ConfigureAwait(false);
        await ReloadStoryletsAsync(cancellationToken).ConfigureAwait(false);
        await ReloadQuestGrammarsAsync(cancellationToken).ConfigureAwait(false);
    }


    public async Task<NeuralProviderSettingsDocument> LoadNeuralProviderSettingsAsync(CancellationToken cancellationToken = default)
        => await _neuralSettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task SaveNeuralProviderSettingsAsync(NeuralProviderSettingsDocument document, CancellationToken cancellationToken = default)
    {
        await _neuralSettingsStore.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        await ReloadNeuralProvidersAsync(cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadNeuralProvidersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _neuralSettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        _neuralProviders.Clear();
        foreach (var item in settings.Providers.Where(x => x.Enabled && x.Kind.Equals("process", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(item.ExecutablePath)) continue;
            try
            {
                _neuralProviders.Register(new ProcessNeuralTextProvider(item.Id, item.DisplayName, item.ExecutablePath!, item.Arguments, item.WorkingDirectory, TimeSpan.FromSeconds(Math.Clamp(item.TimeoutSeconds, 1, 300))));
            }
            catch { /* Project Doctor reports malformed provider settings; generation remains offline-safe. */ }
        }
    }

    public async Task ReloadRecipesAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedRecipe?.Recipe.Id;
        var loaded = await _catalog.LoadAsync(cancellationToken).ConfigureAwait(false);
        _recipes.Clear(); _recipes.AddRange(loaded);
        SelectedRecipe = _recipes.FirstOrDefault(x => string.Equals(x.Recipe.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? _recipes.FirstOrDefault();
        ResetParametersForSelection();
        RecipesChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadStoryletsAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _storyletResolver.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _storylets.Clear();
        _storylets.AddRange(loaded);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }


    public async Task ReloadQuestGrammarsAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _questResolver.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _quests.Clear(); _quests.AddRange(loaded); StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public QuestPlan PlanQuest(QuestGrammarDefinition grammar, QuestWorldState state, ulong? seed = null) =>
        _questPlanner.Plan(grammar, BuildContext(), state, seed ?? Seed);

    public async ValueTask<QuestRealizationResult> RealizeQuestAsync(QuestPlan plan, CancellationToken cancellationToken = default) =>
        await _questRealizationEngine.RealizeAsync(plan, BuildContext(), cancellationToken).ConfigureAwait(false);

    public async Task<string> SaveQuestGrammarAsync(QuestGrammarDefinition grammar, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        string path = await _questStore.SaveAsync(grammar, existingPath, cancellationToken).ConfigureAwait(false);
        await ReloadQuestGrammarsAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }

    public void DeleteQuestGrammar(string path) => _questStore.Delete(path);
    public bool IsBuiltInQuestGrammar(string id) => BuiltInQuestGrammars.All.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) && !_questStore.Contains(id);

    public TextStudioResultItem AdoptQuestResult(QuestRealizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string recipeId = result.Plan.Grammar.SummaryRecipeId ?? "phase12.quest.summary";
        SelectRecipeById(recipeId);
        ulong seed = result.Bundle.Provenance?.Seed ?? result.Plan.Seed;
        var item = new TextStudioResultItem(Guid.NewGuid(), result.Bundle, seed, DateTimeOffset.UtcNow);
        _results.Clear(); _results.Add(item); AddHistory(item); ResultsChanged?.Invoke(this, EventArgs.Empty); return item;
    }

    public IReadOnlyList<StoryletEvaluation> EvaluateStorylets(StoryletRuntimeState state, ulong? seed = null) =>
        _storyletEngine.Evaluate(_storylets, BuildContext(), state, seed ?? Seed);

    public async ValueTask<StoryletRealizationResult?> SelectAndRealizeStoryletAsync(StoryletRuntimeState state, ulong? seed = null, CancellationToken cancellationToken = default)
    {
        ulong actualSeed = seed ?? Seed;
        StoryletSelection? selection = _storyletEngine.Select(_storylets, BuildContext(), state, actualSeed);
        if (selection is null) return null;
        return await _storyletNarrativeEngine.RealizeAsync(selection, BuildContext(), state, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<StoryletRealizationResult> RealizeStoryletAsync(StoryletDefinition storylet, StoryletRuntimeState state, ulong? seed = null, CancellationToken cancellationToken = default)
    {
        ulong actualSeed = seed ?? Seed;
        StoryletEvaluation? evaluation = _storyletEngine.Evaluate([storylet], BuildContext(), state, actualSeed).FirstOrDefault();
        if (evaluation is null || !evaluation.Eligible) throw new InvalidOperationException(evaluation?.Reason ?? "Storylet is not eligible.");
        var selection = new StoryletSelection(storylet, evaluation.Score, evaluation.EffectiveBindings, actualSeed, state.Turn);
        return await _storyletNarrativeEngine.RealizeAsync(selection, BuildContext(), state, cancellationToken).ConfigureAwait(false);
    }

    public void ApplyStoryletEffects(StoryletSelection selection, StoryletRuntimeState state) => _storyletEngine.ApplyEffects(selection, state);

    public async Task<string> SaveStoryletAsync(StoryletDefinition storylet, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        string path = await _storyletStore.SaveAsync(storylet, existingPath, cancellationToken).ConfigureAwait(false);
        await ReloadStoryletsAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }

    public TextStudioResultItem AdoptStoryletResult(StoryletRealizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string recipeId = result.Selection.Storylet.Realization?.RecipeId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(recipeId)) SelectRecipeById(recipeId);
        ulong seed = result.Bundle.Provenance?.Seed ?? result.Selection.Seed;
        var item = new TextStudioResultItem(Guid.NewGuid(), result.Bundle, seed, DateTimeOffset.UtcNow);
        _results.Clear();
        _results.Add(item);
        AddHistory(item);
        ResultsChanged?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public void DeleteStorylet(string path) => _storyletStore.Delete(path);

    public bool IsBuiltInStorylet(string id) =>
        BuiltInStorylets.All.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) && !_storyletStore.Contains(id);

    public void SelectRecipe(TextStudioRecipeEntry? entry)
    {
        if (ReferenceEquals(SelectedRecipe, entry)) return;
        SelectedRecipe = entry;
        _results.Clear();
        ResetParametersForSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SelectRecipeById(string? recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return false;
        var entry = _recipes.FirstOrDefault(x => x.Recipe.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        SelectRecipe(entry);
        return true;
    }

    public void UpdateProjectContext(TextStudioProjectContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!PathsEqual(context.ProjectRoot, Options.ProjectRoot))
            throw new InvalidOperationException("Text Studio cannot switch projects in-place. Open a project-scoped Text Studio window for the new project.");
        _projectContext = context;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActivateTarget(TextRecordTargetDescriptor? target)
    {
        ActiveTarget = target;
        if (target?.SelectedField is { } field)
        {
            foreach (string recipeId in field.EffectiveRecommendedRecipeIds)
            {
                if (SelectRecipeById(recipeId)) break;
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetParameter(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _parameters[name] = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyDialogueContext(DialogueVoiceContext? context)
    {
        _dialogueContext = context;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task GenerateAsync(int targetCount, CancellationToken cancellationToken = default)
    {
        if (SelectedRecipe is null) return;
        targetCount = Math.Clamp(targetCount, 1, 100);
        CancelGeneration();
        _generationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _generationCts.Token;
        IsGenerating = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var locked = _results.Where(x => x.IsLocked).Take(targetCount).ToList();
            var fresh = new List<TextStudioResultItem>();
            var usedSeeds = locked.Select(x => x.Seed).ToHashSet();
            var needed = Math.Max(0, targetCount - locked.Count);
            var candidateIndex = 0;
            for (var i = 0; i < needed; i++)
            {
                token.ThrowIfCancellationRequested();
                ulong seed;
                do { seed = NextSeed(SelectedRecipe.Recipe.Id, candidateIndex++); } while (!usedSeeds.Add(seed));
                var references = locked.Select(x => x.Text).Concat(fresh.Select(x => x.Text)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                var bundle = await _candidateEngine.GenerateBestAsync(SelectedRecipe.Recipe, BuildContext(), seed,
                    new GenerationBudgetOptions(MaxTraceNodes: 50_000, MaxOutputCharacters: 100_000, MaxCandidateAttempts: 20_000, MaxElapsed: TimeSpan.FromSeconds(8)), references, token).ConfigureAwait(false);
                var item = new TextStudioResultItem(Guid.NewGuid(), bundle, seed, DateTimeOffset.UtcNow,
                    IsFavorite: _favorites.Any(x => x.RecipeId.Equals(SelectedRecipe.Recipe.Id, StringComparison.OrdinalIgnoreCase) && x.Seed == seed));
                fresh.Add(item);
                AddHistory(item);
            }
            _results.Clear(); _results.AddRange(locked); _results.AddRange(fresh);
            ResultsChanged?.Invoke(this, EventArgs.Empty);
            await SaveStateAsync(token).ConfigureAwait(false);
        }
        finally
        {
            IsGenerating = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SurpriseMeAsync(string? category = null, CancellationToken cancellationToken = default)
    {
        var candidates = _recipes.Where(x => string.IsNullOrWhiteSpace(category) || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) candidates = _recipes.ToArray();
        if (candidates.Length == 0) return;
        var chooser = new Xoshiro256StarStar(SeedLocked ? Seed : SeedFactory.CreateSeed());
        SelectRecipe(candidates[chooser.NextInt(candidates.Length)]);
        if (!SeedLocked) Seed = SeedFactory.CreateSeed();
        await GenerateAsync(10, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TextStudioResultItem?> VaryAsync(Guid resultId, CancellationToken cancellationToken = default)
    {
        var index = _results.FindIndex(x => x.Id == resultId);
        if (index < 0 || SelectedRecipe is null) return null;
        var source = _results[index];
        var variation = source.Variation + 1;
        var seed = SeedFactory.Derive(source.Seed, "variation:" + variation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var references = _results.Where((_, i) => i != index).Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var bundle = await _candidateEngine.GenerateBestAsync(SelectedRecipe.Recipe, BuildContext(), seed,
            new GenerationBudgetOptions(MaxTraceNodes: 50_000, MaxOutputCharacters: 100_000, MaxCandidateAttempts: 20_000, MaxElapsed: TimeSpan.FromSeconds(8)), references, cancellationToken).ConfigureAwait(false);
        var item = new TextStudioResultItem(Guid.NewGuid(), bundle, seed, DateTimeOffset.UtcNow, source.IsLocked, false, variation);
        _results[index] = item;
        AddHistory(item);
        ResultsChanged?.Invoke(this, EventArgs.Empty);
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    public void ToggleLock(Guid resultId)
    {
        var index = _results.FindIndex(x => x.Id == resultId);
        if (index < 0) return;
        _results[index] = _results[index] with { IsLocked = !_results[index].IsLocked };
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleFavoriteAsync(Guid resultId, CancellationToken cancellationToken = default)
    {
        if (SelectedRecipe is null) return;
        var index = _results.FindIndex(x => x.Id == resultId);
        if (index < 0) return;
        var item = _results[index];
        var favoriteIndex = _favorites.FindIndex(x => x.RecipeId.Equals(SelectedRecipe.Recipe.Id, StringComparison.OrdinalIgnoreCase) && x.Seed == item.Seed);
        var isFavorite = favoriteIndex < 0;
        if (favoriteIndex >= 0) _favorites.RemoveAt(favoriteIndex);
        else _favorites.Insert(0, new TextStudioFavorite(Guid.NewGuid(), SelectedRecipe.Recipe.Id, SelectedRecipe.Recipe.Name,
            item.Text, item.Seed, DateTimeOffset.UtcNow, new Dictionary<string, object?>(_parameters), item.Bundle.Tags, item.Bundle.Features));
        _results[index] = item with { IsFavorite = isFavorite };
        ResultsChanged?.Invoke(this, EventArgs.Empty);
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SaveRecipeAsync(TextRecipe recipe, string? existingPath = null, CancellationToken cancellationToken = default)
    {
        var path = await _catalog.SaveProjectRecipeAsync(recipe, existingPath, cancellationToken).ConfigureAwait(false);
        await ReloadRecipesAsync(cancellationToken).ConfigureAwait(false);
        var entry = _recipes.FirstOrDefault(x => x.Path is not null && string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (entry is not null) SelectRecipe(entry);
        return path;
    }

    public TextStudioRecipeEntry? FindRecipe(string? recipeId) =>
        string.IsNullOrWhiteSpace(recipeId) ? null : _recipes.FirstOrDefault(x => x.Recipe.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Phase 13 batch-generation bridge. Extra row/target context is layered above project context without mutating Playground state.</summary>
    public async ValueTask<GeneratedTextBundle> GenerateBatchBundleAsync(
        string recipeId,
        ulong seed,
        IReadOnlyDictionary<string, object?>? extraContext = null,
        IReadOnlyList<string>? diversityReferences = null,
        CancellationToken cancellationToken = default)
    {
        TextStudioRecipeEntry entry = FindRecipe(recipeId) ?? throw new KeyNotFoundException($"Text Studio recipe '{recipeId}' was not found.");
        GenerationContext baseContext = BuildContext();
        var values = new Dictionary<string, object?>(baseContext.Values ?? new Dictionary<string, object?>(), StringComparer.OrdinalIgnoreCase);
        if (extraContext is not null) foreach (var pair in extraContext) values[pair.Key] = pair.Value;
        var context = new GenerationContext(baseContext.ProjectId, baseContext.TargetKind, baseContext.TargetId, values);
        return await _candidateEngine.GenerateBestAsync(entry.Recipe, context, seed,
            new GenerationBudgetOptions(MaxTraceNodes: 50_000, MaxOutputCharacters: 100_000, MaxCandidateAttempts: 20_000, MaxElapsed: TimeSpan.FromSeconds(8)),
            diversityReferences, cancellationToken).ConfigureAwait(false);
    }

    public void CancelGeneration()
    {
        try { _generationCts?.Cancel(); } catch (ObjectDisposedException) { }
        _generationCts?.Dispose();
        _generationCts = null;
    }

    public async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        var state = new TextStudioProjectState(1, _favorites.Take(500).ToArray(), _history.Take(200).ToArray());
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private void ResetParametersForSelection()
    {
        _parameters.Clear();
        if (SelectedRecipe is null) return;
        foreach (var pair in RecipeParameterDiscovery.Discover(SelectedRecipe.Recipe))
            _parameters[pair.Key] = DefaultValue(pair.Value);
    }

    private static object? DefaultValue(RecipeParameterDefinition definition)
    {
        if (definition.DefaultValue is not { } value) return definition.Type switch
        {
            RecipeParameterType.Boolean => false,
            RecipeParameterType.Integer => 0,
            RecipeParameterType.Number => 0d,
            RecipeParameterType.StringList => Array.Empty<string>(),
            RecipeParameterType.Choice => definition.Choices?.FirstOrDefault() ?? string.Empty,
            _ => string.Empty
        };
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => value.EnumerateArray().Select(x => x.ToString()).ToArray(),
            _ => value.ToString()
        };
    }

    private GenerationContext BuildContext()
    {
        var values = new Dictionary<string, object?>(_projectContext.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _parameters) values[pair.Key] = pair.Value;
        values["project.root"] = _projectContext.ProjectRoot;
        values["project.title"] = _projectContext.ProjectTitle;
        if (ActiveTarget is { } target)
        {
            foreach (var pair in target.EffectiveContextValues) values[pair.Key] = pair.Value;
            if (target.SelectedField is { } selected)
            {
                values["target.field"] = selected.SemanticField;
                values["target.current"] = selected.CurrentValue;
                values["target.displayName"] = selected.DisplayName;
            }
            values["target.recordDisplayName"] = target.DisplayName;
        }

        // Explicit dialogue/voice authoring is the most specific context and therefore wins over
        // generic target defaults such as an event name supplied as speaker.name.
        if (_dialogueContext is { } voice)
        {
            if (!string.IsNullOrWhiteSpace(voice.ProfileId)) values["voice.profileId"] = voice.ProfileId;
            if (!string.IsNullOrWhiteSpace(voice.SpeakerName)) values["speaker.name"] = voice.SpeakerName;
            if (!string.IsNullOrWhiteSpace(voice.ListenerName)) values["listener.name"] = voice.ListenerName;
            if (voice.Act is { } act) values["dialogue.act"] = act.ToString();
            if (voice.Emotion is { } emotion) values["voice.emotion"] = emotion.ToString();
            values["relationship.affinity"] = voice.RelationshipAffinity;
            values["relationship.trust"] = voice.RelationshipTrust;
            foreach (var pair in voice.EffectiveState) values["conversation." + pair.Key] = pair.Value;
        }

        if (!values.ContainsKey("storylet.locations") && values.TryGetValue("project.maps", out object? maps) && maps is not null)
            values["storylet.locations"] = maps;
        if (!values.ContainsKey("storylet.characters"))
        {
            var characters = new List<string>();
            if (values.TryGetValue("speaker.name", out object? speaker) && speaker is not null && !string.IsNullOrWhiteSpace(Convert.ToString(speaker))) characters.Add(Convert.ToString(speaker)!);
            if (values.TryGetValue("listener.name", out object? listener) && listener is not null && !string.IsNullOrWhiteSpace(Convert.ToString(listener)) && !characters.Contains(Convert.ToString(listener)!, StringComparer.OrdinalIgnoreCase)) characters.Add(Convert.ToString(listener)!);
            if (characters.Count > 0) values["storylet.characters"] = characters.ToArray();
        }
        if (!values.ContainsKey("storylet.characters")) values["storylet.characters"] = new[] { "Local Elder", "Town Guard", "Traveling Merchant", "Village Healer" };

        // Phase 12 quest grammars reuse the same structured candidate convention as storylets.
        // Host/project-specific sources override these conservative authoring fallbacks.
        if (!values.ContainsKey("quest.items")) values["quest.items"] = new[] { "sealed letter", "old key", "lost ledger", "moonstone", "healing herb", "ancient relic" };
        if (!values.ContainsKey("quest.monsters")) values["quest.monsters"] = new[] { "marsh beast", "road bandit", "ruin guardian", "cave wyrm", "restless spirit" };
        if (!values.ContainsKey("quest.locations") && values.TryGetValue("storylet.locations", out object? questLocations)) values["quest.locations"] = questLocations;
        if (!values.ContainsKey("quest.characters") && values.TryGetValue("storylet.characters", out object? questCharacters)) values["quest.characters"] = questCharacters;

        if (ActiveTarget is { } activeTarget)
            return new GenerationContext(_projectContext.ProjectRoot, activeTarget.AdapterKind, activeTarget.RecordId, values);
        return new GenerationContext(_projectContext.ProjectRoot, Values: values);
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private ulong NextSeed(string recipeId, int index)
    {
        return SeedLocked
            ? SeedFactory.Derive(Seed, $"playground:{recipeId}:{index}")
            : SeedFactory.CreateSeed();
    }

    private void AddHistory(TextStudioResultItem item)
    {
        if (SelectedRecipe is null || string.IsNullOrWhiteSpace(item.Text)) return;
        _history.Insert(0, new TextStudioHistoryItem(Guid.NewGuid(), SelectedRecipe.Recipe.Id, SelectedRecipe.Recipe.Name, item.Text, item.Seed, item.CreatedAtUtc));
        if (_history.Count > 200) _history.RemoveRange(200, _history.Count - 200);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelGeneration();
    }
}
