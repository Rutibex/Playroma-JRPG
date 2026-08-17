using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>Compatibility name retained for older callers; Phase 5 uses EventCommandPaletteForm.</summary>
[Obsolete("Use EventCommandPaletteForm.")]
public sealed class EventCommandPaletteDialog : EventCommandPaletteForm
{
    public EventCommandPaletteDialog(EventCommandRegistry registry)
        : base(registry)
    {
    }
}
