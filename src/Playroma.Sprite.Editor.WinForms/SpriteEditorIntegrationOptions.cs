using Playroma.Sprite.RpgMakerMV;

namespace Playroma.Sprite.Editor.WinForms;

public sealed record SpriteEditorIntegrationOptions(
    string? ProjectRoot = null,
    string? ProjectTitle = null,
    string? AuthoringProjectPath = null,
    Action? RequestHostClose = null,
    string? InitialImagePath = null,
    MvSpriteEditorDeepLink? DeepLink = null,
    Func<MvRuntimeInstallResult, CancellationToken, ValueTask>? AssetInstalled = null,
    string? SettingsRoot = null);
