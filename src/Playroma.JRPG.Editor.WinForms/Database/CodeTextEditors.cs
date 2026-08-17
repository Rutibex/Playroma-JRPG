using System.Runtime.InteropServices;
using Playroma.JRPG.DatabaseEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public enum CodeEditorLanguage
{
    PlainText,
    JavaScript,
    NoteTags
}

public interface ICodeTextEditor
{
    string Text { get; set; }
    bool IsModified { get; }
    void SetLanguage(CodeEditorLanguage language);
    void SetDiagnostics(IReadOnlyList<CodeTextDiagnostic> diagnostics);
    void Find(string text);
    void GoToLine(int line);
}

public class CodeTextEditorControl : UserControl, ICodeTextEditor
{
    private readonly RichTextBox _editor = new()
    {
        Dock = DockStyle.Fill,
        AcceptsTab = true,
        DetectUrls = false,
        Font = new Font(FontFamily.GenericMonospace, 10),
        WordWrap = false,
        HideSelection = false
    };
    private readonly ListBox _lineNumbers = new() { Dock = DockStyle.Left, Width = 52, BorderStyle = BorderStyle.None, IntegralHeight = false, TabStop = false };
    private readonly TextBox _find = new() { PlaceholderText = "Find…", Width = 180 };
    private readonly Label _diagnostics = new() { AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
    private readonly System.Windows.Forms.Timer _analysisTimer = new() { Interval = 250 };
    private bool _modified;
    private bool _suppress;
    private bool _disposeStarted;
    private bool _applyingHighlighting;
    private CodeEditorLanguage _language;

    public CodeTextEditorControl()
    {
        Dock = DockStyle.Fill;
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 30, WrapContents = false, Padding = new Padding(2) };
        Button next = new() { Text = "Find Next", AutoSize = true };
        toolbar.Controls.Add(_find);
        toolbar.Controls.Add(next);
        toolbar.Controls.Add(_diagnostics);
        Controls.Add(_editor);
        Controls.Add(_lineNumbers);
        Controls.Add(toolbar);
        _editor.TextChanged += EditorTextChanged;
        _editor.VScroll += EditorScrolled;
        _editor.SelectionChanged += EditorSelectionChanged;
        next.Click += (_, _) => Find(_find.Text);
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Find(_find.Text); e.SuppressKeyPress = true; } };
        _analysisTimer.Tick += AnalysisTimerTick;
        SyncLineNumbers();
    }

    public new string Text
    {
        get => _editor.Text;
        set
        {
            // Cancel analysis queued for the previous record before replacing the
            // text. Otherwise that stale tick recolours the new record a second
            // time a fraction of a second later.
            _analysisTimer.Stop();
            _suppress = true;
            try
            {
                _editor.Text = value ?? string.Empty;
                _modified = false;
            }
            finally
            {
                _suppress = false;
            }
            SyncLineNumbers();
            Analyze();
        }
    }

    public bool IsModified => _modified;
    public bool ReadOnly { get => _editor.ReadOnly; set => _editor.ReadOnly = value; }
    public event EventHandler? ContentChanged;
    public event EventHandler? CommitRequested;

    public void MarkCommitted() => _modified = false;

    /// <summary>
    /// Replaces the visible text from a peer editor without raising ContentChanged,
    /// while preserving whether the shared working document is pending commit.
    /// </summary>
    public void SetWorkingText(string? text, bool isModified)
    {
        Text = text ?? string.Empty;
        _modified = isModified;
    }

    public void SetLanguage(CodeEditorLanguage language)
    {
        _language = language;
        Analyze();
    }

    public void SetDiagnostics(IReadOnlyList<CodeTextDiagnostic> diagnostics)
    {
        string text = diagnostics.Count == 0 ? "No syntax warnings" : $"{diagnostics.Count} syntax warning(s)";
        Color colour = diagnostics.Count == 0 ? SystemColors.GrayText : Color.DarkOrange;
        if (_diagnostics.Text != text) _diagnostics.Text = text;
        if (_diagnostics.ForeColor != colour) _diagnostics.ForeColor = colour;
        _diagnostics.Tag = diagnostics;
    }

    public void Find(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int start = Math.Min(_editor.TextLength, _editor.SelectionStart + _editor.SelectionLength);
        int found = _editor.Find(text, start, RichTextBoxFinds.None);
        if (found < 0) found = _editor.Find(text, 0, RichTextBoxFinds.None);
        if (found >= 0) { _editor.Select(found, text.Length); _editor.ScrollToCaret(); }
    }

    public void GoToLine(int line)
    {
        line = Math.Max(1, line);
        int index = _editor.GetFirstCharIndexFromLine(line - 1);
        if (index < 0) return;
        _editor.SelectionStart = index;
        _editor.SelectionLength = 0;
        _editor.ScrollToCaret();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            CommitRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            _find.Focus();
            _find.SelectAll();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposeStarted)
        {
            // The timer is not part of a designer component container, so the control
            // must explicitly stop and dispose it. A queued Tick can otherwise run
            // after the child RichTextBox has already been disposed.
            _disposeStarted = true;
            _analysisTimer.Stop();
            _analysisTimer.Tick -= AnalysisTimerTick;
            _analysisTimer.Dispose();
            _editor.TextChanged -= EditorTextChanged;
            _editor.VScroll -= EditorScrolled;
            _editor.SelectionChanged -= EditorSelectionChanged;
        }

        base.Dispose(disposing);
    }

    private bool CanAccessEditor =>
        !_disposeStarted
        && !IsDisposed
        && !Disposing
        && !_editor.IsDisposed
        && !_editor.Disposing;

    private void AnalysisTimerTick(object? sender, EventArgs e)
    {
        _analysisTimer.Stop();
        if (!CanAccessEditor) return;
        Analyze();
    }


    private void EditorScrolled(object? sender, EventArgs e)
    {
        if (!_applyingHighlighting) SyncLineNumbers();
    }

    private void EditorSelectionChanged(object? sender, EventArgs e)
    {
        if (!_applyingHighlighting) SyncLineNumbers();
    }

    private void EditorTextChanged(object? sender, EventArgs e)
    {
        if (!CanAccessEditor) return;
        if (!_suppress)
        {
            _modified = true;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            _analysisTimer.Stop();
            _analysisTimer.Start();
        }

        // Programmatic Text assignments are analyzed synchronously by the Text
        // property setter, so they must not leave redundant delayed work queued.
        if (!_applyingHighlighting) SyncLineNumbers();
    }

    private void Analyze()
    {
        if (!CanAccessEditor) return;
        if (_language == CodeEditorLanguage.PlainText) { SetDiagnostics([]); return; }

        CodeTextAnalysis result = CodeTextAnalyzer.AnalyzeJavaScript(
            _editor.Text,
            _language == CodeEditorLanguage.NoteTags);
        if (!CanAccessEditor) return;

        SetDiagnostics(result.Diagnostics);
        ApplyHighlighting(result.Tokens);
    }

    private void ApplyHighlighting(IReadOnlyList<CodeTextToken> tokens)
    {
        if (!CanAccessEditor) return;

        int selectionStart = _editor.SelectionStart;
        int selectionLength = _editor.SelectionLength;
        NativePoint scrollPosition = default;
        bool redrawSuspended = false;

        _suppress = true;
        _applyingHighlighting = true;
        try
        {
            // Suspend the native RichEdit redraw rather than only suspending
            // WinForms layout. Selecting every token otherwise paints every
            // intermediate selection and creates a visible flash while typing.
            if (_editor.IsHandleCreated)
            {
                SendMessage(_editor.Handle, EmGetScrollPos, IntPtr.Zero, ref scrollPosition);
                SendMessage(_editor.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                redrawSuspended = true;
            }

            _editor.SelectAll();
            _editor.SelectionColor = SystemColors.WindowText;
            foreach (CodeTextToken token in tokens)
            {
                if (token.Length == 0 || token.Start + token.Length > _editor.TextLength) continue;
                _editor.Select(token.Start, token.Length);
                _editor.SelectionColor = token.Kind switch
                {
                    CodeTextTokenKind.Comment => Color.ForestGreen,
                    CodeTextTokenKind.String => Color.Brown,
                    CodeTextTokenKind.Number => Color.DarkCyan,
                    CodeTextTokenKind.NoteTag => Color.DarkMagenta,
                    CodeTextTokenKind.Error => Color.Red,
                    CodeTextTokenKind.Bracket => Color.Navy,
                    _ => SystemColors.WindowText
                };
            }

            int safeStart = Math.Min(selectionStart, _editor.TextLength);
            int safeLength = Math.Min(selectionLength, Math.Max(0, _editor.TextLength - safeStart));
            _editor.Select(safeStart, safeLength);
            if (redrawSuspended)
            {
                SendMessage(_editor.Handle, EmSetScrollPos, IntPtr.Zero, ref scrollPosition);
            }
        }
        finally
        {
            if (redrawSuspended && !_editor.IsDisposed && _editor.IsHandleCreated)
            {
                SendMessage(_editor.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                _editor.Invalidate();
            }
            _applyingHighlighting = false;
            _suppress = false;
        }

        SyncLineNumbers();
    }

    private const int WmSetRedraw = 0x000B;
    private const int EmGetScrollPos = 0x04DD;
    private const int EmSetScrollPos = 0x04DE;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref NativePoint lParam);

    private void SyncLineNumbers()
    {
        if (!CanAccessEditor) return;

        int lineCount = Math.Max(1, _editor.Lines.Length);
        if (_lineNumbers.Items.Count != lineCount)
        {
            _lineNumbers.BeginUpdate();
            _lineNumbers.Items.Clear();
            for (int line = 1; line <= lineCount; line++) _lineNumbers.Items.Add(line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _lineNumbers.EndUpdate();
        }
        int first = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, 1)));
        if (first >= 0 && first < _lineNumbers.Items.Count) _lineNumbers.TopIndex = first;
    }
}

public sealed class NoteEditor : CodeTextEditorControl
{
    public NoteEditor() => SetLanguage(CodeEditorLanguage.NoteTags);
}

public sealed class FormulaEditor : CodeTextEditorControl
{
    public FormulaEditor() => SetLanguage(CodeEditorLanguage.JavaScript);
}
