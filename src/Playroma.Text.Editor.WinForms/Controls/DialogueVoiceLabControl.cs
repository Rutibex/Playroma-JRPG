using System.Text.Json;
using Playroma.Text.Authoring;
using Playroma.Text.Authoring.Dialogue;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Dialogue;

namespace Playroma.Text.Editor.WinForms.Controls;

/// <summary>Authoring surface for reusable character voices. Built-ins are read-only but can be cloned into the project.</summary>
public sealed class DialogueVoiceLabControl : UserControl
{
    private readonly TextProjectLayout _layout;
    private readonly JsonDialogueVoiceProfileStore _store;
    private readonly ListBox _profiles = new() { Dock=DockStyle.Fill,IntegralHeight=false };
    private readonly TextBox _id = Box(); private readonly TextBox _name = Box(); private readonly TextBox _description = Box(multiline:true, height:58);
    private readonly NumericUpDown _formality = Scale(); private readonly NumericUpDown _verbosity = Scale(); private readonly NumericUpDown _warmth = Scale();
    private readonly NumericUpDown _aggression = Scale(); private readonly NumericUpDown _humor = Scale(); private readonly NumericUpDown _archaic = Scale();
    private readonly NumericUpDown _contractions = Scale(); private readonly NumericUpDown _fillersPreference = Scale(); private readonly NumericUpDown _catchphrasePreference = Scale();
    private readonly NumericUpDown _exclamation = Scale(); private readonly NumericUpDown _ellipsis = Scale();
    private readonly ComboBox _defaultEmotion = new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox _vocabulary = BigBox(); private readonly TextBox _dialect = BigBox(); private readonly TextBox _catchphrases = BigBox(); private readonly TextBox _fillers = BigBox(); private readonly TextBox _taboo = BigBox();
    private readonly TextBox _actOpeners = BigBox(); private readonly TextBox _actClosers = BigBox(); private readonly TextBox _emotionOpeners = BigBox(); private readonly TextBox _emotionClosers = BigBox();
    private readonly TextBox _previewInput = new(){Dock=DockStyle.Top,Multiline=true,Height=100,ScrollBars=ScrollBars.Vertical,Text="I do not know why you want to go there, but the old road is dangerous after dark."};
    private readonly TextBox _previewOutput = new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
    private readonly ComboBox _previewAct = new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150};
    private readonly ComboBox _previewEmotion = new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150};
    private readonly Button _preview = new(){Text="Speak This Way",AutoSize=true};
    private readonly Button _new = new(){Text="New",AutoSize=true}; private readonly Button _clone = new(){Text="Clone",AutoSize=true}; private readonly Button _save = new(){Text="Save",AutoSize=true}; private readonly Button _revert = new(){Text="Revert",AutoSize=true};
    private readonly Label _status = new(){Dock=DockStyle.Bottom,Height=25,Padding=new Padding(6,4,0,0),AutoEllipsis=true};
    private readonly List<VoiceEntry> _entries=[];
    private VoiceEntry? _selected;
    private DialogueVoiceProfile? _baseline;
    private string? _existingPath;
    private bool _loading;
    private bool _dirty;

    public DialogueVoiceLabControl(string projectRoot)
    {
        _layout=new TextProjectLayout(projectRoot); _layout.EnsureCreated(); _store=new JsonDialogueVoiceProfileStore(_layout.Voices);
        Dock=DockStyle.Fill; MinimumSize=new Size(700,500);
        _defaultEmotion.Items.AddRange(Enum.GetNames<DialogueEmotion>()); _defaultEmotion.SelectedItem=DialogueEmotion.Neutral.ToString();
        _previewAct.Items.AddRange(Enum.GetNames<DialogueAct>()); _previewAct.SelectedItem=DialogueAct.SmallTalk.ToString();
        _previewEmotion.Items.AddRange(Enum.GetNames<DialogueEmotion>()); _previewEmotion.SelectedItem=DialogueEmotion.Neutral.ToString();

        var left=new Panel{Dock=DockStyle.Fill,Padding=new Padding(6)}; var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=false}; toolbar.Controls.AddRange([_new,_clone,_save,_revert]); left.Controls.Add(_profiles); left.Controls.Add(toolbar);
        var tabs=new TabControl{Dock=DockStyle.Fill}; tabs.TabPages.Add(BuildStyleTab()); tabs.TabPages.Add(BuildLanguageTab()); tabs.TabPages.Add(BuildPreviewTab());
        var split=new SplitContainer{Dock=DockStyle.Fill,SplitterDistance=245,FixedPanel=FixedPanel.Panel1}; split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(tabs); Controls.Add(split); Controls.Add(_status);

        _profiles.SelectedIndexChanged += (_,_)=>SelectEntry(_profiles.SelectedItem as VoiceEntry);
        _new.Click += (_,_)=>NewProfile(); _clone.Click += (_,_)=>CloneProfile(); _save.Click += async (_,_)=>await SaveCurrentAsync().ConfigureAwait(true); _revert.Click += (_,_)=>Revert(); _preview.Click += (_,_)=>UpdatePreview();
        WireDirty(this);
    }

    public bool HasUnsavedChanges => _dirty;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? ProfilesChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken=default) => await ReloadFromDiskAsync(false,cancellationToken).ConfigureAwait(true);

    public async Task ReloadFromDiskAsync(bool discardUnsavedChanges=false,CancellationToken cancellationToken=default)
    {
        if (_dirty && !discardUnsavedChanges) throw new InvalidOperationException("Voice Lab contains unsaved changes.");
        string? selectedId=_selected?.Profile.Id; _entries.Clear();
        _entries.AddRange(BuiltInDialogueVoiceProfiles.All.Select(p=>new VoiceEntry(p,true,null)));
        foreach(var item in await _store.LoadAllAsync(cancellationToken).ConfigureAwait(true))
        {
            int existing=_entries.FindIndex(x=>x.Profile.Id.Equals(item.Profile.Id,StringComparison.OrdinalIgnoreCase));
            var entry=new VoiceEntry(item.Profile,false,item.Path); if(existing>=0)_entries[existing]=entry;else _entries.Add(entry);
        }
        _profiles.BeginUpdate(); try{_profiles.Items.Clear();foreach(var e in _entries.OrderBy(x=>x.Profile.Name,StringComparer.OrdinalIgnoreCase))_profiles.Items.Add(e);}finally{_profiles.EndUpdate();}
        VoiceEntry? select=_entries.LastOrDefault(x=>x.Profile.Id.Equals(selectedId??"common",StringComparison.OrdinalIgnoreCase))??_entries.FirstOrDefault();
        if(select is not null)_profiles.SelectedItem=_profiles.Items.Cast<VoiceEntry>().FirstOrDefault(x=>x.Profile.Id.Equals(select.Profile.Id,StringComparison.OrdinalIgnoreCase));
        _dirty=false; DirtyStateChanged?.Invoke(this,EventArgs.Empty); SetStatus($"Loaded {_entries.Count} voice profiles.");
    }

    public async Task SaveCurrentAsync(CancellationToken cancellationToken=default)
    {
        DialogueVoiceProfile profile=Capture();
        var errors=DialogueVoiceProfileValidator.Validate(profile);
        if(errors.Count!=0)
        {
            MessageBox.Show(this,"Please correct the voice profile before saving:\r\n\r\n• "+string.Join("\r\n• ",errors),"Voice profile",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            SetStatus("Voice profile validation failed.");
            return;
        }
        try
        {
            string path=await _store.SaveAsync(profile,_existingPath,cancellationToken).ConfigureAwait(true);
            _baseline=profile;_existingPath=path;_selected=new VoiceEntry(profile,false,path);_dirty=false;
            await ReloadFromDiskAsync(true,cancellationToken).ConfigureAwait(true);
            ProfilesChanged?.Invoke(this,EventArgs.Empty); SetStatus("Saved "+Path.GetFileName(path));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this,"Playroma could not save the voice profile:\r\n\r\n"+ex.Message,"Voice profile",MessageBoxButtons.OK,MessageBoxIcon.Error);
            SetStatus("Voice save failed.");
        }
    }

    public void DiscardUnsavedChanges()=>Revert();

    public DialogueVoiceLabRecoveryState? CaptureRecoveryState()=>!_dirty?null:new(
        JsonSerializer.Serialize(Capture(),RecipeJson.Options),
        _existingPath,
        _baseline is null ? null : JsonSerializer.Serialize(_baseline,RecipeJson.Options));
    public void RestoreRecoveryState(DialogueVoiceLabRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state); var profile=JsonSerializer.Deserialize<DialogueVoiceProfile>(state.ProfileJson,RecipeJson.Options); if(profile is null)return;
        DialogueVoiceProfile? baseline = string.IsNullOrWhiteSpace(state.BaselineProfileJson) ? null : JsonSerializer.Deserialize<DialogueVoiceProfile>(state.BaselineProfileJson,RecipeJson.Options);
        _selected=new VoiceEntry(profile,false,state.Path);_baseline=baseline??profile;_existingPath=state.Path;Bind(profile,false);MarkDirty();SetStatus("Recovered unsaved voice profile.");
    }

    private TabPage BuildStyleTab()
    {
        var page=new TabPage("Identity & Style");var table=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(10)};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Row(table,"ID",_id);Row(table,"Name",_name);Row(table,"Description",_description);Row(table,"Default emotion",_defaultEmotion);
        Row(table,"Formality",_formality);Row(table,"Verbosity",_verbosity);Row(table,"Warmth",_warmth);Row(table,"Aggression",_aggression);Row(table,"Humor",_humor);Row(table,"Archaic",_archaic);Row(table,"Contractions",_contractions);Row(table,"Filler chance",_fillersPreference);Row(table,"Catchphrase chance",_catchphrasePreference);Row(table,"Exclamation chance",_exclamation);Row(table,"Ellipsis chance",_ellipsis);
        page.Controls.Add(new Panel{Dock=DockStyle.Fill,AutoScroll=true});page.Controls[0].Controls.Add(table);return page;
    }
    private TabPage BuildLanguageTab()
    {
        var page=new TabPage("Language");var tabs=new TabControl{Dock=DockStyle.Fill};tabs.TabPages.Add(TextPage("Vocabulary","One replacement per line: word=replacement",_vocabulary));tabs.TabPages.Add(TextPage("Dialect","One literal rewrite per line: from=>to",_dialect));tabs.TabPages.Add(TextPage("Catchphrases","One phrase per line.",_catchphrases));tabs.TabPages.Add(TextPage("Fillers","One short filler/opening per line.",_fillers));tabs.TabPages.Add(TextPage("Taboo","Words this voice should avoid; one per line.",_taboo));tabs.TabPages.Add(TextPage("Act Openers","Intent=phrase one|phrase two",_actOpeners));tabs.TabPages.Add(TextPage("Act Closers","Intent=phrase one|phrase two",_actClosers));tabs.TabPages.Add(TextPage("Emotion Openers","Emotion=phrase one|phrase two",_emotionOpeners));tabs.TabPages.Add(TextPage("Emotion Closers","Emotion=phrase one|phrase two",_emotionClosers));page.Controls.Add(tabs);return page;
    }
    private TabPage BuildPreviewTab()
    {
        var page=new TabPage("Preview");var top=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(6)};top.Controls.AddRange([new Label{Text="Act",AutoSize=true,Padding=new Padding(0,7,0,0)},_previewAct,new Label{Text="Emotion",AutoSize=true,Padding=new Padding(8,7,0,0)},_previewEmotion,_preview]);var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=130};split.Panel1.Controls.Add(_previewInput);split.Panel2.Controls.Add(_previewOutput);page.Controls.Add(split);page.Controls.Add(top);return page;
    }
    private static TabPage TextPage(string title,string hint,TextBox box){var p=new TabPage(title);p.Controls.Add(box);p.Controls.Add(new Label{Text=hint,Dock=DockStyle.Top,Height=32,Padding=new Padding(6),AutoEllipsis=true});return p;}

    private void NewProfile(){if(!CanAbandon())return;var p=new DialogueVoiceProfile("my-character",1,"My Character Voice","Project-authored character voice.");_selected=new VoiceEntry(p,false,null);_baseline=p;_existingPath=null;Bind(p,false);MarkDirty();}
    private void CloneProfile(){if(_selected is null)return;if(!CanAbandon())return;var source=Capture();var p=source with{Id=source.Id+"-custom",Name=source.Name+" (Custom)",Version=1};_selected=new VoiceEntry(p,false,null);_baseline=p;_existingPath=null;Bind(p,false);MarkDirty();}
    private bool CanAbandon(){if(!_dirty)return true;return MessageBox.Show(this,"Discard the unsaved voice changes?","Voice Lab",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;}
    private void SelectEntry(VoiceEntry? entry){if(_loading||entry is null)return;if(_dirty&&!ReferenceEquals(entry,_selected)&&!CanAbandon()){_loading=true;_profiles.SelectedItem=_selected;_loading=false;return;}_selected=entry;_baseline=entry.Profile;_existingPath=entry.Path;Bind(entry.Profile,entry.BuiltIn);}
    private void Bind(DialogueVoiceProfile p,bool builtIn){_loading=true;try{_id.Text=p.Id;_name.Text=p.Name;_description.Text=p.Description??"";_formality.Value=D(p.Formality);_verbosity.Value=D(p.Verbosity);_warmth.Value=D(p.Warmth);_aggression.Value=D(p.Aggression);_humor.Value=D(p.Humor);_archaic.Value=D(p.Archaic);_contractions.Value=D(p.ContractionPreference);_fillersPreference.Value=D(p.FillerPreference);_catchphrasePreference.Value=D(p.CatchphrasePreference);_exclamation.Value=D(p.ExclamationPreference);_ellipsis.Value=D(p.EllipsisPreference);_defaultEmotion.SelectedItem=p.DefaultEmotion.ToString();_vocabulary.Text=string.Join(Environment.NewLine,p.EffectiveVocabulary.Select(x=>$"{x.Key}={x.Value}"));_dialect.Text=string.Join(Environment.NewLine,p.EffectiveDialectRules.Select(x=>$"{x.From}=>{x.To}"));_catchphrases.Text=string.Join(Environment.NewLine,p.EffectiveCatchphrases);_fillers.Text=string.Join(Environment.NewLine,p.EffectiveFillers);_taboo.Text=string.Join(Environment.NewLine,p.EffectiveTabooWords);_actOpeners.Text=FormatPhraseMap(p.EffectiveActOpeners);_actClosers.Text=FormatPhraseMap(p.EffectiveActClosers);_emotionOpeners.Text=FormatPhraseMap(p.EffectiveEmotionOpeners);_emotionClosers.Text=FormatPhraseMap(p.EffectiveEmotionClosers);_id.ReadOnly=builtIn;_save.Enabled=!builtIn;_revert.Enabled=!builtIn;_dirty=false;}finally{_loading=false;}DirtyStateChanged?.Invoke(this,EventArgs.Empty);UpdatePreview();SetStatus(builtIn?"Built-in profile. Clone it to customize.":"Project voice profile.");}
    private DialogueVoiceProfile Capture()=>new(_id.Text.Trim(),1,_name.Text.Trim(),string.IsNullOrWhiteSpace(_description.Text)?null:_description.Text.Trim(),V(_formality),V(_verbosity),V(_warmth),V(_aggression),V(_humor),V(_archaic),V(_contractions),V(_fillersPreference),V(_catchphrasePreference),V(_exclamation),V(_ellipsis),Enum.TryParse<DialogueEmotion>(_defaultEmotion.SelectedItem?.ToString(),true,out var e)?e:DialogueEmotion.Neutral,ParseMap(_vocabulary.Text),ParseDialect(_dialect.Text),Lines(_catchphrases.Text),Lines(_fillers.Text),Lines(_taboo.Text),ParsePhraseMap(_actOpeners.Text),ParsePhraseMap(_actClosers.Text),ParsePhraseMap(_emotionOpeners.Text),ParsePhraseMap(_emotionClosers.Text));
    private void Revert(){if(_baseline is null)return;Bind(_baseline,_selected?.BuiltIn==true);}
    private void MarkDirty(){if(_loading||_selected?.BuiltIn==true)return;if(!_dirty){_dirty=true;DirtyStateChanged?.Invoke(this,EventArgs.Empty);}SetStatus("Unsaved voice changes.");}
    private void UpdatePreview(){try{var profile=Capture();var act=Enum.TryParse<DialogueAct>(_previewAct.SelectedItem?.ToString(),true,out var a)?a:DialogueAct.SmallTalk;var emotion=Enum.TryParse<DialogueEmotion>(_previewEmotion.SelectedItem?.ToString(),true,out var e)?e:profile.DefaultEmotion;var engine=new DialogueVoiceEngine();_previewOutput.Text=engine.Realize(_previewInput.Text,profile,new DialogueVoiceRealizationOptions(act,emotion),new Xoshiro256StarStar(123456789UL));}catch(Exception ex){_previewOutput.Text="Preview error: "+ex.Message;}}
    private void WireDirty(Control root){foreach(Control c in root.Controls){if(c is TextBox tb&&tb!=_previewInput&&tb!=_previewOutput)tb.TextChanged+=(_,_)=>MarkDirty();else if(c is NumericUpDown n)n.ValueChanged+=(_,_)=>MarkDirty();else if(c is ComboBox cb&&cb!=_previewAct&&cb!=_previewEmotion)cb.SelectedIndexChanged+=(_,_)=>MarkDirty();WireDirty(c);}}
    private void SetStatus(string text)=>_status.Text=text;
    private static TextBox Box(bool multiline=false,int height=26)=>new(){Dock=DockStyle.Top,Multiline=multiline,Height=height,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None};private static TextBox BigBox()=>new(){Dock=DockStyle.Fill,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font(FontFamily.GenericMonospace,9)};
    private static NumericUpDown Scale()=>new(){Dock=DockStyle.Top,Minimum=0,Maximum=1,DecimalPlaces=2,Increment=.05M};private static decimal D(double v)=>Math.Clamp((decimal)v,0,1);private static double V(NumericUpDown n)=>(double)n.Value;
    private static void Row(TableLayoutPanel t,string label,Control c){int r=t.RowCount++;t.RowStyles.Add(new RowStyle(SizeType.AutoSize));t.Controls.Add(new Label{Text=label,AutoSize=true,Padding=new Padding(0,7,0,0)},0,r);c.Dock=DockStyle.Top;t.Controls.Add(c,1,r);}
    private static IReadOnlyDictionary<string,string> ParseMap(string text){var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(string line in Lines(text)){int i=line.IndexOf('=');if(i>0)d[line[..i].Trim()]=line[(i+1)..].Trim();}return d;}
    private static IReadOnlyList<DialogueRewriteRule> ParseDialect(string text){var l=new List<DialogueRewriteRule>();foreach(string line in Lines(text)){int i=line.IndexOf("=>",StringComparison.Ordinal);if(i>0)l.Add(new(line[..i].Trim(),line[(i+2)..].Trim()));}return l;}
    private static IReadOnlyDictionary<string,IReadOnlyList<string>> ParsePhraseMap(string text){var d=new Dictionary<string,IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);foreach(string line in Lines(text)){int i=line.IndexOf('=');if(i<=0)continue;string[] values=line[(i+1)..].Split('|',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);if(values.Length>0)d[line[..i].Trim()]=values;}return d;}
    private static string FormatPhraseMap(IReadOnlyDictionary<string,IReadOnlyList<string>> map)=>string.Join(Environment.NewLine,map.Select(x=>$"{x.Key}={string.Join("|",x.Value)}"));
    private static string[] Lines(string text)=>text.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(x=>x.Length>0).ToArray();
    private sealed record VoiceEntry(DialogueVoiceProfile Profile,bool BuiltIn,string? Path){public override string ToString()=>Profile.Name+(BuiltIn?"  • built-in":"  • project");}
}
