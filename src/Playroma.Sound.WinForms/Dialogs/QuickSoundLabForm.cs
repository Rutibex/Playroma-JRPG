using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.WinForms.Controls;

namespace Playroma.Sound.WinForms.Dialogs;

public sealed class QuickSoundLabForm : Form
{
    private readonly Func<QuickSoundRequestedEventArgs, GeneratorRequestDocument?> _createNextDocument;
    private readonly Action<IAuthoringDocument> _validateDocument;
    private readonly Func<IAuthoringDocument, Task> _compileDocumentAsync;
    private readonly Func<IAuthoringDocument, Task> _playDocumentAsync;
    private readonly Action<IAuthoringDocument> _editorStateChanged;

    private readonly ToolStripLabel _titleLabel;
    private readonly Panel _editorHost;

    private GeneratorRequestEditorControl? _editor;

    public QuickSoundLabForm(
        Func<QuickSoundRequestedEventArgs, GeneratorRequestDocument?> createNextDocument,
        Action<IAuthoringDocument> validateDocument,
        Func<IAuthoringDocument, Task> compileDocumentAsync,
        Func<IAuthoringDocument, Task> playDocumentAsync,
        Action<IAuthoringDocument> editorStateChanged)
    {
        _createNextDocument = createNextDocument ?? throw new ArgumentNullException(nameof(createNextDocument));
        _validateDocument = validateDocument ?? throw new ArgumentNullException(nameof(validateDocument));
        _compileDocumentAsync = compileDocumentAsync ?? throw new ArgumentNullException(nameof(compileDocumentAsync));
        _playDocumentAsync = playDocumentAsync ?? throw new ArgumentNullException(nameof(playDocumentAsync));
        _editorStateChanged = editorStateChanged ?? throw new ArgumentNullException(nameof(editorStateChanged));

        Text = "Playroma Sound Lab";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 900);
        WindowState = FormWindowState.Maximized;

        var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(20, 20) };
        toolStrip.Items.Add(ActionButton("Play", (_, _) => _editor?.RequestPreview()));
        toolStrip.Items.Add(ActionButton("Try Another", (_, _) => _editor?.TryAnother()));
        toolStrip.Items.Add(ActionButton("Keep + Next", (_, _) => _editor?.KeepAndContinue()));
        toolStrip.Items.Add(ActionButton("Surprise Me", (_, _) => _editor?.SurpriseMe()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(ActionButton("Validate", (_, _) => _editor?.RequestValidation()));
        toolStrip.Items.Add(ActionButton("Compile", (_, _) => _editor?.RequestCompilation()));
        toolStrip.Items.Add(ActionButton("Advanced JSON", (_, _) => _editor?.FormatJson()));
        toolStrip.Items.Add(ActionButton("Save", (_, _) => SaveCurrentDocument()));
        toolStrip.Items.Add(new ToolStripSeparator());
        _titleLabel = new ToolStripLabel("No sound loaded") { Alignment = ToolStripItemAlignment.Right };
        toolStrip.Items.Add(_titleLabel);

        _editorHost = new Panel { Dock = DockStyle.Fill };

        Controls.Add(_editorHost);
        Controls.Add(toolStrip);

        FormClosing += OnFormClosing;
    }

    public GeneratorRequestEditorControl? CurrentEditor => _editor;
    public GeneratorRequestDocument? CurrentDocument => _editor?.Document as GeneratorRequestDocument;

    public void LoadDocument(GeneratorRequestDocument document, bool autoPlay)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (ReferenceEquals(CurrentDocument, document))
        {
            UpdateWindowText();
            if (!Visible) Show();
            BringToFront();
            Activate();
            if (autoPlay && _editor is not null) BeginInvoke((Action)_editor.RequestPreview);
            return;
        }

        UnloadCurrentEditor();

        var editor = new GeneratorRequestEditorControl(document)
        {
            Dock = DockStyle.Fill
        };
        editor.ValidateRequested += (_, eventArgs) => _validateDocument(eventArgs.Document);
        editor.CompileRequested += async (_, eventArgs) => await _compileDocumentAsync(eventArgs.Document);
        editor.PlayRequested += async (_, eventArgs) => await _playDocumentAsync(eventArgs.Document);
        editor.EditorStateChanged += (_, _) =>
        {
            UpdateWindowText();
            _editorStateChanged(editor.Document);
        };
        editor.NewSoundRequested += (_, eventArgs) =>
        {
            GeneratorRequestDocument? nextDocument = _createNextDocument(eventArgs);
            if (nextDocument is not null) LoadDocument(nextDocument, true);
        };
        document.Changed += OnDocumentChanged;

        _editor = editor;
        _editorHost.Controls.Clear();
        _editorHost.Controls.Add(editor);
        UpdateWindowText();

        if (!Visible) Show();
        BringToFront();
        Activate();
        if (autoPlay) BeginInvoke((Action)editor.RequestPreview);
    }

    public void Reset()
    {
        UnloadCurrentEditor();
        Hide();
    }

    private void UnloadCurrentEditor()
    {
        if (_editor is null) return;

        _editor.Document.Changed -= OnDocumentChanged;
        _editorHost.Controls.Remove(_editor);
        _editor.Dispose();
        _editor = null;
        UpdateWindowText();
    }

    private void SaveCurrentDocument()
    {
        if (_editor is null) return;

        try
        {
            if (!_editor.TryApplyEdits()) return;
            _editor.Document.Save();
            _editor.RefreshFromDocument(true);
            _editorStateChanged(_editor.Document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, exception.Message, "Could not save sound", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, CurrentDocument)) return;
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => OnDocumentChanged(sender, eventArgs)));
            return;
        }

        UpdateWindowText();
        if (CurrentDocument is not null) _editorStateChanged(CurrentDocument);
    }

    private void UpdateWindowText()
    {
        string name = CurrentDocument?.DisplayName ?? "No sound loaded";
        Text = $"Sound Lab — {name}";
        _titleLabel.Text = name;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason != CloseReason.UserClosing) return;
        eventArgs.Cancel = true;
        Hide();
    }

    private static ToolStripButton ActionButton(string text, EventHandler onClick)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += onClick;
        return button;
    }
}
