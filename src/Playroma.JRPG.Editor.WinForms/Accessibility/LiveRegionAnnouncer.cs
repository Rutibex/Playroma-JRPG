namespace Playroma.JRPG.Editor.WinForms.Accessibility;

/// <summary>
/// Small off-screen accessibility live region. It gives screen-reader users a text equivalent
/// for asynchronous validation, import, recovery, and background-task status changes.
/// </summary>
public sealed class LiveRegionAnnouncer : IDisposable
{
    private sealed class LiveRegionLabel : Label
    {
        public void AnnounceNameChange() =>
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
    }

    private readonly LiveRegionLabel _region = new()
    {
        AutoSize = true,
        Location = new Point(-32000, -32000),
        TabStop = false,
        AccessibleName = "Playroma status announcement",
        AccessibleRole = AccessibleRole.Alert
    };
    private Control? _owner;

    public Control Region => _region;

    public void Attach(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (ReferenceEquals(_owner, owner)) return;
        Detach();
        _owner = owner;
        owner.Controls.Add(_region);
        _region.BringToFront();
    }

    public void Announce(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || _region.IsDisposed) return;
        string announcement = message.Trim();
        _region.Text = announcement;
        _region.AccessibleName = announcement;
        _region.AnnounceNameChange();
    }

    public void Dispose()
    {
        Detach();
        _region.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Detach()
    {
        if (_owner is not null && !_owner.IsDisposed) _owner.Controls.Remove(_region);
        _owner = null;
    }
}
