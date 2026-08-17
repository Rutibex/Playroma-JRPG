namespace Playroma.JRPG.Core;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
    Fatal
}

public sealed record ProjectLocation(
    string? RelativePath = null,
    string? JsonPath = null,
    int? MapId = null,
    int? RecordId = null,
    int? EventId = null,
    int? PageIndex = null,
    int? CommandIndex = null);

public sealed record ProjectDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    ProjectLocation Location,
    string? TechnicalDetail = null)
{
    public static ProjectDiagnostic Information(
        string code,
        string message,
        ProjectLocation? location = null,
        string? technicalDetail = null) =>
        new(code, DiagnosticSeverity.Information, message, location ?? new ProjectLocation(), technicalDetail);

    public static ProjectDiagnostic Warning(
        string code,
        string message,
        ProjectLocation? location = null,
        string? technicalDetail = null) =>
        new(code, DiagnosticSeverity.Warning, message, location ?? new ProjectLocation(), technicalDetail);

    public static ProjectDiagnostic Error(
        string code,
        string message,
        ProjectLocation? location = null,
        string? technicalDetail = null) =>
        new(code, DiagnosticSeverity.Error, message, location ?? new ProjectLocation(), technicalDetail);

    public static ProjectDiagnostic Fatal(
        string code,
        string message,
        ProjectLocation? location = null,
        string? technicalDetail = null) =>
        new(code, DiagnosticSeverity.Fatal, message, location ?? new ProjectLocation(), technicalDetail);
}

public static class DiagnosticCodes
{
    public const string NotAnMvProject = "PJRPG1001";
    public const string MultipleProjectCandidates = "PJRPG1002";
    public const string UnsupportedMzProject = "PJRPG1003";
    public const string SearchLimitReached = "PJRPG1004";

    public const string StandardDataFileMissing = "PJRPG1101";
    public const string MapFileMissing = "PJRPG1102";
    public const string MapFileNotListed = "PJRPG1103";
    public const string DuplicateMapId = "PJRPG1104";
    public const string MissingIndexZeroNull = "PJRPG1105";
    public const string MapDataLengthMismatch = "PJRPG1106";

    public const string InvalidJson = "PJRPG1201";
    public const string UnexpectedJsonRoot = "PJRPG1202";
    public const string InvalidTextEncoding = "PJRPG1203";

    public const string EncryptedAssetsDetected = "PJRPG1301";
    public const string AssetCaseCollision = "PJRPG1302";
    public const string UnusualAssetExtension = "PJRPG1303";

    public const string PluginFileMissing = "PJRPG1401";
    public const string DuplicatePluginEntry = "PJRPG1402";
    public const string NonstandardPluginManifest = "PJRPG1403";
    public const string InvalidPluginParameter = "PJRPG1404";
    public const string UnsafePluginPath = "PJRPG1405";

    public const string RuntimeIncomplete = "PJRPG1501";
    public const string RuntimeVersionUnknown = "PJRPG1502";
    public const string RuntimeNodeDependency = "PJRPG1503";

    public const string FileChangedDuringLoad = "PJRPG1601";
    public const string FileReadFailure = "PJRPG1602";
    public const string FileInventoryLimitReached = "PJRPG1603";

    public const string AccessDenied = "PJRPG1701";
    public const string PathOutsideProjectRoot = "PJRPG1702";
    public const string ReparsePointSkipped = "PJRPG1703";

    public const string UnknownEventCommand = "PJRPG1801";

    public const string MutableInvalidJson = "PJRPG2001";
    public const string UnsupportedDocumentEncoding = "PJRPG2002";
    public const string DuplicateJsonProperty = "PJRPG2003";
    public const string TypedPropertyWrongType = "PJRPG2004";
    public const string RequiredPropertyMissing = "PJRPG2005";
    public const string MutableDocumentReadOnly = "PJRPG2006";

    public const string ConflictingTextEdits = "PJRPG2101";
    public const string StaleSyntaxNode = "PJRPG2102";
    public const string UnauthorizedGeneratedDifference = "PJRPG2103";
    public const string InvalidArrayMutation = "PJRPG2104";
    public const string RecordIdMismatch = "PJRPG2105";

    public const string FileChangedExternally = "PJRPG2201";
    public const string TemporaryWriteFailed = "PJRPG2202";
    public const string GeneratedJsonInvalid = "PJRPG2203";
    public const string AtomicReplacementFailed = "PJRPG2204";
    public const string SavedFileHashMismatch = "PJRPG2205";
    public const string BackupCreationFailed = "PJRPG2206";
    public const string SaveRecoveryRequired = "PJRPG2207";
    public const string InsufficientDiskSpace = "PJRPG2208";
    public const string ProjectSaveLockHeld = "PJRPG2209";

    public const string IncompleteTransactionFound = "PJRPG2301";
    public const string RecoveryRollbackAvailable = "PJRPG2302";
    public const string RecoveryCompletionAvailable = "PJRPG2303";
    public const string RecoveryStateAmbiguous = "PJRPG2304";
    public const string RecoveryBackupMissing = "PJRPG2305";

    public const string Phase3MissingReference = "PJRPG3001";
    public const string Phase3InvalidStartingLocation = "PJRPG3002";
    public const string Phase3MissingPluginFile = "PJRPG3003";
    public const string Phase3DuplicatePluginEntry = "PJRPG3004";
    public const string Phase3DisabledPluginReferenced = "PJRPG3005";
    public const string Phase3MapDataLengthMismatch = "PJRPG3006";
    public const string Phase3InvalidEventIndentation = "PJRPG3007";
    public const string Phase3AutorunTerminationRisk = "PJRPG3008";
    public const string Phase3TransactionFailure = "PJRPG3009";
    public const string Phase3ReferenceIndexFailure = "PJRPG3010";

    public const string Phase5MissingAsset = "PJRPG5001";
    public const string Phase5CorruptAsset = "PJRPG5002";
    public const string Phase5UnsupportedPreview = "PJRPG5003";
    public const string Phase5VariantConflict = "PJRPG5004";
    public const string Phase5InvalidSheetGrid = "PJRPG5005";
    public const string Phase5ZeroByteAsset = "PJRPG5006";
    public const string Phase5AssetOperationConflict = "PJRPG5007";
    public const string Phase5StagedContentMissing = "PJRPG5008";
    public const string Phase5AssetHashMismatch = "PJRPG5009";
    public const string Phase5UnsupportedCodec = "PJRPG5010";
    public const string Phase5AssetChangedExternally = "PJRPG5011";

    public const string Phase6TilesetMissing = "PJRPG6001";
    public const string Phase6MapLayerLength = "PJRPG6002";
    public const string Phase6InvalidTileValue = "PJRPG6003";
    public const string Phase6ShadowHighBits = "PJRPG6004";
    public const string Phase6InvalidAutotileShape = "PJRPG6005";
    public const string Phase6RenderAssetMissing = "PJRPG6006";
    public const string Phase6TilesetDimensions = "PJRPG6007";
    public const string Phase6CharacterGrid = "PJRPG6008";
    public const string Phase6PluginRenderingDifference = "PJRPG6009";
    public const string Phase6GoldenImageDifference = "PJRPG6010";

    public const string Phase7InvalidMapDimensions = "PJRPG7001";
    public const string Phase7EventOutOfBounds = "PJRPG7002";
    public const string Phase7DuplicateEventId = "PJRPG7003";
    public const string Phase7EventIdIndexMismatch = "PJRPG7004";
    public const string Phase7InvalidRegionValue = "PJRPG7005";
    public const string Phase7UnexpectedShadowBits = "PJRPG7006";
    public const string Phase7InvalidEncounterRegion = "PJRPG7007";
    public const string Phase7MapHierarchyCycle = "PJRPG7008";
    public const string Phase7InvalidParentMap = "PJRPG7009";

    public const string Phase8InvalidDatabaseRecord = "PJRPG8001";
    public const string Phase8RecordIdMismatch = "PJRPG8002";
    public const string Phase8UnknownTraitCode = "PJRPG8003";
    public const string Phase8UnknownEffectCode = "PJRPG8004";
    public const string Phase8UnsafeMaximumReduction = "PJRPG8005";
    public const string Phase8UnterminatedComment = "PJRPG8006";
    public const string Phase8UnterminatedString = "PJRPG8007";
    public const string Phase8UnmatchedClosingBracket = "PJRPG8008";
    public const string Phase8UnmatchedOpeningBracket = "PJRPG8009";
    public const string Phase8ClipboardReviewRequired = "PJRPG8010";

    public const string Phase9AInvalidFieldValue = "PJRPG9001";
    public const string Phase9AInvalidActorLevelRange = "PJRPG9002";
    public const string Phase9AInvalidClassCurve = "PJRPG9003";
    public const string Phase9AMissingEquipment = "PJRPG9004";
    public const string Phase9ADuplicateLearning = "PJRPG9005";
    public const string Phase9AMissingLearnedSkill = "PJRPG9006";
    public const string Phase9AUnreachableLearning = "PJRPG9007";
    public const string Phase9AInvalidLearningLevel = "PJRPG9008";

    public const string Phase9BInvalidUsableItem = "PJRPG9101";
    public const string Phase9BInvalidDamage = "PJRPG9102";
    public const string Phase9BInvalidEffect = "PJRPG9103";
    public const string Phase9BFormulaApproximationWarning = "PJRPG9104";
    public const string Phase9BInvalidEquipmentParameters = "PJRPG9105";
    public const string Phase9BInvalidEquipmentType = "PJRPG9106";
    public const string Phase9BInvalidStateRemoval = "PJRPG9107";
    public const string Phase9BInvalidStateVisual = "PJRPG9108";
    public const string Phase9BPreservedExtensionField = "PJRPG9109";

    public const string Phase9CInvalidEnemyParameters = "PJRPG9201";
    public const string Phase9CInvalidEnemyDrop = "PJRPG9202";
    public const string Phase9CInvalidEnemyAction = "PJRPG9203";
    public const string Phase9CEnemyHasNoAction = "PJRPG9204";
    public const string Phase9CInvalidTroopMember = "PJRPG9205";
    public const string Phase9CInvalidTroopPage = "PJRPG9206";
    public const string Phase9CTroopPageWillNotRun = "PJRPG9207";
    public const string Phase9CMomentPageRepeatRisk = "PJRPG9208";
    public const string Phase9CHiddenMemberNeverAppears = "PJRPG9209";
    public const string Phase9CMemberIndexReference = "PJRPG9210";
    public const string Phase9DInvalidAnimationRecord = "PJRPG9301";
    public const string Phase9DInvalidAnimationFrame = "PJRPG9302";
    public const string Phase9DInvalidAnimationCell = "PJRPG9303";
    public const string Phase9DExtensionCellsPreserved = "PJRPG9304";
    public const string Phase9DPatternOutOfRange = "PJRPG9305";
    public const string Phase9DInvalidAnimationTiming = "PJRPG9306";
    public const string Phase9DTimingFrameOutOfRange = "PJRPG9307";
    public const string Phase9DAnimationHasNoFrames = "PJRPG9308";
    public const string Phase9DAnimationHasNoVisibleCells = "PJRPG9309";
    public const string Phase9EInvalidTilesetRecord = "PJRPG9401";
    public const string Phase9EInvalidTilesetMode = "PJRPG9402";
    public const string Phase9EInvalidTilesetImages = "PJRPG9403";
    public const string Phase9EMissingTilesetImage = "PJRPG9404";
    public const string Phase9EInvalidTilesetFlags = "PJRPG9405";
    public const string Phase9ETilesetFlagArrayShort = "PJRPG9406";
    public const string Phase9ETilesetFlagArrayExtended = "PJRPG9407";
    public const string Phase9EAutotileFlagsDiffer = "PJRPG9408";
    public const string Phase9EInvalidSystem = "PJRPG9410";
    public const string Phase9EInvalidStartingPosition = "PJRPG9411";
    public const string Phase9EInvalidVehicle = "PJRPG9412";
    public const string Phase9EMissingSystemAsset = "PJRPG9413";
    public const string Phase9EInvalidAudioSetting = "PJRPG9414";
    public const string Phase9EInvalidSystemArray = "PJRPG9415";
    public const string Phase9EInvalidSystemReference = "PJRPG9416";
    public const string Phase9EAttackMotionCount = "PJRPG9417";
    public const string Phase9EInvalidAttackMotion = "PJRPG9418";
    public const string Phase9EInvalidTerms = "PJRPG9419";
    public const string Phase9ETermPlaceholderMissing = "PJRPG9420";
    public const string Phase10StructuralProjection = "PJRPG10001";
    public const string Phase10UnexpectedIndentation = "PJRPG10002";
    public const string Phase10OrphanContinuation = "PJRPG10003";
    public const string Phase10MissingBranch = "PJRPG10004";
    public const string Phase10MissingClosingCommand = "PJRPG10005";
    public const string Phase10UnknownCommand = "PJRPG10010";
    public const string Phase10InvalidIndentation = "PJRPG10011";
    public const string Phase10InvalidParameters = "PJRPG10012";
    public const string Phase10ParameterCount = "PJRPG10013";
    public const string Phase10ParameterType = "PJRPG10014";
    public const string Phase10MissingTerminator = "PJRPG10015";
    public const string Phase10CommandsAfterTerminator = "PJRPG10016";
    public const string Phase10InvalidTerminator = "PJRPG10017";
    public const string Phase10DuplicateLabel = "PJRPG10018";
    public const string Phase10MissingLabel = "PJRPG10019";
    public const string Phase10InvalidMovementRoute = "PJRPG10020";
    public const string Phase10InvalidMovementContinuation = "PJRPG10021";
    public const string Phase10MovementRouteCountMismatch = "PJRPG10022";
    public const string Phase10MovementRouteMismatch = "PJRPG10023";
    public const string Phase10LoopWithoutBreak = "PJRPG10024";

}
