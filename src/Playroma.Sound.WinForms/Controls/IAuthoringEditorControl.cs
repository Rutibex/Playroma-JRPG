using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;

namespace Playroma.Sound.WinForms.Controls;

public interface IAuthoringEditorControl
{
    IAuthoringDocument Document { get; }
    bool HasUnappliedChanges { get; }

    event EventHandler<DocumentActionEventArgs>? ValidateRequested;
    event EventHandler<DocumentActionEventArgs>? CompileRequested;
    event EventHandler<DocumentActionEventArgs>? PlayRequested;
    event EventHandler? EditorStateChanged;

    bool TryApplyEdits(bool showError = true);
    void RefreshFromDocument(bool force);
    void SetValidation(ValidationSummary summary);
    void SetCompilationState(AuthoringCompilationResult result);
    void SelectDiagnostic(AuthoringDiagnostic diagnostic);
    void FormatJson();
}
