using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;
using Playroma.Text.Generators.Transforms;
using Playroma.Text.Generators.Names;
using Playroma.Text.Generators.Tracery;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Generators.Markov;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Generators.Dialogue;
using Playroma.Text.Core.AI;

namespace Playroma.Text.Generators;

public static class GeneratorCatalog
{
    public static GenerationStageRegistry CreatePhase0Registry() => new();

    public static GenerationStageRegistry CreatePhase1Registry(TextTransformRegistry? transforms = null)
    {
        transforms ??= TextTransformRegistry.CreateDefault();
        var registry = new GenerationStageRegistry();
        registry.Register(new RandomListStage());
        registry.Register(new WeightedTableStage());
        registry.Register(new NestedTableStage());
        registry.Register(new ShuffleBagStage());
        registry.Register(new TemplateStage(transforms));
        registry.Register(new AdLibStage(transforms));
        registry.Register(new AffixComposeStage(transforms));
        registry.Register(new TransformStage(transforms));
        registry.Register(new ConstraintStage());
        return registry;
    }

    public static GenerationStageRegistry CreatePhase2Registry(TextTransformRegistry? transforms = null, LanguageProfileRegistry? profiles = null)
    {
        var registry = CreatePhase1Registry(transforms);
        profiles ??= LanguageProfileRegistry.CreateBuiltIn();
        var nameEngine = new NameGenerationEngine(profiles);
        registry.Register(new NameGeneratorStage(nameEngine));
        registry.Register(new NameSetStage(nameEngine));
        registry.Register(new NameCollisionStage());
        return registry;
    }

    public static GenerationStageRegistry CreatePhase3Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null)
    {
        var registry = CreatePhase2Registry(transforms, profiles);
        registry.Register(new TraceryGrammarStage(new TraceryEngine(traceryModifiers), grammars));
        return registry;
    }
    public static GenerationStageRegistry CreatePhase5Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null)
    {
        var registry = CreatePhase3Registry(transforms, profiles, traceryModifiers, grammars);
        registry.Register(new MarkovGenerationStage(corpora, markovCache));
        return registry;
    }

    public static GenerationStageRegistry CreatePhase6Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null)
    {
        var registry = CreatePhase5Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache);
        registry.Register(new SemanticAnnotateStage());
        registry.Register(new FeatureUnifyStage());
        registry.Register(new SemanticConstraintStage());
        registry.Register(new AgreementStage());
        registry.Register(new SemanticScoreStage());
        return registry;
    }

    /// <summary>
    /// Phase 7 adds host/editor integration rather than new generation algorithms. This alias keeps
    /// host composition explicit while preserving the complete Phase 6 generator surface.
    /// </summary>
    public static GenerationStageRegistry CreatePhase7Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null) =>
        CreatePhase6Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache);

    /// <summary>Phase 8 adds RPG Maker MV target/import integration, not a new stochastic engine.</summary>
    public static GenerationStageRegistry CreatePhase8Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null) =>
        CreatePhase7Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache);

    /// <summary>Phase 9 adds map/event/message compilation and host integration, not a new stochastic engine.</summary>
    public static GenerationStageRegistry CreatePhase9Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null) =>
        CreatePhase8Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache);

    /// <summary>Phase 10 adds reusable character-voice realization as a composable generation stage.</summary>
    public static GenerationStageRegistry CreatePhase10Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null,
        IDialogueVoiceResolver? voices = null)
    {
        var registry = CreatePhase9Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache);
        registry.Register(new DialogueVoiceStage(new DialogueVoiceProfileRegistry(voices)));
        return registry;
    }


    /// <summary>Phase 11 adds storylet selection/realization above the existing generation stack.</summary>
    public static GenerationStageRegistry CreatePhase11Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null,
        IDialogueVoiceResolver? voices = null) =>
        CreatePhase10Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache, voices);

    /// <summary>Phase 12 adds quest grammar/planning above the existing generation stack.</summary>
    public static GenerationStageRegistry CreatePhase12Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null,
        IDialogueVoiceResolver? voices = null) =>
        CreatePhase11Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache, voices);

    /// <summary>Phase 13 adds batch orchestration/UI above the existing generation stack.</summary>
    public static GenerationStageRegistry CreatePhase13Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null,
        IDialogueVoiceResolver? voices = null) =>
        CreatePhase12Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache, voices);

    /// <summary>Phase 14 adds advanced candidate search and an optional neural-provider stage.</summary>
    public static GenerationStageRegistry CreatePhase14Registry(
        TextTransformRegistry? transforms = null,
        LanguageProfileRegistry? profiles = null,
        TraceryModifierRegistry? traceryModifiers = null,
        ITraceryGrammarResolver? grammars = null,
        ICorpusResolver? corpora = null,
        IMarkovModelCache? markovCache = null,
        IDialogueVoiceResolver? voices = null,
        INeuralTextProviderResolver? neuralProviders = null)
    {
        var registry = CreatePhase13Registry(transforms, profiles, traceryModifiers, grammars, corpora, markovCache, voices);
        registry.Register(new NeuralTextStage(neuralProviders));
        return registry;
    }

}

/// <summary>A tiny diagnostic stage used by tests and host smoke checks; not a user-facing generator.</summary>
public sealed class EchoDiagnosticStage : ITextGenerationStage
{
    public const string StageKind = "core.echo-diagnostic";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = context.Context.Get<string>("text") ?? string.Empty;
        var bundle = context.WorkingBundle with { PrimaryText = text };
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle.WithField("text", text, "diagnostic", true)));
    }
}
