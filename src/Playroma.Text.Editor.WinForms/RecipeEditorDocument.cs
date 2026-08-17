using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Editor.WinForms;

public sealed class RecipeEditorDocument
{
    public RecipeEditorDocument(TextRecipe recipe, bool isBuiltIn, string? path)
        : this(recipe, recipe, isBuiltIn, path)
    {
    }

    public RecipeEditorDocument(TextRecipe original, TextRecipe working, bool isBuiltIn, string? path)
    {
        Original = original ?? throw new ArgumentNullException(nameof(original));
        Working = working ?? throw new ArgumentNullException(nameof(working));
        IsBuiltIn = isBuiltIn;
        Path = path;
    }

    public TextRecipe Original { get; }
    public TextRecipe Working { get; private set; }
    public bool IsBuiltIn { get; }
    public string? Path { get; }
    public bool IsDirty => Working.Fingerprint() != Original.Fingerprint();

    public void SetMetadata(string id, string name, TextContentKind kind, string? description)
        => Working = Working with { Id = id.Trim(), Name = name.Trim(), Kind = kind, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() };

    public void SetCandidatePipeline(CandidatePipelineDefinition? pipeline)
        => Working = Working with { CandidatePipeline = pipeline };

    public void ReplaceStage(TextRecipeStage stage)
    {
        var stages = Working.Stages.ToList();
        var index = stages.FindIndex(x => string.Equals(x.Id, stage.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) stages[index] = stage; else stages.Add(stage);
        Working = Working with { Stages = stages };
    }

    public void AddStage(string kind)
    {
        var baseId = kind.Replace('.', '-');
        var id = baseId;
        var i = 2;
        while (Working.Stages.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))) id = baseId + "-" + i++;
        var stages = Working.Stages.ToList();
        IReadOnlyDictionary<string, JsonElement> settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(kind, TraceryGrammarStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            var grammar = new TraceryGrammarDocument(
                Working.Id + ".grammar", 1, Working.Name + " Grammar",
                new Dictionary<string, IReadOnlyList<TraceryRuleDefinition>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["origin"] = [new TraceryRuleDefinition("New #subject# text.")],
                    ["subject"] = [new TraceryRuleDefinition("generated")]
                });
            using var grammarJson = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(grammar, false));
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["grammar"] = grammarJson.RootElement.Clone(),
                ["field"] = JsonSerializer.SerializeToElement("text"),
                ["isPrimary"] = JsonSerializer.SerializeToElement(true),
                ["emitMetadataTags"] = JsonSerializer.SerializeToElement(true)
            };
        }
        else if (string.Equals(kind, MarkovGenerationStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["corpusId"] = JsonSerializer.SerializeToElement("custom-corpus"),
                ["mode"] = JsonSerializer.SerializeToElement("word"),
                ["order"] = JsonSerializer.SerializeToElement(2),
                ["minimumOrder"] = JsonSerializer.SerializeToElement(1),
                ["backoff"] = JsonSerializer.SerializeToElement(true),
                ["minimumTokens"] = JsonSerializer.SerializeToElement(6),
                ["maximumTokens"] = JsonSerializer.SerializeToElement(32),
                ["preventExactMatches"] = JsonSerializer.SerializeToElement(true),
                ["maximumSimilarity"] = JsonSerializer.SerializeToElement(.86),
                ["candidateAttempts"] = JsonSerializer.SerializeToElement(64),
                ["field"] = JsonSerializer.SerializeToElement("text"),
                ["isPrimary"] = JsonSerializer.SerializeToElement(true)
            };
        }
        else if (string.Equals(kind, SemanticAnnotateStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["features"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["tone"] = "neutral" })
            };
        }
        else if (string.Equals(kind, FeatureUnifyStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["requirements"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["tone"] = "$context.tone" }),
                ["bindMissing"] = JsonSerializer.SerializeToElement(true),
                ["mode"] = JsonSerializer.SerializeToElement("error")
            };
        }
        else if (string.Equals(kind, SemanticConstraintStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["predicate"] = JsonSerializer.SerializeToElement("feature.tone == context.tone"),
                ["mode"] = JsonSerializer.SerializeToElement("error")
            };
        }
        else if (string.Equals(kind, AgreementStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["target"] = JsonSerializer.SerializeToElement("primary"),
                ["numberFeature"] = JsonSerializer.SerializeToElement("grammar.number"),
                ["personFeature"] = JsonSerializer.SerializeToElement("grammar.person"),
                ["genderFeature"] = JsonSerializer.SerializeToElement("grammar.gender")
            };
        }
        else if (string.Equals(kind, SemanticScoreStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["scorers"] = JsonSerializer.SerializeToElement(new CandidateScorerDefinition[]
                {
                    new("quality", CandidateScorerKind.UniqueWordRatio, 1)
                }, RecipeJson.Options)
            };
        }
        else if (string.Equals(kind, DialogueVoiceStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["profileContextKey"] = JsonSerializer.SerializeToElement("voice.profileId"),
                ["act"] = JsonSerializer.SerializeToElement("SmallTalk"),
                ["fields"] = JsonSerializer.SerializeToElement(new[] { "text" }),
                ["addActFraming"] = JsonSerializer.SerializeToElement(true),
                ["addEmotionFraming"] = JsonSerializer.SerializeToElement(true),
                ["addCatchphrase"] = JsonSerializer.SerializeToElement(true),
                ["applyDialect"] = JsonSerializer.SerializeToElement(true),
                ["applyVocabulary"] = JsonSerializer.SerializeToElement(true)
            };
        }
        else if (string.Equals(kind, NeuralTextStage.StageKind, StringComparison.OrdinalIgnoreCase))
        {
            settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["providerId"] = JsonSerializer.SerializeToElement("local-model"),
                ["prompt"] = JsonSerializer.SerializeToElement("Polish this RPG text while preserving its meaning: {{text}}"),
                ["systemPrompt"] = JsonSerializer.SerializeToElement("Return only the revised text."),
                ["includeProjectContext"] = JsonSerializer.SerializeToElement(false),
                ["maximumOutputCharacters"] = JsonSerializer.SerializeToElement(2000),
                ["field"] = JsonSerializer.SerializeToElement("text"),
                ["isPrimary"] = JsonSerializer.SerializeToElement(true)
            };
        }
        stages.Add(new TextRecipeStage(id, kind, true, settings));
        Working = Working with { Stages = stages };
    }

    public void RemoveStage(string stageId) => Working = Working with { Stages = Working.Stages.Where(x => !string.Equals(x.Id, stageId, StringComparison.OrdinalIgnoreCase)).ToArray() };

    public void MoveStage(string stageId, int delta)
    {
        var stages = Working.Stages.ToList();
        var index = stages.FindIndex(x => string.Equals(x.Id, stageId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, stages.Count - 1);
        if (target == index) return;
        var item = stages[index]; stages.RemoveAt(index); stages.Insert(target, item);
        Working = Working with { Stages = stages };
    }

    public void SetStageSettings(string stageId, string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Stage settings must be a JSON object.");
        var settings = doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var stage = Working.Stages.First(x => string.Equals(x.Id, stageId, StringComparison.OrdinalIgnoreCase));
        ReplaceStage(stage with { Settings = settings });
    }

    public void SetInlineGrammar(string stageId, TraceryGrammarDocument grammar)
    {
        var stage = Working.Stages.First(x => string.Equals(x.Id, stageId, StringComparison.OrdinalIgnoreCase));
        var settings = stage.Settings?.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        using var grammarDoc = JsonDocument.Parse(TraceryGrammarJson.SerializePlayroma(grammar, false));
        settings["grammar"] = grammarDoc.RootElement.Clone();
        settings.Remove("grammarId");
        ReplaceStage(stage with { Settings = settings });
    }

    public void ApplyRawJson(string json)
    {
        Working = JsonSerializer.Deserialize<TextRecipe>(json, RecipeJson.Options)
            ?? throw new JsonException("The recipe JSON was empty.");
    }

    public string ToJson() => JsonSerializer.Serialize(Working, RecipeJson.Options);

    public TextRecipe CreateProjectCopy()
    {
        if (!IsBuiltIn) return Working;
        var id = Working.Id.EndsWith(".custom", StringComparison.OrdinalIgnoreCase) ? Working.Id : Working.Id + ".custom";
        var name = Working.Name.EndsWith(" (Custom)", StringComparison.OrdinalIgnoreCase) ? Working.Name : Working.Name + " (Custom)";
        return Working with { Id = id, Name = name, Version = 1 };
    }

    public TraceryGrammarDocument? TryGetInlineGrammar(string stageId)
    {
        var stage = Working.Stages.FirstOrDefault(x => string.Equals(x.Id, stageId, StringComparison.OrdinalIgnoreCase));
        if (stage?.Settings is null || !string.Equals(stage.Kind, TraceryGrammarStage.StageKind, StringComparison.OrdinalIgnoreCase) ||
            !stage.Settings.TryGetValue("grammar", out var element) || element.ValueKind != JsonValueKind.Object) return null;
        try { return TraceryGrammarJson.Parse(element, Working.Id + ".inline", Working.Name + " Grammar"); }
        catch { return null; }
    }
}
