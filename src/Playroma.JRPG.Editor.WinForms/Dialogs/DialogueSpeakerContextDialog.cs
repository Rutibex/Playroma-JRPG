using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Dialogue;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Generators.Dialogue;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

internal sealed class DialogueSpeakerContextDialog : Form
{
    private readonly TextProjectLayout _layout;
    private readonly string _targetKey;
    private readonly ComboBox _profile=new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox _speaker=new(){Dock=DockStyle.Top}; private readonly TextBox _listener=new(){Dock=DockStyle.Top};
    private readonly ComboBox _act=new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList}; private readonly ComboBox _emotion=new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TrackBar _affinity=Slider(); private readonly TrackBar _trust=Slider();
    private readonly CheckBox _remember=new(){Text="Remember this voice for this event/character",AutoSize=true};
    private readonly Button _ok=new(){Text="Use Voice",DialogResult=DialogResult.OK,AutoSize=true}; private readonly Button _cancel=new(){Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};
    private readonly List<DialogueVoiceProfile> _profiles=[];

    public DialogueSpeakerContextDialog(string projectRoot,string targetKey,string? defaultSpeaker,DialogueAct defaultAct)
    {
        _layout=new TextProjectLayout(projectRoot);_layout.EnsureCreated();_targetKey=targetKey;
        Text="Character Voice";StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(540,430);
        _act.Items.AddRange(Enum.GetNames<DialogueAct>());_emotion.Items.AddRange(Enum.GetNames<DialogueEmotion>());_act.SelectedItem=defaultAct.ToString();_emotion.SelectedItem=DialogueEmotion.Neutral.ToString();_speaker.Text=defaultSpeaker??string.Empty;
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(12)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Row(table,"Speaker",_speaker);Row(table,"Listener",_listener);Row(table,"Voice profile",_profile);Row(table,"Dialogue intent",_act);Row(table,"Emotion",_emotion);Row(table,"Relationship",_affinity);Row(table,"Trust",_trust);
        int r=table.RowCount++;table.Controls.Add(_remember,1,r);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,FlowDirection=FlowDirection.RightToLeft,AutoSize=true,Padding=new Padding(8)};buttons.Controls.AddRange([_cancel,_ok]);Controls.Add(table);Controls.Add(buttons);AcceptButton=_ok;CancelButton=_cancel;
        Load += async (_,_)=>await LoadProfilesAsync().ConfigureAwait(true);
        FormClosing += OnClosing;
    }

    public DialogueVoiceContext? Context { get; private set; }

    private async Task LoadProfilesAsync()
    {
        _profiles.Clear();_profiles.AddRange(BuiltInDialogueVoiceProfiles.All);
        var store=new JsonDialogueVoiceProfileStore(_layout.Voices);
        foreach(var item in await store.LoadAllAsync().ConfigureAwait(true))
        {
            int i=_profiles.FindIndex(x=>x.Id.Equals(item.Profile.Id,StringComparison.OrdinalIgnoreCase));if(i>=0)_profiles[i]=item.Profile;else _profiles.Add(item.Profile);
        }
        _profile.Items.Clear();foreach(var p in _profiles.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase))_profile.Items.Add(new VoiceItem(p));
        var assignments=new DialogueVoiceAssignmentStore(_layout.VoiceAssignments);var assignment=await assignments.FindAsync(_targetKey).ConfigureAwait(true);
        string id=assignment?.ProfileId??"common";int idx=_profile.Items.Cast<VoiceItem>().ToList().FindIndex(x=>x.Profile.Id.Equals(id,StringComparison.OrdinalIgnoreCase));_profile.SelectedIndex=idx>=0?idx:0;
        if(!string.IsNullOrWhiteSpace(assignment?.SpeakerName))_speaker.Text=assignment!.SpeakerName!;
    }

    private void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(DialogResult!=DialogResult.OK)return;
        string? profileId=(_profile.SelectedItem as VoiceItem)?.Profile.Id;if(string.IsNullOrWhiteSpace(profileId)){e.Cancel=true;return;}
        var act=Enum.TryParse<DialogueAct>(_act.SelectedItem?.ToString(),true,out var a)?a:DialogueAct.SmallTalk;var emotion=Enum.TryParse<DialogueEmotion>(_emotion.SelectedItem?.ToString(),true,out var em)?em:DialogueEmotion.Neutral;
        Context=new(profileId,string.IsNullOrWhiteSpace(_speaker.Text)?null:_speaker.Text.Trim(),act,emotion,_affinity.Value/100d,_trust.Value/100d,string.IsNullOrWhiteSpace(_listener.Text)?null:_listener.Text.Trim());
        if(_remember.Checked)
        {
            try{new DialogueVoiceAssignmentStore(_layout.VoiceAssignments).UpsertAsync(new DialogueVoiceAssignment(_targetKey,profileId,Context.SpeakerName)).GetAwaiter().GetResult();}catch(Exception ex){MessageBox.Show(this,"The voice will be used now, but Playroma could not remember the assignment:\r\n\r\n"+ex.Message,"Voice assignment",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
    }

    private static TrackBar Slider()=>new(){Minimum=-100,Maximum=100,TickFrequency=25,SmallChange=5,LargeChange=10,Dock=DockStyle.Top};
    private static void Row(TableLayoutPanel t,string label,Control c){int r=t.RowCount++;t.RowStyles.Add(new RowStyle(SizeType.AutoSize));t.Controls.Add(new Label{Text=label,AutoSize=true,Padding=new Padding(0,7,0,0)},0,r);c.Dock=DockStyle.Top;t.Controls.Add(c,1,r);}
    private sealed record VoiceItem(DialogueVoiceProfile Profile){public override string ToString()=>Profile.Name;}
}
