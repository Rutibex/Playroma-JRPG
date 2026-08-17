using System.Runtime.CompilerServices;

namespace Playroma.JRPG.Editor.WinForms;

internal static class ControlHelpers
{
    public static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16, FontStyle.Bold),
        Margin = new Padding(8, 8, 8, 4)
    };

    public static Label Subheading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
        Margin = new Padding(4)
    };

    public static ListView CreateDetailsList(params (string Name, int Width)[] columns)
    {
        ListView list = new()
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false
        };
        foreach ((string name, int width) in columns) list.Columns.Add(name, width);
        return list;
    }

    public static DataGridView CreateReadOnlyGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None
        };
    }

    private sealed class SplitLayoutState
    {
        public SplitLayoutState(int requestedDistance, int panel1MinSize, int panel2MinSize)
        {
            RequestedDistance = requestedDistance;
            Panel1MinSize = panel1MinSize;
            Panel2MinSize = panel2MinSize;
        }

        public int RequestedDistance { get; set; }
        public int Panel1MinSize { get; }
        public int Panel2MinSize { get; }
        public bool Applying { get; set; }
    }

    private static readonly ConditionalWeakTable<SplitContainer, SplitLayoutState> SplitLayouts = new();

    public static SplitContainer CreateSplitContainer(
        Orientation orientation,
        int splitterDistance,
        int panel1MinSize,
        int panel2MinSize,
        int splitterWidth = 4,
        bool preserveCurrentDistance = false)
    {
        if (splitterDistance < 0) throw new ArgumentOutOfRangeException(nameof(splitterDistance));
        if (panel1MinSize < 0) throw new ArgumentOutOfRangeException(nameof(panel1MinSize));
        if (panel2MinSize < 0) throw new ArgumentOutOfRangeException(nameof(panel2MinSize));
        if (splitterWidth < 1) throw new ArgumentOutOfRangeException(nameof(splitterWidth));

        // A SplitContainer validates its splitter against its current size. Controls
        // created in field initializers still have the tiny WinForms default size, so
        // the real minimums and distance must be deferred until the control is laid out.
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Orientation = orientation,
            SplitterWidth = splitterWidth,
            BorderStyle = BorderStyle.None,
            Panel1MinSize = 0,
            Panel2MinSize = 0
        };

        SplitLayoutState state = new(splitterDistance, panel1MinSize, panel2MinSize);
        SplitLayouts.Add(split, state);

        void QueueApply()
        {
            if (split.IsDisposed || !split.IsHandleCreated) return;
            try
            {
                split.BeginInvoke(new Action(() => ApplySplitLayout(split, state)));
            }
            catch (InvalidOperationException) when (split.IsDisposed || !split.IsHandleCreated)
            {
                // The handle disappeared while the deferred layout was being queued.
            }
        }

        void OnHandleCreated(object? sender, EventArgs e) => QueueApply();
        void OnSizeChanged(object? sender, EventArgs e) => ApplySplitLayout(split, state);
        void OnSplitterMoved(object? sender, SplitterEventArgs e)
        {
            if (!state.Applying) state.RequestedDistance = split.SplitterDistance;
        }
        void OnDisposed(object? sender, EventArgs e)
        {
            split.HandleCreated -= OnHandleCreated;
            split.SizeChanged -= OnSizeChanged;
            split.SplitterMoved -= OnSplitterMoved;
            split.Disposed -= OnDisposed;
        }

        split.HandleCreated += OnHandleCreated;
        split.SizeChanged += OnSizeChanged;
        split.SplitterMoved += OnSplitterMoved;
        split.Disposed += OnDisposed;

        // Kept for source compatibility. Persisted distances should be supplied with
        // SetSplitterDistanceSafe so they are stored until the first real layout pass.
        _ = preserveCurrentDistance;
        return split;
    }

    public static void SetSplitterDistanceSafe(SplitContainer split, int requested)
    {
        ArgumentNullException.ThrowIfNull(split);
        requested = Math.Max(0, requested);

        if (SplitLayouts.TryGetValue(split, out SplitLayoutState state))
        {
            state.RequestedDistance = requested;
            ApplySplitLayout(split, state);
            return;
        }

        // Compatibility path for a SplitContainer created elsewhere.
        if (!split.IsHandleCreated)
        {
            void OnHandleCreated(object? sender, EventArgs e)
            {
                split.HandleCreated -= OnHandleCreated;
                if (split.IsDisposed) return;
                try
                {
                    split.BeginInvoke(new Action(() => SetSplitterDistanceSafe(split, requested)));
                }
                catch (InvalidOperationException) when (split.IsDisposed || !split.IsHandleCreated)
                {
                }
            }

            split.HandleCreated += OnHandleCreated;
            return;
        }

        ApplySplitLayout(
            split,
            new SplitLayoutState(requested, split.Panel1MinSize, split.Panel2MinSize));
    }

    private static void ApplySplitLayout(SplitContainer split, SplitLayoutState state)
    {
        if (state.Applying || split.IsDisposed || !split.IsHandleCreated) return;
        if (split.Panel1Collapsed || split.Panel2Collapsed) return;

        int extent = split.Orientation == Orientation.Vertical
            ? split.ClientSize.Width
            : split.ClientSize.Height;
        int available = extent - split.SplitterWidth;
        if (available <= 0) return;

        int panel1Min = state.Panel1MinSize;
        int panel2Min = state.Panel2MinSize;
        long minimumTotal = (long)panel1Min + panel2Min;
        if (minimumTotal > available && minimumTotal > 0)
        {
            double scale = available / (double)minimumTotal;
            panel1Min = (int)Math.Floor(state.Panel1MinSize * scale);
            panel2Min = available - panel1Min;
        }

        int maximumDistance = Math.Max(panel1Min, available - panel2Min);
        int safeDistance = Math.Clamp(state.RequestedDistance, panel1Min, maximumDistance);

        state.Applying = true;
        try
        {
            // Relax the old limits before changing the distance. This is also safe
            // when a user shrinks a window below the preferred minimum total.
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = Math.Clamp(safeDistance, 0, available);
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
        }
        finally
        {
            state.Applying = false;
        }
    }

    public static int ClampSplitterDistance(SplitContainer split, int requested)
    {
        ArgumentNullException.ThrowIfNull(split);
        int extent = split.Orientation == Orientation.Vertical
            ? split.ClientSize.Width
            : split.ClientSize.Height;
        int available = Math.Max(0, extent - split.SplitterWidth);
        int minimum = split.Panel1MinSize;
        int maximum = Math.Max(minimum, available - split.Panel2MinSize);
        return Math.Clamp(requested, minimum, maximum);
    }

    public static bool BeginInvokeIfRequired(Control control, Action action)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(action);

        if (control.IsDisposed || control.Disposing) return true;
        if (!control.InvokeRequired) return false;

        try
        {
            control.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (control.IsDisposed || control.Disposing || !control.IsHandleCreated)
        {
            // The owner is shutting down or its handle disappeared between checks.
        }

        return true;
    }

    /// <summary>
    /// Posts an action to the owning control's message queue even when already on the UI thread.
    /// This is useful for actions launched from ToolStrip drop-down callbacks, because opening a
    /// modal editor before the drop-down has completed its close sequence can re-enter WinForms
    /// while its native popup is still being torn down.
    /// </summary>
    public static void Post(Control dispatcher, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);

        if (dispatcher.IsDisposed || dispatcher.Disposing || !dispatcher.IsHandleCreated) return;
        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (dispatcher.IsDisposed || dispatcher.Disposing || !dispatcher.IsHandleCreated)
        {
            // The dispatcher was destroyed between the state check and BeginInvoke.
        }
    }

    /// <summary>
    /// Disposes a temporary ContextMenuStrip only after WinForms has finished processing its
    /// Closed event. Disposing directly from Closed can make the remaining ToolStrip close logic
    /// access an already-disposed native popup and throw ObjectDisposedException.
    /// </summary>
    public static void DisposeTransientContextMenuAfterClose(
        ContextMenuStrip menu,
        Control dispatcher)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(dispatcher);

        ToolStripDropDownClosedEventHandler? closed = null;
        closed = (_, _) =>
        {
            if (closed is not null) menu.Closed -= closed;
            Post(dispatcher, () =>
            {
                if (!menu.IsDisposed) menu.Dispose();
            });
        };
        menu.Closed += closed;
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1) { value /= 1024; suffix++; }
        return $"{value:0.##} {suffixes[suffix]}";
    }
}
