using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Dialogue;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Generators.Dialogue;

namespace Playroma.Text.Editor.WinForms.Controls;

/// <summary>Friendly per-generation dialogue controls; hides implementation details such as the Phase 10 voice stage.</summary>
public sealed class DialogueVoiceContextControl : UserControl
{
    private readonly string _projectRoot;
    private readonly ComboBox _profile = Combo();
    private readonly TextBox _speaker = new() { Dock = DockStyle.Top, PlaceholderText = "Speaker name (optional)" };
    private readonly TextBox _listener = new() { Dock = DockStyle.Top, PlaceholderText = "Listener name (optional)" };
    private readonly TextBox _state = new() { Dock = DockStyle.Top, Multiline = true, Height = 62, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Conversation state, one key=value per line" };
    private readonly ComboBox _act = Combo();
    private readonly ComboBox _emotion = Combo();
    private readonly TrackBar _affinity = Slider();
    private readonly TrackBar _trust = Slider();
    private readonly Label _affinityValue = ValueLabel();
    private readonly Label _trustValue = ValueLabel();
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(4), AutoEllipsis = true };
    private readonly List<(DialogueVoiceProfile Profile, bool BuiltIn)> _profiles = [];
    private TextStudioSession? _session;
    private bool _binding;

    public DialogueVoiceContextControl(string projectRoot)
    {
        _projectRoot = projectRoot;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(panel, "Voice", _profile); AddRow(panel, "Speaker", _speaker); AddRow(panel, "Listener", _listener); AddRow(panel, "Intent", _act); AddRow(panel, "Emotion", _emotion); AddRow(panel, "State", _state);
        AddSliderRow(panel, "Affinity", _affinity, _affinityValue); AddSliderRow(panel, "Trust", _trust, _trustValue);
        Controls.Add(panel); Controls.Add(_summary);
        _act.Items.Add("(Recipe default)"); _act.Items.AddRange(Enum.GetNames<DialogueAct>());
        _emotion.Items.Add("(Profile default)"); _emotion.Items.AddRange(Enum.GetNames<DialogueEmotion>());
        _act.SelectedIndex = 0; _emotion.SelectedIndex = 0;
        _profile.SelectedIndexChanged += (_, _) => Changed(); _speaker.TextChanged += (_, _) => Changed(); _listener.TextChanged += (_, _) => Changed(); _state.TextChanged += (_, _) => Changed();
        _act.SelectedIndexChanged += (_, _) => Changed(); _emotion.SelectedIndexChanged += (_, _) => Changed();
        _affinity.ValueChanged += (_, _) => { _affinityValue.Text = Format(_affinity.Value); Changed(); };
        _trust.ValueChanged += (_, _) => { _trustValue.Text = Format(_trust.Value); Changed(); };
    }

    public async Task InitializeAsync(TextStudioSession session, CancellationToken cancellationToken = default)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
        SetContext(session.DialogueContext);
    }

    public async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        string? selected = SelectedProfileId;
        _profiles.Clear();
        _profiles.AddRange(BuiltInDialogueVoiceProfiles.All.Select(x => (x, true)));
        var layout = new TextProjectLayout(_projectRoot); layout.EnsureCreated();
        var store = new JsonDialogueVoiceProfileStore(layout.Voices);
        var project = await store.LoadAllAsync(cancellationToken).ConfigureAwait(true);
        foreach (var item in project)
        {
            int existing = _profiles.FindIndex(x => x.Profile.Id.Equals(item.Profile.Id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) _profiles[existing] = (item.Profile, false); else _profiles.Add((item.Profile, false));
        }
        _binding = true;
        try
        {
            _profile.Items.Clear();
            foreach (var item in _profiles.OrderBy(x=>x.Profile.Name,StringComparer.OrdinalIgnoreCase)) _profile.Items.Add(new ProfileItem(item.Profile, item.BuiltIn));
            int index = _profile.Items.Cast<ProfileItem>().ToList().FindIndex(x => x.Profile.Id.Equals(selected ?? "common", StringComparison.OrdinalIgnoreCase));
            _profile.SelectedIndex = index >= 0 ? index : (_profile.Items.Count > 0 ? 0 : -1);
        }
        finally { _binding = false; }
        Changed();
    }

    public void SetContext(DialogueVoiceContext? context)
    {
        _binding = true;
        try
        {
            string id = context?.ProfileId ?? "common";
            int index = _profile.Items.Cast<ProfileItem>().ToList().FindIndex(x=>x.Profile.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _profile.SelectedIndex = index;
            _speaker.Text = context?.SpeakerName ?? string.Empty; _listener.Text = context?.ListenerName ?? string.Empty;
            _state.Text = context is null ? string.Empty : string.Join(Environment.NewLine, context.EffectiveState.Select(x => $"{x.Key}={x.Value}"));
            _act.SelectedItem = context?.Act?.ToString() ?? "(Recipe default)";
            _emotion.SelectedItem = context?.Emotion?.ToString() ?? "(Profile default)";
            _affinity.Value = Clamp100((context?.RelationshipAffinity ?? 0) * 100); _trust.Value = Clamp100((context?.RelationshipTrust ?? 0) * 100);
            _affinityValue.Text = Format(_affinity.Value); _trustValue.Text = Format(_trust.Value);
        }
        finally { _binding = false; }
        Changed();
    }

    public DialogueVoiceContext CaptureContext() => new(
        SelectedProfileId,
        string.IsNullOrWhiteSpace(_speaker.Text) ? null : _speaker.Text.Trim(),
        Enum.TryParse<DialogueAct>(_act.SelectedItem?.ToString(), true, out var act) ? act : null,
        Enum.TryParse<DialogueEmotion>(_emotion.SelectedItem?.ToString(), true, out var emotion) ? emotion : null,
        _affinity.Value / 100d,
        _trust.Value / 100d,
        string.IsNullOrWhiteSpace(_listener.Text) ? null : _listener.Text.Trim(),
        ParseState(_state.Text));

    public string? SelectedProfileId => (_profile.SelectedItem as ProfileItem)?.Profile.Id;

    private void Changed()
    {
        if (_binding) return;
        DialogueVoiceContext context = CaptureContext();
        _session?.ApplyDialogueContext(context);
        DialogueVoiceProfile? profile = (_profile.SelectedItem as ProfileItem)?.Profile;
        _summary.Text = profile is null ? "No voice selected." : $"{profile.Name} — {profile.Description}";
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0,7,0,0) },0,row); control.Dock = DockStyle.Top; table.Controls.Add(control,1,row);
    }
    private static void AddSliderRow(TableLayoutPanel table,string label,TrackBar slider,Label value)
    {
        var host = new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2 }; host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,50)); host.Controls.Add(slider,0,0); host.Controls.Add(value,1,0); AddRow(table,label,host);
    }
    private static ComboBox Combo() => new() { Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList };
    private static TrackBar Slider() => new() { Minimum=-100,Maximum=100,TickFrequency=25,SmallChange=5,LargeChange=10,Dock=DockStyle.Top };
    private static Label ValueLabel() => new() { Text="0.00",AutoSize=true,Padding=new Padding(4,7,0,0) };
    private static string Format(int value) => (value/100d).ToString("+0.00;-0.00;0.00",System.Globalization.CultureInfo.InvariantCulture);
    private static int Clamp100(double value) => Math.Clamp((int)Math.Round(value),-100,100);
    private static IReadOnlyDictionary<string, object?> ParseState(string text)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int index = line.IndexOf('='); if (index <= 0) continue;
            string key = line[..index].Trim(); string value = line[(index + 1)..].Trim();
            if (bool.TryParse(value, out bool b)) result[key] = b;
            else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) result[key] = d;
            else result[key] = value;
        }
        return result;
    }

    private sealed record ProfileItem(DialogueVoiceProfile Profile,bool BuiltIn)
    {
        public override string ToString() => Profile.Name + (BuiltIn ? "  • built-in" : "  • project");
    }
}
