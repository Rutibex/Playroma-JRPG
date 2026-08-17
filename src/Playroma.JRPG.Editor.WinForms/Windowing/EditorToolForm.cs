using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

/// <summary>
/// Base class for all new top-level editor windows. It owns only window lifecycle behavior;
/// editor-specific behavior remains in composed controls and services.
/// </summary>
public class EditorToolForm : Form, IWinFormsEditorWindow, IEditorCommandTarget
{
    private readonly Panel _contentHost = new() { Dock = DockStyle.Fill };
    private readonly LiveRegionAnnouncer _liveRegion = new();
    private bool _loadStarted;
    private bool _manualClosePending;
    private bool _serviceCloseApproved;
    private bool _asyncDisposed;

    public EditorToolForm(WindowRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Text = request.DisplayTitle ?? request.Identity;
        Name = request.Kind + "Form";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(480, 320);
        Size = new Size(960, 680);
        ShowInTaskbar = request.Presentation == WindowPresentation.Modeless;
        KeyPreview = true;
        Controls.Add(_contentHost);
        InstallStatusContent("Loading…");
        Shown += (_, _) =>
        {
            _liveRegion.Attach(this);
            AccessibilityMetadata.ApplyFormDefaults(this);
            FocusVisibilityService.Install(this);
        };
    }

    public WindowRequest Request { get; }
    public WindowKey Key => Request.Key;
    public virtual string Title => Request.DisplayTitle ?? Request.Identity;
    public virtual string? ToolTip => Request.ToolTip ?? Request.Document?.RelativePath;
    public virtual bool IsDirty => false;
    public bool CanActivate => !_asyncDisposed && !IsDisposed && LoadState is not WindowLoadState.Closing and not WindowLoadState.Closed;
    public bool IsVisible => Visible;
    public WindowLoadState LoadState { get; private set; } = WindowLoadState.Created;
    public Form Form => this;

    public event EventHandler? StateChanged;
    public event EventHandler<WindowCloseRequestedEventArgs>? CloseRequested;
    public event EventHandler? CommandStateChanged;

    public virtual EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context) =>
        EditorCommandStatus.NotHandled(command);

    public virtual ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context) =>
        ValueTask.CompletedTask;

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loadStarted) return;
        _loadStarted = true;
        SetLoadState(WindowLoadState.Loading);
        try
        {
            await OnLoadWindowAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            SetLoadState(WindowLoadState.Ready);
        }
        catch (Exception exception)
        {
            InstallLoadError(exception);
            SetLoadState(WindowLoadState.Failed);
            throw;
        }
    }

    public virtual ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public virtual NavigationLocation? CaptureLocation() =>
        Request.Location ?? (Request.Document is null ? null : new NavigationLocation(Request.Document));

    public virtual ValueTask RestoreLocationAsync(
        NavigationLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public void RefreshWindowState() => NotifyStateChanged();

    public void MarkStale()
    {
        if (LoadState is WindowLoadState.Ready or WindowLoadState.Stale)
        {
            SetLoadState(WindowLoadState.Stale);
        }
    }

    public void CompleteCloseRequest(bool closeWindow)
    {
        if (!closeWindow)
        {
            _manualClosePending = false;
            return;
        }

        _serviceCloseApproved = true;
        SetLoadState(WindowLoadState.Closing);
        Close();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_asyncDisposed) return;
        _asyncDisposed = true;
        try
        {
            // Dispose the visual tree before releasing document leases or other async lifetimes.
            // Control disposal handlers may still need the underlying editor data.
            if (!IsDisposed) Dispose();
            await OnDisposeWindowAsync().ConfigureAwait(true);
        }
        finally
        {
            _liveRegion.Dispose();
            SetLoadState(WindowLoadState.Closed);
            GC.SuppressFinalize(this);
        }
    }

    protected Panel ContentHost => _contentHost;
    protected bool IsServiceCloseApproved => _serviceCloseApproved;

    protected void InstallContent(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _contentHost.SuspendLayout();
        try
        {
            _contentHost.Controls.Clear();
            control.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(control);
        }
        finally
        {
            _contentHost.ResumeLayout(performLayout: true);
        }
    }

    protected virtual ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnDisposeWindowAsync() => ValueTask.CompletedTask;

    protected void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        CommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void NotifyCommandStateChanged() => CommandStateChanged?.Invoke(this, EventArgs.Empty);

    protected void AnnounceAccessibilityStatus(string message) => _liveRegion.Announce(message);

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        NotifyStateChanged();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_serviceCloseApproved ||
            Request.Presentation == WindowPresentation.Modal ||
            e.CloseReason == CloseReason.WindowsShutDown ||
            CloseRequested is null)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        if (!_manualClosePending)
        {
            _manualClosePending = true;
            CloseRequested?.Invoke(this, new WindowCloseRequestedEventArgs(e.CloseReason));
        }
    }

    private void InstallStatusContent(string text)
    {
        InstallContent(new Label
        {
            Text = text,
            AccessibleName = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        });
    }

    private void InstallLoadError(Exception exception)
    {
        TextBox error = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = "This editor window could not be loaded." + Environment.NewLine + Environment.NewLine + exception
        };
        InstallContent(error);
    }

    private void SetLoadState(WindowLoadState state)
    {
        if (LoadState == state) return;
        LoadState = state;
        if (state == WindowLoadState.Ready) AnnounceAccessibilityStatus($"{Title} ready.");
        else if (state == WindowLoadState.Failed) AnnounceAccessibilityStatus($"{Title} could not be loaded.");
        NotifyStateChanged();
    }
}
