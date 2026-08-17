using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

/// <summary>
/// UI-thread-owned registry for all modeless top-level editor windows.
/// </summary>
public sealed class WinFormsWindowService : IWinFormsWindowService, IAsyncDisposable
{
    private readonly Control _dispatcher;
    private readonly int _uiThreadId;
    private readonly WinFormsThemeService _theme;
    private readonly WinFormsWindowPlacementService _placement;
    private readonly INotificationService? _notifications;
    private readonly IWinFormsOwnerProvider? _ownerProvider;
    private readonly Dictionary<WindowKey, IWinFormsEditorWindow> _windows = [];
    private readonly Dictionary<WindowKey, TaskCompletionSource<DialogResult>> _modalCompletions = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private IEditorWindow? _activeWindow;

    public WinFormsWindowService(
        Control dispatcher,
        WinFormsThemeService theme,
        WinFormsWindowPlacementService placement,
        INotificationService? notifications = null,
        WinFormsWindowFactoryRegistry? factories = null,
        IWinFormsOwnerProvider? ownerProvider = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _notifications = notifications;
        _ownerProvider = ownerProvider;
        Factories = factories ?? new WinFormsWindowFactoryRegistry();
        _uiThreadId = Environment.CurrentManagedThreadId;
        _theme.ThemeChanged += OnThemeChanged;
    }

    public WinFormsWindowFactoryRegistry Factories { get; }

    public IReadOnlyList<IEditorWindow> OpenWindows
    {
        get
        {
            lock (_windows)
            {
                return _windows.Values.Cast<IEditorWindow>().ToArray();
            }
        }
    }

    public IEditorWindow? ActiveWindow => _activeWindow;

    public event EventHandler? WindowsChanged;
    public event EventHandler<EditorWindowEventArgs>? WindowOpened;
    public event EventHandler<EditorWindowEventArgs>? WindowClosed;
    public event EventHandler<ActiveEditorWindowChangedEventArgs>? ActiveWindowChanged;

    public bool TryGet(WindowKey key, out IEditorWindow? window)
    {
        lock (_windows)
        {
            bool found = _windows.TryGetValue(key, out IWinFormsEditorWindow? concrete);
            window = concrete;
            return found;
        }
    }

    public ValueTask<IEditorWindow> OpenAsync(
        WindowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Presentation == WindowPresentation.Modal)
        {
            throw new InvalidOperationException(
                "Modal windows must be opened with ShowDialogAsync so their result is preserved.");
        }

        return RunOnUiThreadAsync<IEditorWindow>(
            () => OpenCoreAsync(request, cancellationToken),
            cancellationToken);
    }

    public ValueTask<DialogResult> ShowDialogAsync(
        WindowRequest request,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WindowRequest modalRequest = request with { Presentation = WindowPresentation.Modal };
        return RunOnUiThreadAsync(
            () => ShowDialogCoreAsync(modalRequest, owner, prepare: null, cancellationToken: cancellationToken),
            cancellationToken);
    }

    public ValueTask<DialogResult> ShowDialogPreparedAsync(
        WindowRequest request,
        Func<IWinFormsEditorWindow, CancellationToken, ValueTask> prepare,
        IWin32Window? owner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prepare);
        WindowRequest modalRequest = request with { Presentation = WindowPresentation.Modal };
        return RunOnUiThreadAsync(
            () => ShowDialogCoreAsync(modalRequest, owner, prepare, cancellationToken),
            cancellationToken);
    }

    public ValueTask<bool> CloseAsync(
        IEditorWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        return RunOnUiThreadAsync(
            () => CloseCoreAsync(window, force: false, cancellationToken),
            cancellationToken);
    }

    public ValueTask<bool> CloseAllAsync(CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CloseMatchingCoreAsync(_ => true, force: false, cancellationToken),
            cancellationToken);

    public ValueTask<bool> CloseScopeAsync(
        WindowScope scope,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CloseMatchingCoreAsync(
                window => window.Request.Scope == scope,
                force: false,
                cancellationToken),
            cancellationToken);

    public ValueTask<bool> CanCloseAllAsync(CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CanCloseMatchingCoreAsync(_ => true, cancellationToken),
            cancellationToken);

    public ValueTask<bool> CanCloseScopeAsync(
        WindowScope scope,
        CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(
            () => CanCloseMatchingCoreAsync(window => window.Request.Scope == scope, cancellationToken),
            cancellationToken);

    public async ValueTask CloseAllForcedAsync(CancellationToken cancellationToken = default)
    {
        await RunOnUiThreadAsync(
            () => CloseMatchingCoreAsync(_ => true, force: true, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CloseScopeForcedAsync(
        WindowScope scope,
        CancellationToken cancellationToken = default)
    {
        await RunOnUiThreadAsync(
            () => CloseMatchingCoreAsync(
                window => window.Request.Scope == scope,
                force: true,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public void Activate(IEditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            if (_dispatcher.IsDisposed || _dispatcher.Disposing || !_dispatcher.IsHandleCreated) return;
            _dispatcher.BeginInvoke(new Action(() => Activate(window)));
            return;
        }

        if (window is not IWinFormsEditorWindow concrete || !concrete.CanActivate) return;
        Form form = concrete.Form;
        if (!form.Visible) ShowModeless(concrete);
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        if (!form.Visible) form.Show();
        form.BringToFront();
        form.Activate();
        SetActiveWindow(concrete);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _theme.ThemeChanged -= OnThemeChanged;
        try
        {
            await RunOnUiThreadAsync(
                () => CloseMatchingCoreAsync(_ => true, force: true, CancellationToken.None),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private async ValueTask<IEditorWindow> OpenCoreAsync(
        WindowRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (TryGetConcrete(request.Key, out IWinFormsEditorWindow? existing))
            {
                if (request.Location is not null)
                {
                    await existing.RestoreLocationAsync(request.Location, cancellationToken).ConfigureAwait(true);
                }
                Activate(existing);
                return existing;
            }

            IWinFormsWindowFactory factory = Factories.Resolve(request.Kind);
            IWinFormsEditorWindow window = await factory.CreateAsync(request, cancellationToken).ConfigureAwait(true);
            ValidateCreatedWindow(request, window);
            RegisterWindow(window);
            try
            {
                _placement.Restore(window.Form, request);
                _theme.Apply(window.Form);
                ShowModeless(window);
                SetActiveWindow(window);
                await window.LoadAsync(cancellationToken).ConfigureAwait(true);
                if (request.Location is not null)
                {
                    await window.RestoreLocationAsync(request.Location, cancellationToken).ConfigureAwait(true);
                }
                _theme.Apply(window.Form);
                return window;
            }
            catch (Exception exception)
            {
                await RemoveAndDisposeAsync(window, capturePlacement: false).ConfigureAwait(true);
                _notifications?.Error(
                    "Editor window failed to load",
                    $"{window.Title}: {exception.Message}",
                    exception);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<DialogResult> ShowDialogCoreAsync(
        WindowRequest request,
        IWin32Window? explicitOwner,
        Func<IWinFormsEditorWindow, CancellationToken, ValueTask>? prepare,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        IWinFormsEditorWindow? window = null;
        TaskCompletionSource<DialogResult>? completion = null;
        bool gateHeld = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            gateHeld = true;
            if (TryGetConcrete(request.Key, out IWinFormsEditorWindow? existing))
            {
                if (request.Location is not null)
                {
                    await existing.RestoreLocationAsync(request.Location, cancellationToken).ConfigureAwait(true);
                }
                Activate(existing);
                if (_modalCompletions.TryGetValue(request.Key, out TaskCompletionSource<DialogResult>? existingCompletion))
                {
                    _gate.Release();
                    gateHeld = false;
                    return await existingCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
                }
                return existing.Form.DialogResult;
            }

            IWinFormsWindowFactory factory = Factories.Resolve(request.Kind);
            window = await factory.CreateAsync(request, cancellationToken).ConfigureAwait(true);
            ValidateCreatedWindow(request, window);
            RegisterWindow(window);
            _placement.Restore(window.Form, request);
            _theme.Apply(window.Form);
            await window.LoadAsync(cancellationToken).ConfigureAwait(true);
            if (request.Location is not null)
            {
                await window.RestoreLocationAsync(request.Location, cancellationToken).ConfigureAwait(true);
            }
            if (prepare is not null)
            {
                await prepare(window, cancellationToken).ConfigureAwait(true);
            }
            _theme.Apply(window.Form);
            completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _modalCompletions[request.Key] = completion;
            IWin32Window? owner = explicitOwner ?? ResolveOwner(request.OwnerKey);
            SetActiveWindow(window);

            // ShowDialog runs a nested WinForms message loop. Never hold the registry gate while
            // that loop is active, otherwise coordinated shutdown and duplicate activation can deadlock.
            _gate.Release();
            gateHeld = false;
            DialogResult result = owner is null
                ? window.Form.ShowDialog()
                : window.Form.ShowDialog(owner);
            completion.TrySetResult(result);
            return result;
        }
        catch (Exception exception)
        {
            completion?.TrySetException(exception);
            throw;
        }
        finally
        {
            if (!gateHeld)
            {
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
                gateHeld = true;
            }
            if (completion is not null) _modalCompletions.Remove(request.Key);
            if (window is not null)
            {
                await RemoveAndDisposeAsync(window, capturePlacement: true).ConfigureAwait(true);
            }
            if (gateHeld) _gate.Release();
        }
    }

    private async ValueTask<bool> CloseCoreAsync(
        IEditorWindow window,
        bool force,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(allowDisposing: force);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!TryGetConcrete(window.Key, out IWinFormsEditorWindow? registered)) return true;
            if (!ReferenceEquals(registered, window)) return true;
            if (!force && !await registered.CanCloseAsync(cancellationToken).ConfigureAwait(true))
            {
                registered.CompleteCloseRequest(closeWindow: false);
                return false;
            }

            await RemoveAndDisposeAsync(registered, capturePlacement: true).ConfigureAwait(true);
            return true;
        }
        catch
        {
            if (window is IWinFormsEditorWindow concrete)
            {
                concrete.CompleteCloseRequest(closeWindow: false);
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<bool> CanCloseMatchingCoreAsync(
        Func<IWinFormsEditorWindow, bool> predicate,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            IWinFormsEditorWindow[] candidates;
            lock (_windows)
            {
                candidates = _windows.Values.Where(predicate).Reverse().ToArray();
            }

            foreach (IWinFormsEditorWindow window in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await window.CanCloseAsync(cancellationToken).ConfigureAwait(true)) continue;
                window.CompleteCloseRequest(closeWindow: false);
                return false;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<bool> CloseMatchingCoreAsync(
        Func<IWinFormsEditorWindow, bool> predicate,
        bool force,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed(allowDisposing: force);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            IWinFormsEditorWindow[] candidates;
            lock (_windows)
            {
                candidates = _windows.Values.Where(predicate).Reverse().ToArray();
            }

            if (!force)
            {
                foreach (IWinFormsEditorWindow window in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await window.CanCloseAsync(cancellationToken).ConfigureAwait(true)) continue;
                    window.CompleteCloseRequest(closeWindow: false);
                    return false;
                }
            }

            foreach (IWinFormsEditorWindow window in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RemoveAndDisposeAsync(window, capturePlacement: true).ConfigureAwait(true);
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RegisterWindow(IWinFormsEditorWindow window)
    {
        lock (_windows)
        {
            if (!_windows.TryAdd(window.Key, window))
            {
                throw new InvalidOperationException($"Window '{window.Key}' is already registered.");
            }
        }

        window.StateChanged += OnWindowStateChanged;
        window.CloseRequested += OnWindowCloseRequested;
        window.Form.Activated += OnFormActivated;
        window.Form.FormClosed += OnFormClosedUnexpectedly;
        WindowsChanged?.Invoke(this, EventArgs.Empty);
        WindowOpened?.Invoke(this, new EditorWindowEventArgs(window));
    }

    private async ValueTask RemoveAndDisposeAsync(
        IWinFormsEditorWindow window,
        bool capturePlacement)
    {
        bool removed;
        lock (_windows)
        {
            removed = _windows.Remove(window.Key);
        }
        if (!removed) return;

        window.StateChanged -= OnWindowStateChanged;
        window.CloseRequested -= OnWindowCloseRequested;
        window.Form.Activated -= OnFormActivated;
        window.Form.FormClosed -= OnFormClosedUnexpectedly;
        if (capturePlacement) _placement.Capture(window.Form, window.Request);
        if (!window.Form.IsDisposed && window.Form.Visible)
        {
            window.CompleteCloseRequest(closeWindow: true);
        }
        await window.DisposeAsync().ConfigureAwait(true);

        if (ReferenceEquals(_activeWindow, window))
        {
            IEditorWindow? next;
            lock (_windows)
            {
                next = _windows.Values.LastOrDefault(candidate => candidate.CanActivate);
            }
            SetActiveWindow(next);
        }
        WindowsChanged?.Invoke(this, EventArgs.Empty);
        WindowClosed?.Invoke(this, new EditorWindowEventArgs(window));
    }

    private void ShowModeless(IWinFormsEditorWindow window)
    {
        IWin32Window? owner = ResolveOwner(window.Request.OwnerKey);
        if (owner is null || window.Request.Presentation == WindowPresentation.Modeless)
        {
            window.Form.Show();
        }
        else
        {
            window.Form.Show(owner);
        }
    }

    private IWin32Window? ResolveOwner(WindowKey? ownerKey)
    {
        if (ownerKey.HasValue && TryGetConcrete(ownerKey.Value, out IWinFormsEditorWindow? owner))
        {
            return owner.Form;
        }
        IWin32Window? current = _ownerProvider?.CurrentOwner;
        if (current is Form currentForm && (currentForm.IsDisposed || currentForm.Disposing)) current = null;
        return current ?? _dispatcher.FindForm();
    }

    private bool TryGetConcrete(WindowKey key, out IWinFormsEditorWindow? window)
    {
        lock (_windows)
        {
            return _windows.TryGetValue(key, out window);
        }
    }

    private static void ValidateCreatedWindow(WindowRequest request, IWinFormsEditorWindow window)
    {
        if (window is null) throw new InvalidOperationException("The window factory returned null.");
        if (window.Key != request.Key)
        {
            throw new InvalidOperationException(
                $"Factory for '{request.Kind}' created key '{window.Key}' instead of '{request.Key}'.");
        }
        if (window.Form.IsDisposed)
        {
            throw new InvalidOperationException($"Factory for '{request.Kind}' returned a disposed Form.");
        }
    }

    private void SetActiveWindow(IEditorWindow? window)
    {
        if (ReferenceEquals(_activeWindow, window)) return;
        IEditorWindow? previous = _activeWindow;
        _activeWindow = window;
        ActiveWindowChanged?.Invoke(this, new ActiveEditorWindowChangedEventArgs(previous, window));
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is not IWinFormsEditorWindow window) return;
        Form form = window.Form;
        if (form.IsDisposed || form.Disposing) return;
        if (form.InvokeRequired)
        {
            form.BeginInvoke(new Action(() => OnWindowStateChanged(window, EventArgs.Empty)));
            return;
        }
        string dirty = window.IsDirty ? " *" : string.Empty;
        string stale = window.LoadState == WindowLoadState.Stale ? " [external change]" : string.Empty;
        form.Text = window.Title + dirty + stale;
    }

    private async void OnWindowCloseRequested(object? sender, WindowCloseRequestedEventArgs e)
    {
        if (sender is not IWinFormsEditorWindow window) return;
        try
        {
            await CloseAsync(window).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            window.CompleteCloseRequest(closeWindow: false);
            _notifications?.Error("Could not close editor window", exception.Message, exception);
        }
    }

    private void OnFormActivated(object? sender, EventArgs e)
    {
        if (sender is not Form form) return;
        IWinFormsEditorWindow? window;
        lock (_windows)
        {
            window = _windows.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Form, form));
        }
        if (window is not null) SetActiveWindow(window);
    }

    private async void OnFormClosedUnexpectedly(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form form) return;
        IWinFormsEditorWindow? window;
        lock (_windows)
        {
            window = _windows.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Form, form));
        }
        if (window is null) return;
        try
        {
            await RemoveAndDisposeAsync(window, capturePlacement: true).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications?.Error("Editor window cleanup failed", exception.Message, exception);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            if (!_dispatcher.IsDisposed && !_dispatcher.Disposing && _dispatcher.IsHandleCreated)
            {
                _dispatcher.BeginInvoke(new Action(() => OnThemeChanged(sender, e)));
            }
            return;
        }

        IWinFormsEditorWindow[] windows;
        lock (_windows)
        {
            windows = _windows.Values.ToArray();
        }
        foreach (IWinFormsEditorWindow window in windows)
        {
            if (!window.Form.IsDisposed) _theme.Apply(window.Form);
        }
    }

    private async ValueTask<T> RunOnUiThreadAsync<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            return await operation().ConfigureAwait(true);
        }
        if (_dispatcher.IsDisposed || _dispatcher.Disposing)
        {
            throw new ObjectDisposedException(nameof(WinFormsWindowService));
        }
        if (!_dispatcher.IsHandleCreated)
        {
            throw new InvalidOperationException(
                "The window service dispatcher has not created its WinForms handle yet.");
        }

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        _dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                T result = await operation().ConfigureAwait(true);
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }));
        return await completion.Task.ConfigureAwait(false);
    }

    private void ThrowIfDisposed(bool allowDisposing = false)
    {
        if (_disposed && !allowDisposing) throw new ObjectDisposedException(nameof(WinFormsWindowService));
    }
}
