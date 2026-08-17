namespace Playroma.JRPG.Core;

public enum ProjectLoadStage
{
    Locating,
    Classifying,
    InventoryingFiles,
    LoadingDatabase,
    LoadingMaps,
    ReadingPlugins,
    FingerprintingRuntime,
    InventoryingAssets,
    Finalizing
}

public sealed record ProjectLoadProgress(
    ProjectLoadStage Stage,
    int Completed,
    int Total,
    string? CurrentPath = null);
