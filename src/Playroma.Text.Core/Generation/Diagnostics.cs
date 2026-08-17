namespace Playroma.Text.Core.Generation;

public enum GenerationDiagnosticSeverity { Info, Warning, Error }

public sealed record GenerationDiagnostic(
    string Code,
    GenerationDiagnosticSeverity Severity,
    string Message,
    string? StageId = null,
    string? Detail = null);

public static class GenerationDiagnosticCodes
{
    public const string UnknownStage = "PTXT0001";
    public const string BudgetExceeded = "PTXT0002";
    public const string Cancelled = "PTXT0003";
    public const string InvalidRecipe = "PTXT0004";
    public const string StageFailed = "PTXT0005";
    public const string InvalidStageSettings = "PTXT0101";
    public const string EmptySelection = "PTXT0102";
    public const string MissingTemplateValue = "PTXT0103";
    public const string UnknownTransform = "PTXT0104";
    public const string ConstraintFailed = "PTXT0105";
    public const string UnknownLanguageProfile = "PTXT0201";
    public const string NameGenerationFailed = "PTXT0202";
    public const string NameCollision = "PTXT0203";
    public const string NameSimilarityRejected = "PTXT0204";
    public const string InvalidNameProfile = "PTXT0205";
    public const string InvalidTraceryGrammar = "PTXT0301";
    public const string TraceryParseError = "PTXT0302";
    public const string TraceryMissingSymbol = "PTXT0303";
    public const string TraceryUnknownModifier = "PTXT0304";
    public const string TraceryActionError = "PTXT0305";
    public const string TraceryRecursionLimit = "PTXT0306";
    public const string TraceryNodeLimit = "PTXT0307";
    public const string TraceryParameterError = "PTXT0308";
    public const string TraceryNoEligibleRules = "PTXT0309";
    public const string TraceryStaticCycle = "PTXT0310";
    public const string TraceryUnknownReference = "PTXT0311";
    public const string MissingCorpus = "PTXT0501";
    public const string InvalidCorpus = "PTXT0502";
    public const string MarkovTrainingFailed = "PTXT0503";
    public const string MarkovGenerationFailed = "PTXT0504";
    public const string MarkovSimilarityRejected = "PTXT0505";
    public const string MarkovCacheError = "PTXT0506";
    public const string InvalidSemanticFeature = "PTXT0601";
    public const string SemanticUnificationConflict = "PTXT0602";
    public const string SemanticConstraintFailed = "PTXT0603";
    public const string InvalidSemanticPredicate = "PTXT0604";
    public const string AgreementFailed = "PTXT0605";
    public const string CandidateRejected = "PTXT0606";
    public const string CandidatePipelineExhausted = "PTXT0607";
    public const string CandidateScoringError = "PTXT0608";
    public const string UnknownVoiceProfile = "PTXT1001";
    public const string InvalidVoiceProfile = "PTXT1002";
    public const string VoiceRealizationFailed = "PTXT1003";
    public const string InvalidStorylet = "PTXT1101";
    public const string StoryletPredicateError = "PTXT1102";
    public const string StoryletBindingFailed = "PTXT1103";
    public const string StoryletSelectionEmpty = "PTXT1104";
    public const string StoryletRealizationFailed = "PTXT1105";
    public const string InvalidQuestGrammar = "PTXT1201";
    public const string QuestBindingFailed = "PTXT1202";
    public const string QuestPlanningFailed = "PTXT1203";
    public const string QuestPlanInvalid = "PTXT1204";
    public const string QuestRealizationFailed = "PTXT1205";
    public const string QuestExportUnsafe = "PTXT1206";
    public const string AdvancedSearchFailed = "PTXT1401";
    public const string NeuralProviderUnavailable = "PTXT1402";
    public const string NeuralProviderFailed = "PTXT1403";
    public const string NeuralOutputRejected = "PTXT1404";
    public const string AssetMigrationRequired = "PTXT1405";
    public const string ReleaseAuditIssue = "PTXT1406";
}
