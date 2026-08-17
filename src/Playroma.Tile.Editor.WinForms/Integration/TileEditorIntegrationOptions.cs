namespace Playroma.Tile.Editor.WinForms.Integration;

/// <summary>Host settings used when the complete Playroma Tile form is embedded or launched by another Playroma authoring tool.</summary>
public sealed record TileEditorIntegrationOptions(
    string RpgMakerProjectRoot,
    string RpgMakerProjectTitle,
    string DefaultAuthoringProjectPath,
    Action? RequestHostClose = null,
    bool ImportDatabaseOnFirstOpen = true,
    IReadOnlyList<string>? InitialImportFiles = null,
    string? SpriteBridgeManifestPath = null,
    string? SpriteBridgeReturnDirectory = null);
