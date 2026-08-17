namespace Playroma.Text.RpgMakerMV;

public static class MvMapTextTargets
{
    public static IReadOnlyList<MvTextFieldSpec> Specs { get; } =
    [
        new("editorName", "name", "Editor Name", "/name", false, false, 100,
            ["phase9.mv.map-text", "starter.names.city", "starter.fantasy.settlement-name"]),
        new("displayName", "displayName", "Display Name", "/displayName", false, false, 100,
            ["phase9.mv.map-text", "starter.names.city", "starter.fantasy.settlement-name"]),
        new("note", "note", "Map Note", "/note", true, true, null,
            ["phase9.mv.map-text", "phase6.semantic.item-lore", "phase5.markov.eldritch-note"])
    ];
}
