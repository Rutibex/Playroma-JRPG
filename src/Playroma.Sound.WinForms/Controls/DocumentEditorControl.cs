using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;

namespace Playroma.Sound.WinForms.Controls;

public sealed class DocumentActionEventArgs : EventArgs
{
    public DocumentActionEventArgs(IAuthoringDocument document) => Document = document;
    public IAuthoringDocument Document { get; }
}

/// <summary>
/// Generic document surface. Specialized graph and scene canvases replace its centre
/// in later phases, while this raw JSON view remains useful for diagnostics and advanced editing.
/// </summary>
public sealed class DocumentEditorControl : UserControl, IAuthoringEditorControl
{
    private readonly ToolStrip _tools;
    private readonly ToolStripButton _applyButton;
    private readonly ToolStripButton _revertButton;
    private readonly ToolStripButton _formatButton;
    private readonly ToolStripButton _validateButton;
    private readonly ToolStripButton _compileButton;
    private readonly ToolStripButton _playButton;
    private readonly TextBox _editor;
    private readonly Label _identityLabel;
    private readonly Label _stateLabel;
    private readonly Label _diagnosticLabel;
    private bool _synchronizing;

    public DocumentEditorControl(IAuthoringDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Dock = DockStyle.Fill;

        _tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        _applyButton = Button("Apply JSON", "Commit the JSON text as one undoable document edit");
        _revertButton = Button("Revert Text", "Discard text changes that have not been applied");
        _formatButton = Button("Format", "Format the JSON without committing it");
        _validateButton = Button("Validate", "Validate the current authoring document");
        _compileButton = Button("Compile", "Compile the current document in the background");
        _playButton = Button("Play", "Compile and preview through WASAPI");
        _tools.Items.AddRange([_applyButton, _revertButton, new ToolStripSeparator(), _formatButton, new ToolStripSeparator(), _validateButton, _compileButton, _playButton]);

        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 66,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 5, 8, 3),
            BackColor = SystemColors.ControlLight
        };
        _identityLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
        _stateLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        _diagnosticLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
        summary.Controls.Add(_identityLabel, 0, 0);
        summary.Controls.Add(_stateLabel, 0, 1);
        summary.Controls.Add(_diagnosticLabel, 0, 2);

        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsTab = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            HideSelection = false,
            Font = new Font(FontFamily.GenericMonospace, 10f),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(_editor);
        Controls.Add(summary);
        Controls.Add(_tools);

        _applyButton.Click += (_, _) => TryApplyEdits();
        _revertButton.Click += (_, _) => RefreshFromDocument(true);
        _formatButton.Click += (_, _) => FormatJson();
        _validateButton.Click += (_, _) => ValidateRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        _compileButton.Click += (_, _) => CompileRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        _playButton.Click += (_, _) => PlayRequested?.Invoke(this, new DocumentActionEventArgs(Document));
        _editor.TextChanged += (_, _) =>
        {
            if (_synchronizing) return;
            UpdateStateLabels();
            EditorStateChanged?.Invoke(this, EventArgs.Empty);
        };
        _editor.KeyDown += OnEditorKeyDown;
        Document.Changed += OnDocumentChanged;
        RefreshFromDocument(true);
    }

    public event EventHandler<DocumentActionEventArgs>? ValidateRequested;
    public event EventHandler<DocumentActionEventArgs>? CompileRequested;
    public event EventHandler<DocumentActionEventArgs>? PlayRequested;
    public event EventHandler? EditorStateChanged;

    public IAuthoringDocument Document { get; }
    public bool HasUnappliedChanges => !StringComparer.Ordinal.Equals(_editor.Text, Document.SerializeContent());

    public bool TryApplyEdits(bool showError = true)
    {
        if (!HasUnappliedChanges) return true;
        try
        {
            Document.ReplaceSerializedContent(_editor.Text, "Edit JSON");
            RefreshFromDocument(true);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            if (showError)
                MessageBox.Show(this, exception.Message, "Invalid document JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _diagnosticLabel.Text = exception.Message;
            _diagnosticLabel.ForeColor = Color.Firebrick;
            return false;
        }
    }

    public void RefreshFromDocument(bool force)
    {
        if (!force && HasUnappliedChanges) return;
        _synchronizing = true;
        try
        {
            _editor.Text = Document.SerializeContent();
            _editor.SelectionStart = 0;
            _editor.SelectionLength = 0;
        }
        finally
        {
            _synchronizing = false;
        }
        SetValidation(Document.Validate());
        UpdateStateLabels();
    }

    public void SetValidation(ValidationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        int errors = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Error);
        int warnings = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Warning);
        int information = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Information);
        _diagnosticLabel.Text = $"Validation: {errors} error(s), {warnings} warning(s), {information} information message(s)";
        _diagnosticLabel.ForeColor = errors > 0 ? Color.Firebrick : warnings > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
    }

    public void SetCompilationState(AuthoringCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _diagnosticLabel.Text = $"Compilation: {result.State} in {result.Duration.TotalMilliseconds:0.0} ms";
        _diagnosticLabel.ForeColor = result.Succeeded ? Color.DarkGreen : result.State == AuthoringCompilationState.Cancelled ? SystemColors.GrayText : Color.Firebrick;
    }

    public void SelectDiagnostic(AuthoringDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnosticLabel.Text = $"{diagnostic.Code}: {diagnostic.Message}";
        _diagnosticLabel.ForeColor = diagnostic.Severity == AuthoringDiagnosticSeverity.Error ? Color.Firebrick : Color.DarkGoldenrod;
        _editor.Focus();
        if (!string.IsNullOrWhiteSpace(diagnostic.ObjectId))
        {
            int index = _editor.Text.IndexOf(diagnostic.ObjectId, StringComparison.Ordinal);
            if (index >= 0)
            {
                _editor.SelectionStart = index;
                _editor.SelectionLength = diagnostic.ObjectId.Length;
                _editor.ScrollToCaret();
            }
        }
    }

    public void FormatJson()
    {
        try
        {
            JsonNode? node = JsonNode.Parse(_editor.Text);
            if (node is null) throw new JsonException("The document is empty.");
            _synchronizing = true;
            try { _editor.Text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }); }
            finally { _synchronizing = false; }
            UpdateStateLabels();
        }
        catch (JsonException exception)
        {
            MessageBox.Show(this, exception.Message, "Cannot format JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateStateLabels()
    {
        _identityLabel.Text = $"{Document.DisplayName}  [{Document.Kind}]";
        string unapplied = HasUnappliedChanges ? " | unapplied JSON" : "";
        string dirty = Document.IsDirty ? "modified" : "saved";
        _stateLabel.Text = $"{Document.DocumentId} | revision {Document.Revision} | {dirty}{unapplied} | {Document.FilePath}";
        _applyButton.Enabled = HasUnappliedChanges;
        _revertButton.Enabled = HasUnappliedChanges;
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => OnDocumentChanged(sender, eventArgs)));
            return;
        }
        RefreshFromDocument(!HasUnappliedChanges || eventArgs.Reason.StartsWith("Undo", StringComparison.Ordinal) || eventArgs.Reason.StartsWith("Redo", StringComparison.Ordinal));
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Control && eventArgs.KeyCode == Keys.Enter)
        {
            TryApplyEdits();
            eventArgs.SuppressKeyPress = true;
        }
        else if (eventArgs.Control && eventArgs.Shift && eventArgs.KeyCode == Keys.F)
        {
            FormatJson();
            eventArgs.SuppressKeyPress = true;
        }
    }

    private static ToolStripButton Button(string text, string toolTip) => new(text)
    {
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        ToolTipText = toolTip
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Document.Changed -= OnDocumentChanged;
            _editor.Font.Dispose();
            _identityLabel.Font.Dispose();
        }
        base.Dispose(disposing);
    }
}
