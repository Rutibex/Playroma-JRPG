using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.WinForms.ResourcesUI;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Multi-file, folder and drag/drop importer. Nothing is staged until Finish succeeds; all files
/// are committed as one project transaction and therefore one undo entry.
/// </summary>
public sealed class AssetImportWizardForm : Form
{
    private static readonly string SupportedFilter =
        "Supported RPG Maker assets|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.ico;*.ogg;*.m4a;*.wav;*.mp3;*.flac;*.webm;*.mp4;*.ogv;*.ttf;*.otf;*.woff;*.woff2|All files|*.*";

    private readonly MvProjectSession _session;
    private readonly AssetCategory _suggestedCategory;
    private readonly List<string> _sources = [];
    private readonly List<AssetImportCandidate> _candidates = [];
    private readonly TabControl _pages = new() { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };
    private readonly ListView _sourceList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false
    };
    private readonly TextBox _review = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly Label _pageTitle = new() { Dock = DockStyle.Top, Height = 44, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Padding = new Padding(10, 12, 0, 0) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(8, 6, 8, 0), AutoEllipsis = true };
    private readonly Button _back = new() { Text = "< Back", AutoSize = true, MinimumSize = new Size(86, 30) };
    private readonly Button _next = new() { Text = "Next >", AutoSize = true, MinimumSize = new Size(86, 30) };
    private readonly Button _finish = new() { Text = "Finish", AutoSize = true, MinimumSize = new Size(86, 30), Enabled = false };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(86, 30) };
    private readonly Button _previewAgain = new() { Text = "Refresh Preview", AutoSize = true };
    private CancellationTokenSource? _previewCancellation;
    private AssetImportPlan? _validatedPlan;
    private AssetManagementPreview? _validatedPreview;
    private bool _updatingGrid;
    private bool _importing;

    public AssetImportWizardForm(
        MvProjectSession session,
        IEnumerable<string>? initialSources = null,
        AssetCategory suggestedCategory = AssetCategory.Unknown)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _suggestedCategory = suggestedCategory;
        Text = "Import Assets";
        Name = nameof(AssetImportWizardForm);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 590);
        ClientSize = new Size(1120, 720);
        KeyPreview = true;
        AllowDrop = true;

        BuildSourcePage();
        BuildClassificationPage();
        BuildReviewPage();
        Controls.Add(_pages);
        Controls.Add(_status);
        Controls.Add(_pageTitle);
        BuildBottomButtons();

        _back.Click += (_, _) => ChangePage(-1);
        _next.Click += async (_, _) => await MoveNextAsync().ConfigureAwait(true);
        _finish.Click += async (_, _) => await FinishAsync().ConfigureAwait(true);
        _previewAgain.Click += async (_, _) => await PrepareReviewAsync().ConfigureAwait(true);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnGridCellValueChanged;
        _grid.DataError += (_, e) => e.ThrowException = false;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += OnFormClosing;

        AddSources(initialSources ?? []);
        ShowPage(0);
    }

    public int ImportedCount { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.I))
        {
            AddFiles();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildSourcePage()
    {
        _sourceList.Columns.Add("Source file", 680);
        _sourceList.Columns.Add("Type", 90);
        _sourceList.Columns.Add("Size", 100);
        _sourceList.Columns.Add("Status", 180);

        ToolStrip strip = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton addFiles = new("Add Files…");
        ToolStripButton addFolder = new("Add Folder…");
        ToolStripButton remove = new("Remove Selected");
        ToolStripButton clear = new("Clear");
        addFiles.Click += (_, _) => AddFiles();
        addFolder.Click += (_, _) => AddFolder();
        remove.Click += (_, _) => RemoveSelectedSources();
        clear.Click += (_, _) => { _sources.Clear(); RefreshSourceList(); InvalidatePreview(); };
        strip.Items.AddRange([addFiles, addFolder, new ToolStripSeparator(), remove, clear]);

        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        Label help = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Text = "Add individual files, an entire folder, or drag files and folders onto this window. Unsupported files are ignored when adding a folder.",
            Padding = new Padding(4, 8, 4, 0)
        };
        panel.Controls.Add(_sourceList);
        panel.Controls.Add(help);
        panel.Controls.Add(strip);
        _pages.TabPages.Add(new TabPage { Controls = { panel } });
    }

    private void BuildClassificationPage()
    {
        DataGridViewTextBoxColumn source = new() { Name = "Source", HeaderText = "Source", ReadOnly = true, Width = 230 };
        DataGridViewComboBoxColumn category = new()
        {
            Name = "Category",
            HeaderText = "Category",
            Width = 150,
            FlatStyle = FlatStyle.Flat,
            DataSource = Enum.GetValues<AssetCategory>().Where(value => value != AssetCategory.Unknown && AssetCategoryRegistry.Directories.ContainsKey(value)).ToArray()
        };
        DataGridViewTextBoxColumn logical = new() { Name = "LogicalName", HeaderText = "Logical name", Width = 210 };
        DataGridViewComboBoxColumn action = new()
        {
            Name = "Action",
            HeaderText = "Conflict action",
            Width = 155,
            FlatStyle = FlatStyle.Flat,
            DisplayMember = nameof(ImportActionOption.DisplayName),
            ValueMember = nameof(ImportActionOption.Value),
            DataSource = new[]
            {
                new ImportActionOption(AssetImportCandidateAction.ResolveConflict, "Choose an action…"),
                new ImportActionOption(AssetImportCandidateAction.Import, "Import / add variant"),
                new ImportActionOption(AssetImportCandidateAction.ReplaceExisting, "Replace existing"),
                new ImportActionOption(AssetImportCandidateAction.AutoRename, "Auto rename"),
                new ImportActionOption(AssetImportCandidateAction.Skip, "Skip")
            }
        };
        DataGridViewTextBoxColumn destination = new() { Name = "Destination", HeaderText = "Destination", ReadOnly = true, Width = 260 };
        DataGridViewTextBoxColumn status = new() { Name = "Status", HeaderText = "Validation", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 };
        _grid.Columns.AddRange([source, category, logical, action, destination, status]);

        ToolStrip strip = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton applyCategory = new("Set Category for Selected…");
        ToolStripButton autoRename = new("Auto Rename Conflicts");
        ToolStripButton skipInvalid = new("Skip Invalid");
        applyCategory.Click += (_, _) => SetCategoryForSelected();
        autoRename.Click += (_, _) => AutoRenameConflicts();
        skipInvalid.Click += (_, _) => SkipInvalid();
        strip.Items.AddRange([applyCategory, autoRename, skipInvalid]);

        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        panel.Controls.Add(_grid);
        panel.Controls.Add(strip);
        _pages.TabPages.Add(new TabPage { Controls = { panel } });
    }

    private void BuildReviewPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        FlowLayoutPanel actions = new() { Dock = DockStyle.Top, Height = 42, WrapContents = false };
        actions.Controls.Add(_previewAgain);
        panel.Controls.Add(_review);
        panel.Controls.Add(actions);
        _pages.TabPages.Add(new TabPage { Controls = { panel } });
    }

    private void BuildBottomButtons()
    {
        Panel bottom = new() { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttons.Controls.AddRange([_back, _next, _finish, _cancel]);
        bottom.Controls.Add(buttons);
        Controls.Add(bottom);
        CancelButton = _cancel;
    }

    private void AddFiles()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select assets to import",
            Filter = SupportedFilter,
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddSources(dialog.FileNames);
    }

    private void AddFolder()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select a folder containing assets. Subfolders will be included.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AddSources(AssetImportPlanner.EnumerateSupportedFiles(dialog.SelectedPath, recursive: true));
    }

    private void AddSources(IEnumerable<string> paths)
    {
        int before = _sources.Count;
        foreach (string supplied in paths)
        {
            if (string.IsNullOrWhiteSpace(supplied)) continue;
            if (Directory.Exists(supplied))
            {
                foreach (string file in AssetImportPlanner.EnumerateSupportedFiles(supplied, recursive: true)) AddSource(file);
            }
            else AddSource(supplied);
        }
        if (_sources.Count != before)
        {
            RefreshSourceList();
            InvalidatePreview();
        }
    }

    private void AddSource(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (!File.Exists(fullPath)) return;
        if (_sources.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) return;
        _sources.Add(fullPath);
    }

    private void RemoveSelectedSources()
    {
        string[] selected = _sourceList.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as string)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        foreach (string path in selected) _sources.RemoveAll(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        RefreshSourceList();
        InvalidatePreview();
    }

    private void RefreshSourceList()
    {
        _sourceList.BeginUpdate();
        try
        {
            _sourceList.Items.Clear();
            foreach (string source in _sources)
            {
                FileInfo info = new(source);
                AssetMediaKind media = AssetCategoryRegistry.ResolveMediaKind(info.Extension);
                string status = !info.Exists
                    ? "Missing"
                    : AssetCategoryRegistry.IsEncryptedExtension(info.Extension)
                        ? "Encrypted deployment file"
                        : media == AssetMediaKind.Unknown
                            ? "Unsupported"
                            : info.Length == 0
                                ? "Empty"
                                : "Ready";
                ListViewItem item = new(new[] { source, media.ToString(), ControlHelpers.FormatBytes(info.Exists ? info.Length : 0), status }) { Tag = source };
                _sourceList.Items.Add(item);
            }
        }
        finally { _sourceList.EndUpdate(); }
        _status.Text = $"{_sources.Count:N0} source file(s) selected.";
        UpdateButtons();
    }

    private void BuildCandidates()
    {
        _candidates.Clear();
        foreach (string source in _sources)
        {
            AssetImportCandidate candidate = AssetImportPlanner.CreateCandidate(source, _suggestedCategory);
            _candidates.Add(candidate);
        }
        ValidateAndRefreshGrid();
    }

    private void ValidateAndRefreshGrid()
    {
        foreach (AssetImportCandidate candidate in _candidates.Where(candidate => candidate.Action == AssetImportCandidateAction.AutoRename))
        {
            candidate.LogicalName = AssetImportPlanner.CreateAutomaticName(_session, candidate, _candidates);
            candidate.Action = AssetImportCandidateAction.Import;
        }
        AssetImportPlanner.ValidateAll(_session, _candidates);
        _updatingGrid = true;
        try
        {
            _grid.Rows.Clear();
            foreach (AssetImportCandidate candidate in _candidates)
            {
                int rowIndex = _grid.Rows.Add(
                    Path.GetFileName(candidate.SourcePath),
                    candidate.Category,
                    candidate.LogicalName,
                    candidate.Action,
                    candidate.DestinationDisplay,
                    candidate.Status);
                DataGridViewRow row = _grid.Rows[rowIndex];
                row.Tag = candidate;
                row.DefaultCellStyle.ForeColor = candidate.IsValid ? _grid.ForeColor : Color.Firebrick;
                row.Cells[0].ToolTipText = candidate.SourcePath;
            }
        }
        finally { _updatingGrid = false; }
        int valid = _candidates.Count(candidate => candidate.IsValid && candidate.Action != AssetImportCandidateAction.Skip);
        int invalid = _candidates.Count(candidate => !candidate.IsValid);
        _status.Text = $"{valid:N0} file(s) ready · {invalid:N0} require attention · {_candidates.Count(candidate => candidate.Action == AssetImportCandidateAction.Skip):N0} skipped";
        InvalidatePreview();
        UpdateButtons();
    }

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingGrid || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[e.RowIndex].Tag is not AssetImportCandidate candidate) return;
        try
        {
            object? value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            switch (_grid.Columns[e.ColumnIndex].Name)
            {
                case "Category" when value is AssetCategory category:
                    candidate.Category = category;
                    break;
                case "LogicalName":
                    candidate.LogicalName = Convert.ToString(value) ?? string.Empty;
                    break;
                case "Action" when value is AssetImportCandidateAction action:
                    candidate.Action = action;
                    break;
            }
            ValidateAndRefreshGrid();
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void SetCategoryForSelected()
    {
        using Form picker = new()
        {
            Text = "Choose Asset Category",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 120)
        };
        ComboBox categories = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(10) };
        categories.Items.AddRange(Enum.GetValues<AssetCategory>().Where(value => value != AssetCategory.Unknown && AssetCategoryRegistry.Directories.ContainsKey(value)).Cast<object>().ToArray());
        categories.SelectedIndex = 0;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        picker.Controls.Add(categories);
        picker.Controls.Add(buttons);
        picker.AcceptButton = ok;
        picker.CancelButton = cancel;
        if (picker.ShowDialog(this) != DialogResult.OK || categories.SelectedItem is not AssetCategory selected) return;
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.Tag is AssetImportCandidate candidate && AssetImportPlanner.IsMediaCompatible(selected, candidate.MediaKind)) candidate.Category = selected;
        }
        ValidateAndRefreshGrid();
    }

    private void AutoRenameConflicts()
    {
        foreach (AssetImportCandidate candidate in SelectedCandidates().Where(candidate =>
                     candidate.Action == AssetImportCandidateAction.ResolveConflict
                     || candidate.Status.StartsWith("Duplicate destination within this batch", StringComparison.Ordinal)))
        {
            candidate.Action = AssetImportCandidateAction.AutoRename;
        }
        ValidateAndRefreshGrid();
    }

    private void SkipInvalid()
    {
        foreach (AssetImportCandidate candidate in SelectedCandidates().Where(candidate => !candidate.IsValid)) candidate.Action = AssetImportCandidateAction.Skip;
        ValidateAndRefreshGrid();
    }

    private IEnumerable<AssetImportCandidate> SelectedCandidates()
    {
        DataGridViewRow[] selected = _grid.SelectedRows.Cast<DataGridViewRow>().ToArray();
        return selected.Length == 0
            ? _candidates
            : selected.Select(row => row.Tag).OfType<AssetImportCandidate>();
    }

    private async Task MoveNextAsync()
    {
        switch (_pages.SelectedIndex)
        {
            case 0:
                if (_sources.Count == 0)
                {
                    MessageBox.Show(this, "Add at least one source file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                BuildCandidates();
                ShowPage(1);
                break;
            case 1:
                ValidateAndRefreshGrid();
                if (_candidates.Any(candidate => !candidate.IsValid))
                {
                    MessageBox.Show(this, "Resolve or skip every invalid row before continuing.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ShowPage(2);
                await PrepareReviewAsync().ConfigureAwait(true);
                break;
        }
    }

    private void ChangePage(int delta)
    {
        int next = Math.Clamp(_pages.SelectedIndex + delta, 0, _pages.TabPages.Count - 1);
        ShowPage(next);
    }

    private void ShowPage(int index)
    {
        _pages.SelectedIndex = index;
        _pageTitle.Text = index switch
        {
            0 => "1. Select source files",
            1 => "2. Classify assets and resolve conflicts",
            _ => "3. Review the atomic project transaction"
        };
        UpdateButtons();
    }

    private async Task PrepareReviewAsync()
    {
        InvalidatePreview();
        ValidateAndRefreshGrid();
        if (_candidates.Any(candidate => !candidate.IsValid))
        {
            _review.Text = "The import cannot be previewed until every invalid row is resolved or skipped.";
            return;
        }
        AssetImportPlan plan = AssetImportPlanner.BuildPlan(_candidates);
        if (plan.EffectiveItems.Count == 0)
        {
            _review.Text = "No files are selected for import.";
            return;
        }

        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancellationToken token = _previewCancellation.Token;
        SetBusy(true, "Building transaction preview…");
        try
        {
            AssetManagementPreview preview = await _session.AssetOperations.PreviewBatchImportAsync(plan, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _validatedPlan = plan;
            _validatedPreview = preview;
            _review.Text = BuildReviewText(preview, plan);
            _finish.Enabled = true;
            _status.Text = $"Preview ready: {plan.EffectiveItems.Count:N0} asset(s), {preview.Transaction.ChangedFileCount:N0} file change(s).";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _review.Text = "Preview failed:" + Environment.NewLine + Environment.NewLine + exception;
            _status.Text = exception.Message;
        }
        finally
        {
            if (!IsDisposed && !Disposing) SetBusy(false);
        }
    }

    private async Task FinishAsync()
    {
        if (_validatedPlan is null || _validatedPreview is null) return;
        DialogResult result = MessageBox.Show(
            this,
            $"Stage {_validatedPlan.EffectiveItems.Count:N0} asset(s) as one undoable project transaction?\n\nThe project files are not written until Save Project.",
            "Stage Batch Import",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result != DialogResult.OK) return;

        bool completed = false;
        _importing = true;
        SetBusy(true, "Staging batch import…");
        try
        {
            await _session.AssetOperations.ImportBatchAsync(_validatedPlan).ConfigureAwait(true);
            ImportedCount = _validatedPlan.EffectiveItems.Count;
            completed = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            InvalidatePreview();
        }
        finally
        {
            _importing = false;
            if (!IsDisposed && !Disposing) SetBusy(false);
        }

        if (completed)
        {
            DialogResult = DialogResult.OK;
            if (!IsDisposed) Close();
        }
    }

    private void InvalidatePreview()
    {
        _previewCancellation?.Cancel();
        _validatedPlan = null;
        _validatedPreview = null;
        _finish.Enabled = false;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        UseWaitCursor = busy;
        _back.Enabled = !busy && _pages.SelectedIndex > 0;
        _next.Enabled = !busy && _pages.SelectedIndex < 2;
        _previewAgain.Enabled = !busy;
        _cancel.Enabled = !busy;
        if (status is not null) _status.Text = status;
        if (!busy) UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool busy = UseWaitCursor;
        _back.Enabled = !busy && _pages.SelectedIndex > 0;
        _next.Visible = _pages.SelectedIndex < 2;
        _next.Enabled = !busy && (_pages.SelectedIndex != 0 || _sources.Count > 0);
        _finish.Visible = _pages.SelectedIndex == 2;
        _finish.Enabled = !busy && _validatedPlan is not null;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddSources(paths);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_importing)
        {
            e.Cancel = true;
            return;
        }
        _previewCancellation?.Cancel();
    }

    private static string BuildReviewText(AssetManagementPreview preview, AssetImportPlan plan)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine(preview.Description);
        text.AppendLine(new string('=', Math.Max(8, preview.Description.Length)));
        text.AppendLine();
        text.AppendLine($"Import items: {plan.EffectiveItems.Count:N0}");
        text.AppendLine($"Physical file changes: {preview.Transaction.ChangedFileCount:N0}");
        text.AppendLine($"JSON documents changed: {preview.Transaction.ChangedDocumentCount:N0}");
        text.AppendLine();
        text.AppendLine("File operations");
        text.AppendLine("---------------");
        foreach (string change in preview.FileChanges) text.AppendLine(change);
        return text.ToString();
    }

    private sealed record ImportActionOption(AssetImportCandidateAction Value, string DisplayName);
}
