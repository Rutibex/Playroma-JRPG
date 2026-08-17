namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvSpriteEditorDeepLink(
    string? ImagePath = null,
    string? ProfileId = null,
    int? FrameIndex = null,
    int? CellIndex = null,
    string? DatabaseSection = null,
    int? DatabaseRecordId = null,
    string? PropertyName = null);
