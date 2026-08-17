using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;

namespace Playroma.JRPG.Editor.WinForms.Assets;

/// <summary>
/// Format-aware Phase 5 asset preview. The control owns every decoded resource and
/// obtains content through the staged project overlay, so no source file remains locked.
/// </summary>
public sealed class AssetPreviewControl : UserControl
{
    private readonly AssetCatalogueService _catalogue;
    private readonly IAssetContentSource _content;
    private readonly WindowsAudioWaveformProvider _waveforms;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly AssetImageCanvas _image = new() { Dock = DockStyle.Fill };
    private readonly WaveformControl _waveform = new() { Dock = DockStyle.Fill };
    private readonly Panel _previewHost = new() { Dock = DockStyle.Fill };
    private readonly TextBox _metadata = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9f), WordWrap = false };
    private readonly DataGridView _usedBy = ControlHelpers.CreateReadOnlyGrid();
    private readonly ListView _variants = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly ListView _diagnostics = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly FlowLayoutPanel _previewToolbar = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(3), WrapContents = false, AutoScroll = true };
    private readonly TrackBar _zoom = new() { Minimum = 10, Maximum = 800, Value = 100, TickFrequency = 50, Width = 140 };
    private readonly CheckBox _grid = new() { Text = "Grid", Checked = true, AutoSize = true, Padding = new Padding(4, 7, 4, 0) };
    private readonly NumericUpDown _cell = new() { Minimum = 0, Maximum = 0, Width = 68 };
    private readonly Button _play = new() { Text = "Play", AutoSize = true };
    private readonly Button _pause = new() { Text = "Pause", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly CheckBox _loop = new() { Text = "Loop", AutoSize = true, Padding = new Padding(3, 7, 0, 0) };
    private readonly TrackBar _position = new() { Minimum = 0, Maximum = 1000, Width = 160, TickStyle = TickStyle.None };
    private readonly TrackBar _volume = new() { Minimum = 0, Maximum = 100, Value = 80, Width = 90, TickStyle = TickStyle.None };
    private readonly Label _time = new() { AutoSize = true, Padding = new Padding(3, 8, 3, 0) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 25, AutoEllipsis = true, Padding = new Padding(4) };
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _variantCancellation;
    private PrivateFontPreviewSession? _fontSession;
    private Label? _fontSample;
    private WindowsAudioPreviewSession? _audio;
    private AssetRecord? _current;
    private bool _updatingPosition;
    private bool _suppressVariantSelection;

    public AssetPreviewControl(MvProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _catalogue = session.Assets;
        _content = session.AssetContent;
        _waveforms = new WindowsAudioWaveformProvider(_content);
        Dock = DockStyle.Fill;

        _usedBy.Columns.Add("Confidence", "Confidence");
        _usedBy.Columns.Add("Source", "Source");
        _usedBy.Columns.Add("Location", "Location");
        _usedBy.Columns.Add("Context", "Context");
        _usedBy.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _variants.Columns.Add("Extension", 75);
        _variants.Columns.Add("Path", 285);
        _variants.Columns.Add("Size", 90);
        _variants.Columns.Add("Status", 115);
        _diagnostics.Columns.Add("Severity", 80);
        _diagnostics.Columns.Add("Code", 95);
        _diagnostics.Columns.Add("Message", 420);

        _previewToolbar.Controls.AddRange([
            new Label { Text = "Zoom", AutoSize = true, Padding = new Padding(0, 8, 0, 0) },
            _zoom,
            _grid,
            new Label { Text = "Cell", AutoSize = true, Padding = new Padding(8, 8, 0, 0) },
            _cell,
            _play,
            _pause,
            _stop,
            _loop,
            _position,
            _time,
            new Label { Text = "Volume", AutoSize = true, Padding = new Padding(8, 8, 0, 0) },
            _volume
        ]);

        Panel previewPage = new() { Dock = DockStyle.Fill };
        previewPage.Controls.Add(_previewHost);
        previewPage.Controls.Add(_previewToolbar);
        _tabs.TabPages.Add(new TabPage("Preview") { Controls = { previewPage } });
        _tabs.TabPages.Add(new TabPage("Metadata") { Controls = { _metadata } });
        _tabs.TabPages.Add(new TabPage("Used By") { Controls = { _usedBy } });
        _tabs.TabPages.Add(new TabPage("Variants") { Controls = { _variants } });
        _tabs.TabPages.Add(new TabPage("Diagnostics") { Controls = { _diagnostics } });
        Controls.Add(_tabs);
        Controls.Add(_status);

        _zoom.ValueChanged += (_, _) => _image.Zoom = _zoom.Value / 100f;
        _grid.CheckedChanged += (_, _) => _image.ShowGrid = _grid.Checked;
        _cell.ValueChanged += (_, _) => _image.SelectedCell = decimal.ToInt32(_cell.Value);
        _play.Click += (_, _) => RunAudioAction(audio => audio.Play());
        _pause.Click += (_, _) => RunAudioAction(audio => audio.Pause());
        _stop.Click += (_, _) => RunAudioAction(audio => audio.Stop());
        _loop.CheckedChanged += (_, _) => { if (_audio is not null) _audio.LoopEnabled = _loop.Checked; };
        _volume.ValueChanged += (_, _) => { if (_audio is not null) _audio.Volume = _volume.Value / 100f; };
        _position.Scroll += (_, _) => SeekFromTrackBar();
        _variants.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressVariantSelection) return;
            await PreviewSelectedVariantAsync().ConfigureAwait(true);
        };
        SetAudioButtons(false);
    }

    public event EventHandler? SelectedVariantChanged;

    public AssetRecord? Current => _current;
    public AssetVariant? SelectedVariant => _variants.SelectedItems.Count == 1 ? _variants.SelectedItems[0].Tag as AssetVariant : _current?.PreferredVariant;
    public string SelectedTabName => _tabs.SelectedTab?.Text ?? "Preview";

    public void SelectTab(string? tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName)) return;
        TabPage? page = _tabs.TabPages.Cast<TabPage>()
            .FirstOrDefault(candidate => candidate.Text.Equals(tabName, StringComparison.OrdinalIgnoreCase));
        if (page is not null) _tabs.SelectedTab = page;
    }

    public async ValueTask ShowAssetAsync(AssetRecord? record, CancellationToken cancellationToken = default)
    {
        CancelCurrentLoad();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _loadCancellation.Token;
        DisposeAudio();
        DisposePrivateFont();
        _current = record;
        _image.SetImage(null);
        _waveform.SetWaveform(null);
        ClearPreviewHost();
        _metadata.Clear();
        _usedBy.Rows.Clear();
        _variants.Items.Clear();
        _diagnostics.Items.Clear();
        SetAudioButtons(false);
        _time.Text = string.Empty;

        if (record is null)
        {
            _status.Text = "Select an asset.";
            return;
        }

        PopulateReferenceAndVariantTabs(record);
        if (record.IsMissing)
        {
            ShowMessage("The asset is referenced but no physical file exists.");
            _metadata.Text = BuildMetadataText(record);
            _status.Text = "Missing asset";
            return;
        }

        try
        {
            _status.Text = "Reading metadata…";
            AssetRecord analysed = await _catalogue.EnsureMetadataAsync(record.Key, token);
            token.ThrowIfCancellationRequested();
            _current = analysed;
            _metadata.Text = BuildMetadataText(analysed);
            PopulateDiagnostics(analysed);
            AssetVariant? preferred = analysed.PreferredVariant;
            SelectVariantInList(preferred);
            if (preferred is null)
            {
                ShowMessage("No physical variant is available.");
                return;
            }
            await PreviewVariantAsync(analysed, preferred, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer selection owns the preview.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            ShowMessage("Preview unavailable: " + exception.Message);
            _status.Text = exception.Message;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelCurrentLoad();
            DisposeAudio();
            DisposePrivateFont();
        }
        base.Dispose(disposing);
    }

    private void PopulateReferenceAndVariantTabs(AssetRecord record)
    {
        foreach (AssetReferenceHint reference in record.Usage.References)
        {
            _usedBy.Rows.Add(reference.IsExact ? "Exact" : "Heuristic", reference.SourceRelativePath, reference.SourceLocation, reference.Context ?? string.Empty);
        }
        foreach (AssetVariant variant in record.Variants)
        {
            ListViewItem item = new(variant.Extension);
            item.SubItems.Add(variant.RelativePath);
            item.SubItems.Add(ControlHelpers.FormatBytes(variant.Size));
            item.SubItems.Add(variant.Status.ToString());
            item.Tag = variant;
            _variants.Items.Add(item);
        }
        PopulateDiagnostics(record);
    }

    private void PopulateDiagnostics(AssetRecord record)
    {
        _diagnostics.Items.Clear();
        foreach (Playroma.JRPG.Core.ProjectDiagnostic diagnostic in record.Diagnostics)
        {
            ListViewItem item = new(diagnostic.Severity.ToString());
            item.SubItems.Add(diagnostic.Code);
            item.SubItems.Add(diagnostic.Message);
            item.ToolTipText = diagnostic.TechnicalDetail;
            _diagnostics.Items.Add(item);
        }
    }

    private async Task PreviewSelectedVariantAsync()
    {
        SelectedVariantChanged?.Invoke(this, EventArgs.Empty);
        if (_variants.SelectedItems.Count != 1
            || _variants.SelectedItems[0].Tag is not AssetVariant variant
            || _current is null) return;

        _variantCancellation?.Cancel();
        _variantCancellation?.Dispose();
        _variantCancellation = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation?.Token ?? CancellationToken.None);
        CancellationToken token = _variantCancellation.Token;
        try
        {
            await PreviewVariantAsync(_current, variant, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            ShowMessage("Preview unavailable: " + exception.Message);
            _status.Text = exception.Message;
        }
        finally
        {
            SelectedVariantChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async ValueTask PreviewVariantAsync(AssetRecord record, AssetVariant variant, CancellationToken cancellationToken)
    {
        DisposeAudio();
        DisposePrivateFont();
        _image.SetImage(null);
        _waveform.SetWaveform(null);
        SetAudioButtons(false);
        if (variant.IsEncrypted)
        {
            ShowMessage("Encrypted deployment assets are catalogued but are not decoded or edited.");
            _status.Text = "Encrypted asset";
            return;
        }
        if (variant.Status == AssetStatus.PendingDeletion)
        {
            ShowMessage("This file is staged for deletion. Undo the operation to restore its preview.");
            _status.Text = "Pending deletion";
            return;
        }

        _status.Text = $"Loading {variant.RelativePath}…";
        switch (record.MediaKind)
        {
            case AssetMediaKind.Image:
                await ShowImageAsync(record, variant, cancellationToken);
                break;
            case AssetMediaKind.Audio:
                await ShowAudioAsync(record, variant, cancellationToken);
                break;
            case AssetMediaKind.Font:
                ShowFont(variant);
                break;
            case AssetMediaKind.Movie:
                ShowMessage("Movie metadata is available. Use Open Externally for playback.");
                break;
            default:
                ShowMessage("No preview provider supports this file format.");
                break;
        }
        _status.Text = $"{record.Key} — {variant.Extension} — {record.Status}";
    }

    private async ValueTask ShowImageAsync(AssetRecord record, AssetVariant variant, CancellationToken cancellationToken)
    {
        Bitmap? image = null;
        try
        {
            image = await AssetImageLoader.LoadOwnedBitmapAsync(_content, variant.RelativePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SheetCell> cells = BuildCells(record);
            ClearPreviewHost();
            _previewHost.Controls.Add(_image);
            _image.SetImage(image, cells);
            image = null; // Ownership transferred to the canvas.
            _cell.Maximum = Math.Max(0, cells.Count - 1);
            _cell.Value = 0;
            _cell.Enabled = cells.Count > 0;
            _grid.Enabled = cells.Count > 0;
        }
        finally
        {
            image?.Dispose();
        }
    }

    private async ValueTask ShowAudioAsync(AssetRecord record, AssetVariant variant, CancellationToken cancellationToken)
    {
        WaveformEnvelope? waveform = null;
        if (_waveforms.CanHandle(record, variant))
        {
            try
            {
                waveform = await _waveforms.CreateAsync(record, variant, 5_000, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                _status.Text = "Waveform unavailable: " + exception.Message;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        ClearPreviewHost();
        _previewHost.Controls.Add(_waveform);
        _waveform.SetWaveform(waveform, record.Metadata.Audio?.LoopStart, record.Metadata.Audio?.LoopLength);

        string fullPath = _content.GetFullPath(variant.RelativePath);
        WindowsAudioPreviewSession? audioSession = null;
        try
        {
            audioSession = await Task.Run(() => new WindowsAudioPreviewSession(fullPath, variant.Extension), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _audio = audioSession;
            audioSession = null;
        }
        finally
        {
            if (audioSession is not null) await audioSession.DisposeAsync();
        }
        _audio.Volume = _volume.Value / 100f;
        _audio.LoopEnabled = _loop.Checked;
        if (record.Metadata.Audio is { LoopStart: long loopStart } audio)
        {
            int sampleRate = audio.SampleRate ?? 44_100;
            _audio.LoopStart = TimeSpan.FromSeconds(loopStart / (double)Math.Max(1, sampleRate));
            if (audio.LoopEnd is long loopEnd) _audio.LoopEnd = TimeSpan.FromSeconds(loopEnd / (double)Math.Max(1, sampleRate));
        }
        _audio.PositionChanged += OnAudioPositionChanged;
        _audio.PlaybackStopped += OnAudioPlaybackStopped;
        SetAudioButtons(true);
        UpdateAudioPosition();
    }

    private void ShowFont(AssetVariant variant)
    {
        DisposePrivateFont();
        try
        {
            _fontSession = new PrivateFontPreviewSession(_content.GetFullPath(variant.RelativePath));
            _fontSample = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Font = _fontSession.CreateFont(22f),
                Text = "Playroma jRPG\r\nABCDEFGHIJKLMNOPQRSTUVWXYZ\r\nabcdefghijklmnopqrstuvwxyz\r\n0123456789\r\nThe quick brown fox jumps over the lazy dog."
            };
            ClearPreviewHost();
            _previewHost.Controls.Add(_fontSample);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or InvalidDataException)
        {
            ShowMessage("Font preview is unavailable: " + exception.Message);
        }
    }

    private IReadOnlyList<SheetCell> BuildCells(AssetRecord record)
    {
        ImageAssetMetadata? image = record.Metadata.Image;
        if (image is null) return [];
        return record.Key.Category switch
        {
            AssetCategory.Characters => MvSpriteSheetDescriptors.DescribeCharacter(record.Key.LogicalName, image.Width, image.Height).Cells,
            AssetCategory.Faces => MvSpriteSheetDescriptors.DescribeFaces(image.Width, image.Height).Cells,
            AssetCategory.Animations => MvSpriteSheetDescriptors.DescribeAnimation(image.Width, image.Height).Cells,
            AssetCategory.SideViewActors => MvSpriteSheetDescriptors.DescribeSideViewActor(image.Width, image.Height).Cells,
            AssetCategory.Tilesets => MvSpriteSheetDescriptors.DescribeTiles(image.Width, image.Height).Cells,
            AssetCategory.System when record.Key.LogicalName.Equals("IconSet", StringComparison.OrdinalIgnoreCase) => MvSpriteSheetDescriptors.DescribeIconSet(image.Width, image.Height).Cells,
            _ => []
        };
    }

    private void SelectVariantInList(AssetVariant? variant)
    {
        if (variant is null) return;
        _suppressVariantSelection = true;
        try
        {
            foreach (ListViewItem item in _variants.Items)
            {
                if (item.Tag is AssetVariant candidate && string.Equals(candidate.RelativePath, variant.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    return;
                }
            }
        }
        finally
        {
            _suppressVariantSelection = false;
        }
    }

    private void OnAudioPositionChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(UpdateAudioPosition)); return; }
        UpdateAudioPosition();
    }

    private void OnAudioPlaybackStopped(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(UpdateAudioPosition)); return; }
        UpdateAudioPosition();
    }

    private void UpdateAudioPosition()
    {
        if (_audio is null) return;
        _updatingPosition = true;
        try
        {
            double duration = Math.Max(0.001, _audio.Duration.TotalSeconds);
            double fraction = _audio.Position.TotalSeconds / duration;
            _position.Value = Math.Clamp((int)Math.Round(fraction * _position.Maximum), _position.Minimum, _position.Maximum);
            _waveform.PositionFraction = fraction;
            _time.Text = $"{FormatTime(_audio.Position)} / {FormatTime(_audio.Duration)}";
        }
        finally { _updatingPosition = false; }
    }

    private void SeekFromTrackBar()
    {
        if (_audio is null || _updatingPosition) return;
        double ratio = _position.Value / (double)Math.Max(1, _position.Maximum);
        _audio.Position = TimeSpan.FromTicks((long)Math.Round(_audio.Duration.Ticks * ratio));
        UpdateAudioPosition();
    }

    private void RunAudioAction(Action<WindowsAudioPreviewSession> action)
    {
        if (_audio is null) return;
        try { action(_audio); }
        catch (Exception exception) { _status.Text = exception.Message; }
    }

    private void DisposeAudio()
    {
        if (_audio is null) return;
        _audio.PositionChanged -= OnAudioPositionChanged;
        _audio.PlaybackStopped -= OnAudioPlaybackStopped;
        _audio.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _audio = null;
        SetAudioButtons(false);
    }

    private void DisposePrivateFont()
    {
        if (_fontSample is not null)
        {
            Font font = _fontSample.Font;
            _fontSample.Dispose();
            font.Dispose();
            _fontSample = null;
        }
        _fontSession?.Dispose();
        _fontSession = null;
    }

    private void CancelCurrentLoad()
    {
        _variantCancellation?.Cancel();
        _variantCancellation?.Dispose();
        _variantCancellation = null;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private static string BuildMetadataText(AssetRecord record)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine($"Logical asset: {record.Key}");
        text.AppendLine($"Media: {record.MediaKind}");
        text.AppendLine($"Status: {record.Status}");
        text.AppendLine($"Variants: {record.Variants.Count}");
        text.AppendLine($"Total size: {ControlHelpers.FormatBytes(record.TotalBytes)}");
        text.AppendLine($"Exact references: {record.Usage.ExactReferenceCount}");
        text.AppendLine($"Heuristic references: {record.Usage.HeuristicReferenceCount}");
        text.AppendLine($"Runtime reserved: {record.Usage.IsRuntimeReserved}");
        if (record.Metadata.Image is { } image)
        {
            text.AppendLine(); text.AppendLine("IMAGE");
            text.AppendLine($"Format: {image.Format}"); text.AppendLine($"Dimensions: {image.Width} × {image.Height}"); text.AppendLine($"Bit depth: {image.BitDepth}"); text.AppendLine($"Alpha: {image.HasAlpha}"); text.AppendLine($"Valid: {image.IsValid}");
            if (!string.IsNullOrWhiteSpace(image.Detail)) text.AppendLine(image.Detail);
        }
        if (record.Metadata.Audio is { } audio)
        {
            text.AppendLine(); text.AppendLine("AUDIO");
            text.AppendLine($"Format: {audio.Format}"); text.AppendLine($"Duration: {audio.Duration}"); text.AppendLine($"Channels: {audio.Channels}"); text.AppendLine($"Sample rate: {audio.SampleRate}"); text.AppendLine($"Bits: {audio.BitsPerSample}"); text.AppendLine($"Bit rate: {audio.AverageBitRate}"); text.AppendLine($"Loop start: {audio.LoopStart}"); text.AppendLine($"Loop length: {audio.LoopLength}"); text.AppendLine($"Loop end: {audio.LoopEnd}");
            if (audio.Comments is not null) foreach ((string key, string value) in audio.Comments) text.AppendLine($"{key}: {value}");
            if (!string.IsNullOrWhiteSpace(audio.Detail)) text.AppendLine(audio.Detail);
        }
        if (record.Metadata.Movie is { } movie)
        {
            text.AppendLine(); text.AppendLine("MOVIE");
            text.AppendLine($"Container: {movie.Container}"); text.AppendLine($"Duration: {movie.Duration}"); text.AppendLine($"Dimensions: {movie.Width} × {movie.Height}"); text.AppendLine($"FPS: {movie.FramesPerSecond}"); text.AppendLine($"Video codec: {movie.VideoCodec}"); text.AppendLine($"Audio codec: {movie.AudioCodec}");
            if (!string.IsNullOrWhiteSpace(movie.Detail)) text.AppendLine(movie.Detail);
        }
        if (record.Metadata.Font is { } font)
        {
            text.AppendLine(); text.AppendLine("FONT");
            text.AppendLine($"Format: {font.Format}"); text.AppendLine($"Family: {font.Family}"); text.AppendLine($"Subfamily: {font.Subfamily}"); text.AppendLine($"Full name: {font.FullName}");
            if (!string.IsNullOrWhiteSpace(font.Detail)) text.AppendLine(font.Detail);
        }
        return text.ToString();
    }


    private void ClearPreviewHost()
    {
        Control[] controls = _previewHost.Controls.Cast<Control>().ToArray();
        _previewHost.Controls.Clear();
        foreach (Control control in controls)
        {
            if (ReferenceEquals(control, _image) || ReferenceEquals(control, _waveform) || ReferenceEquals(control, _fontSample)) continue;
            control.Dispose();
        }
    }

    private void ShowMessage(string message)
    {
        ClearPreviewHost();
        _previewHost.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Padding = new Padding(12) });
    }

    private void SetAudioButtons(bool enabled)
    {
        _play.Enabled = enabled;
        _pause.Enabled = enabled;
        _stop.Enabled = enabled;
        _loop.Enabled = enabled;
        _position.Enabled = enabled;
        _volume.Enabled = enabled;
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : value.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);
}
