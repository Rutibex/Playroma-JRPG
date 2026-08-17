using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>
/// Supplies a category-specific editor surface while the shared database host retains
/// record navigation, notes, validation, references, history, clipboard, and raw views.
/// </summary>
public interface IDatabaseEditorBody
{
    Control View { get; }
    bool OwnsTraits { get; }
    bool OwnsEffects { get; }
    bool OwnsFormula { get; }

    ValueTask BindAsync(
        IReadOnlyList<DatabaseRecordHandle> records,
        CancellationToken cancellationToken = default);
}

internal static class DatabaseEditorBodyFactory
{
    public static IDatabaseEditorBody? Create(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        MvDocumentKind kind,
        IDatabaseEditorDefinition definition,
        Func<DocumentRequest, Task>? documentOpener = null) => kind switch
        {
            MvDocumentKind.Actors => new ActorEditorControl(session, document, definition, documentOpener),
            MvDocumentKind.Classes => new ClassEditorControl(session, document, definition),
            MvDocumentKind.Skills => new UsableItemEditorControl(session, document, definition, MvDocumentKind.Skills),
            MvDocumentKind.Items => new UsableItemEditorControl(session, document, definition, MvDocumentKind.Items),
            MvDocumentKind.Weapons => new EquipmentEditorControl(session, document, definition, MvDocumentKind.Weapons),
            MvDocumentKind.Armors => new EquipmentEditorControl(session, document, definition, MvDocumentKind.Armors),
            MvDocumentKind.States => new StateEditorControl(session, document, definition),
            MvDocumentKind.Enemies => new EnemyEditorControl(session, document, definition, documentOpener),
            MvDocumentKind.Troops => new TroopEditorControl(session, document, definition, documentOpener),
            MvDocumentKind.Animations => new AnimationEditorControl(session, document, definition),
            MvDocumentKind.Tilesets => new TilesetEditorControl(session, document, definition),
            MvDocumentKind.CommonEvents => new CommonEventEditorControl(session, document, definition, documentOpener),
            _ => null
        };
}
