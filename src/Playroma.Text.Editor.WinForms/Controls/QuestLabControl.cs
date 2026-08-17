using System.Globalization;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Core.Planning;
using Playroma.Text.Generators.Planning;
using Playroma.Text.RpgMakerMV.Planning;

namespace Playroma.Text.Editor.WinForms.Controls;

/// <summary>Mixed-initiative authoring surface for quest grammars, planner state, plan inspection, realization and MV export preview.</summary>
public sealed class QuestLabControl : UserControl
{
    private readonly TextStudioSession _session;
    private readonly ComboBox _quests = new(){Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox _id=new(){Dock=DockStyle.Top}, _name=new(){Dock=DockStyle.Top}, _root=new(){Dock=DockStyle.Top}, _goals=new(){Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly TextBox _description=new(){Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly DataGridView _bindings=Grid(), _actions=Grid(), _methods=Grid(), _state=Grid();
    private readonly TextBox _stateTags=new(){Dock=DockStyle.Top,PlaceholderText="Initial tags, comma separated"};
    private readonly TextBox _preview=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Font=new Font("Consolas",9)};
    private readonly ToolStripLabel _status=new("Ready");
    private QuestGrammarDefinition? _working,_baseline; private string? _path; private bool _dirty,_loading; private QuestPlan? _lastPlan; private QuestRealizationResult? _lastRealization;

    public QuestLabControl(TextStudioSession session)
    {
        _session=session ?? throw new ArgumentNullException(nameof(session)); Dock=DockStyle.Fill; MinimumSize=new Size(700,500);
        ConfigureGrids();
        var toolbar=new ToolStrip{GripStyle=ToolStripGripStyle.Hidden,Dock=DockStyle.Top};
        ToolStripButton add=new("New"), clone=new("Clone"), save=new("Save"), del=new("Delete"), plan=new("Plan Quest"), realize=new("Realize Plan"), simulate=new("Compare 12"), send=new("Open in Playground"), mv=new("MV Export Preview");
        toolbar.Items.AddRange([add,clone,save,del,new ToolStripSeparator(),plan,realize,simulate,send,mv,new ToolStripSeparator(),_status]);
        var left=new Panel{Dock=DockStyle.Fill,Padding=new Padding(6)}; left.Controls.Add(_quests);
        var tabs=new TabControl{Dock=DockStyle.Fill};
        var identity=new TabPage("Quest"); identity.Controls.Add(BuildIdentity());
        var b=new TabPage("Bindings"); b.Controls.Add(_bindings);
        var a=new TabPage("Actions"); a.Controls.Add(_actions);
        var m=new TabPage("HTN Methods"); m.Controls.Add(_methods);
        var state=new TabPage("Initial State"); state.Controls.Add(_state); state.Controls.Add(_stateTags);
        var outp=new TabPage("Plan / Preview"); outp.Controls.Add(_preview);
        tabs.TabPages.AddRange([identity,b,a,m,state,outp]);
        var split=new SplitContainer{Dock=DockStyle.Fill,SplitterDistance=245,FixedPanel=FixedPanel.Panel1}; split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(tabs);
        Controls.Add(split); Controls.Add(toolbar);

        add.Click+=(_,_)=>NewQuest(); clone.Click+=(_,_)=>CloneQuest(); save.Click+=async(_,_)=>await SaveCurrentAsync().ConfigureAwait(true); del.Click+=(_,_)=>DeleteCurrent(); plan.Click+=(_,_)=>PlanCurrent(); realize.Click+=async(_,_)=>await RealizeCurrentAsync().ConfigureAwait(true); simulate.Click+=(_,_)=>ComparePlans(); send.Click+=(_,_)=>SendToPlayground(); mv.Click+=(_,_)=>PreviewMvExport();
        _quests.SelectedIndexChanged+=(_,_)=>{ if(!_loading && _quests.SelectedItem is QuestListItem item) BindQuest(item.Quest,ProjectPathFor(item.Quest.Id) is string pp && File.Exists(pp)?pp:null); };
        foreach(Control c in new Control[]{_id,_name,_root,_goals,_description,_stateTags}) c.TextChanged+=(_,_)=>MarkDirty();
        foreach(DataGridView grid in new[]{_bindings,_actions,_methods,_state}) { grid.CellValueChanged+=(_,_)=>MarkDirty(); grid.RowsAdded+=(_,_)=>MarkDirty(); grid.RowsRemoved+=(_,_)=>MarkDirty(); }
    }

    public bool HasUnsavedChanges=>_dirty;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? PlaygroundResultReady;

    public async Task InitializeAsync(CancellationToken cancellationToken=default){ await ReloadFromDiskAsync(false,cancellationToken).ConfigureAwait(true); }
    public async Task ReloadFromDiskAsync(bool discardUnsavedChanges=true,CancellationToken cancellationToken=default)
    {
        if(_dirty && !discardUnsavedChanges){} else if(_dirty) DiscardUnsavedChanges();
        await _session.ReloadQuestGrammarsAsync(cancellationToken).ConfigureAwait(true); RebuildList(_working?.Id); SetStatus($"{_session.QuestGrammars.Count} quest grammars loaded.");
    }

    public async Task SaveCurrentAsync(CancellationToken cancellationToken=default)
    {
        QuestGrammarDefinition? grammar=ReadEditor(); if(grammar is null)return; var errors=QuestGrammarValidator.Validate(grammar); if(errors.Count>0){MessageBox.Show(this,string.Join(Environment.NewLine,errors),"Quest grammar cannot be saved",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        try{_path=await _session.SaveQuestGrammarAsync(grammar,_path,cancellationToken).ConfigureAwait(true);_baseline=grammar;_working=grammar;SetDirty(false);RebuildList(grammar.Id);SetStatus("Saved "+Path.GetFileName(_path));}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not save quest grammar",MessageBoxButtons.OK,MessageBoxIcon.Error);SetStatus(ex.Message);}
    }

    public void DiscardUnsavedChanges(){ if(_baseline is not null) BindQuest(_baseline,_path); else SetDirty(false); }
    public async Task<bool> PrepareForCloseAsync(CancellationToken cancellationToken=default)
    {
        if(!_dirty)return true; var choice=MessageBox.Show(this,"Quest Lab has unsaved changes. Save them before closing Text Studio?","Playroma Text Studio",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question); if(choice==DialogResult.Cancel)return false; if(choice==DialogResult.Yes){await SaveCurrentAsync(cancellationToken).ConfigureAwait(true);return !_dirty;} DiscardUnsavedChanges();return true;
    }

    public QuestLabRecoveryState? CaptureRecoveryState()=>!_dirty||_working is null?null:new(JsonSerializer.Serialize(ReadEditor(),RecipeJson.Options),_path,_baseline is null?null:JsonSerializer.Serialize(_baseline,RecipeJson.Options),CaptureState().Capture());
    public void RestoreRecoveryState(QuestLabRecoveryState state)
    {
        QuestGrammarDefinition? working=JsonSerializer.Deserialize<QuestGrammarDefinition>(state.QuestJson,RecipeJson.Options); QuestGrammarDefinition? baseline=string.IsNullOrWhiteSpace(state.BaselineQuestJson)?null:JsonSerializer.Deserialize<QuestGrammarDefinition>(state.BaselineQuestJson!,RecipeJson.Options); if(working is null)return; BindQuest(working,state.Path);_baseline=baseline;RestoreState(state.InitialState);SetDirty(true);SetStatus("Recovered unsaved quest grammar.");
    }

    private Control BuildIdentity()
    {
        var top=new TableLayoutPanel{Dock=DockStyle.Top,Height=115,ColumnCount=2,RowCount=3,Padding=new Padding(6)};top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        AddRow(top,0,"ID",_id);AddRow(top,1,"Name",_name);AddRow(top,2,"Root Task",_root);
        var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,SplitterDistance=125}; var dpanel=new Panel{Dock=DockStyle.Fill};dpanel.Controls.Add(_description);dpanel.Controls.Add(new Label{Text="Description",Dock=DockStyle.Top,Height=22}); var gpanel=new Panel{Dock=DockStyle.Fill};gpanel.Controls.Add(_goals);gpanel.Controls.Add(new Label{Text="Goal predicates (one per line)",Dock=DockStyle.Top,Height=22}); split.Panel1.Controls.Add(dpanel);split.Panel2.Controls.Add(gpanel);
        var panel=new Panel{Dock=DockStyle.Fill};panel.Controls.Add(split);panel.Controls.Add(top);return panel;
    }

    private static void AddRow(TableLayoutPanel p,int row,string label,Control c){p.RowStyles.Add(new RowStyle(SizeType.Absolute,34));p.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);c.Dock=DockStyle.Fill;p.Controls.Add(c,1,row);}
    private static DataGridView Grid()=>new(){Dock=DockStyle.Fill,AllowUserToAddRows=true,AllowUserToDeleteRows=true,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false};
    private void ConfigureGrids()
    {
        _bindings.Columns.Add("Id","ID");_bindings.Columns.Add(new DataGridViewComboBoxColumn{Name="Kind",HeaderText="Kind",DataSource=Enum.GetNames<StoryletBindingKind>()});_bindings.Columns.Add("Source","Candidate context key");_bindings.Columns.Add("Tags","Required tags");
        _actions.Columns.Add("Id","ID");_actions.Columns.Add("Name","Name");_actions.Columns.Add("Pre","Preconditions (; separated)");_actions.Columns.Add("Effects","Effects (tag:+x; set:k=v; inc:k=1)");_actions.Columns.Add("Cost","Cost");_actions.Columns.Add("Recipe","Realization recipe");
        _methods.Columns.Add("Id","ID");_methods.Columns.Add("Task","Task");_methods.Columns.Add("Priority","Priority");_methods.Columns.Add("Subtasks","Subtasks (, separated)");_methods.Columns.Add("Pre","Preconditions (; separated)");
        _state.Columns.Add("Key","State key");_state.Columns.Add("Value","Initial value");
    }

    private void NewQuest(){var q=new QuestGrammarDefinition("project.quest.new",1,"New Quest Grammar",RootTask:"quest",GoalPredicates:["tag(\"quest-complete\")"],Actions:[new("finish","Complete objective",Effects:[new(QuestEffectKind.AddTag,"quest-complete")],RealizationRecipeId:"phase12.quest.journal-update")],Methods:[new("quest.default","quest",["finish"],Priority:10)],SummaryRecipeId:"phase12.quest.summary");BindQuest(q,null);_baseline=null;SetDirty(true);}
    private void CloneQuest(){if(_working is null)return;var q=_working with{Id=_working.Id+".custom",Name=_working.Name+" (Custom)"};BindQuest(q,null);_baseline=null;SetDirty(true);}
    private void DeleteCurrent(){if(string.IsNullOrWhiteSpace(_path)||!File.Exists(_path)){MessageBox.Show(this,"Built-in quest grammars are read-only. Clone it first.","Quest Lab");return;}if(MessageBox.Show(this,"Delete this project quest grammar?","Quest Lab",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;_session.DeleteQuestGrammar(_path);_path=null;_working=null;_baseline=null;SetDirty(false);_ = ReloadFromDiskAsync(true);}

    private void PlanCurrent()
    {
        try{QuestGrammarDefinition? grammar=ReadEditor();if(grammar is null)return;QuestWorldState state=CaptureState();_lastPlan=_session.PlanQuest(grammar,state);_lastRealization=null;ShowPlan(_lastPlan);SetStatus($"Plan found: {_lastPlan.Steps.Count} steps via {_lastPlan.Diagnostics.Mode}.");}
        catch(Exception ex){_preview.Text=ex.Message;SetStatus("Planning failed: "+ex.Message);}
    }
    private async Task RealizeCurrentAsync(){if(_lastPlan is null)PlanCurrent();if(_lastPlan is null)return;try{_lastRealization=await _session.RealizeQuestAsync(_lastPlan).ConfigureAwait(true);ShowRealization(_lastRealization);SetStatus("Quest plan realized through Text Studio recipes.");}catch(Exception ex){_preview.Text=ex.Message;SetStatus(ex.Message);}}
    private void ComparePlans(){QuestGrammarDefinition? grammar=ReadEditor();if(grammar is null)return;var lines=new List<string>();for(int i=0;i<12;i++){try{ulong seed=Playroma.Text.Core.Random.SeedFactory.Derive(_session.Seed,"quest-compare:"+i);QuestPlan p=_session.PlanQuest(grammar,CaptureState(),seed);lines.Add($"{i+1,2}. {p.Diagnostics.Mode,-13} {p.Steps.Count,2} steps / cost {p.TotalCost:0.##}: "+string.Join(" → ",p.Steps.Select(x=>x.Action.Name)));}catch(Exception ex){lines.Add($"{i+1,2}. no plan — {ex.Message}");}}_preview.Text=string.Join(Environment.NewLine,lines);SetStatus("Compared 12 deterministic planning seeds; live state unchanged.");}
    private void SendToPlayground(){if(_lastRealization is null){SetStatus("Realize the quest first.");return;}_session.AdoptQuestResult(_lastRealization);PlaygroundResultReady?.Invoke(this,EventArgs.Empty);SetStatus("Quest realization opened in Playground.");}
    private void PreviewMvExport(){if(_lastPlan is null){PlanCurrent();if(_lastPlan is null)return;}var objectives=_lastRealization?.Steps.Select(x=>x.Bundle.PrimaryText).ToArray(); MvQuestExportPlan export=MvQuestPlanCompiler.CompileSuggestedCommonEvent(_lastPlan,_lastRealization?.Bundle.PrimaryText,objectives);_preview.Text=export.PreviewText+Environment.NewLine+Environment.NewLine+"RAW MV COMMANDS"+Environment.NewLine+string.Join(Environment.NewLine,export.RawCommands)+Environment.NewLine+Environment.NewLine+"STATE MAPPING SUGGESTIONS"+Environment.NewLine+string.Join(Environment.NewLine,export.StateSuggestions);SetStatus("Generated non-destructive MV Common Event suggestion.");}

    private void ShowPlan(QuestPlan p){var lines=new List<string>{$"QUEST PLAN — {p.Grammar.Name}",$"Mode: {p.Diagnostics.Mode} | Steps: {p.Steps.Count} | Cost: {p.TotalCost:0.###}",$"Search: expanded {p.Diagnostics.ExpandedNodes}, generated {p.Diagnostics.GeneratedNodes}, max frontier {p.Diagnostics.MaxFrontier}",""};if(p.Bindings.Count>0)lines.Add("Bindings: "+string.Join(", ",p.Bindings.Select(x=>$"{x.Id}={x.Candidate.Name}")));for(int i=0;i<p.Steps.Count;i++)lines.Add($"{i+1}. {p.Steps[i].Action.Name}  [cost {p.Steps[i].Action.Cost:0.##}]"); var graph=QuestObjectiveGraphBuilder.Build(p); if(graph.Edges.Count>0){lines.Add("");lines.Add("OBJECTIVE DEPENDENCIES");foreach(var edge in graph.Edges)lines.Add($"{edge.FromStepIndex+1} → {edge.ToStepIndex+1}  ({edge.Reason})");} if(p.EffectiveValidationIssues.Count>0){lines.Add("");lines.Add("VALIDATION:");lines.AddRange(p.EffectiveValidationIssues.Select(x=>"! "+x));}else{lines.Add("");lines.Add("Causal validation: PASS");}_preview.Text=string.Join(Environment.NewLine,lines);}
    private void ShowRealization(QuestRealizationResult r){var lines=new List<string>{$"{r.Plan.Grammar.Name}","",r.Bundle.PrimaryText,"","OBJECTIVES"};for(int i=0;i<r.Steps.Count;i++)lines.Add($"{i+1}. {r.Steps[i].Bundle.PrimaryText}");lines.Add("");lines.Add($"Planner: {r.Plan.Diagnostics.Mode}; cost {r.Plan.TotalCost:0.###}; causal validation PASS");_preview.Text=string.Join(Environment.NewLine,lines);}

    private void BindQuest(QuestGrammarDefinition q,string? path){_loading=true;try{_working=q;_baseline=q;_path=path;_id.Text=q.Id;_name.Text=q.Name;_description.Text=q.Description??"";_root.Text=q.RootTask??"";_goals.Text=string.Join(Environment.NewLine,q.EffectiveGoalPredicates);_bindings.Rows.Clear();foreach(var x in q.EffectiveBindings)_bindings.Rows.Add(x.Id,x.Kind.ToString(),x.CandidateContextKey,string.Join(",",x.EffectiveRequiredTags));_actions.Rows.Clear();foreach(var x in q.EffectiveActions)_actions.Rows.Add(x.Id,x.Name,string.Join("; ",x.EffectivePreconditions),string.Join("; ",x.EffectiveEffects.Select(FormatEffect)),x.Cost.ToString(CultureInfo.InvariantCulture),x.RealizationRecipeId??"");_methods.Rows.Clear();foreach(var x in q.EffectiveMethods)_methods.Rows.Add(x.Id,x.TaskId,x.Priority,string.Join(",",x.Subtasks),string.Join("; ",x.EffectivePreconditions));_lastPlan=null;_lastRealization=null;_preview.Clear();SetDirty(false);}finally{_loading=false;}}
    private QuestGrammarDefinition? ReadEditor(){if(string.IsNullOrWhiteSpace(_id.Text)&&string.IsNullOrWhiteSpace(_name.Text))return _working;var bindings=new List<StoryletBindingDefinition>();foreach(DataGridViewRow r in _bindings.Rows){if(r.IsNewRow||string.IsNullOrWhiteSpace(Cell(r,"Id")))continue;Enum.TryParse(Cell(r,"Kind"),true,out StoryletBindingKind kind);bindings.Add(new(Cell(r,"Id"),kind,Cell(r,"Source"),true,true,Split(Cell(r,"Tags"))));}var actions=new List<QuestActionDefinition>();foreach(DataGridViewRow r in _actions.Rows){if(r.IsNewRow||string.IsNullOrWhiteSpace(Cell(r,"Id")))continue;double.TryParse(Cell(r,"Cost"),NumberStyles.Float,CultureInfo.InvariantCulture,out double cost);if(cost<0)cost=1;actions.Add(new(Cell(r,"Id"),Cell(r,"Name"),Cost:cost,Preconditions:SplitSemi(Cell(r,"Pre")),Effects:ParseEffects(Cell(r,"Effects")),RealizationRecipeId:Empty(Cell(r,"Recipe"))??"phase12.quest.journal-update"));}var methods=new List<QuestMethodDefinition>();foreach(DataGridViewRow r in _methods.Rows){if(r.IsNewRow||string.IsNullOrWhiteSpace(Cell(r,"Id")))continue;int.TryParse(Cell(r,"Priority"),out int pri);methods.Add(new(Cell(r,"Id"),Cell(r,"Task"),Split(Cell(r,"Subtasks")),Priority:pri,Preconditions:SplitSemi(Cell(r,"Pre"))));}_working=new(_id.Text.Trim(),1,_name.Text.Trim(),Empty(_description.Text),true,Empty(_root.Text),SplitLines(_goals.Text),bindings,actions,methods,"phase12.quest.summary",Planner:new());return _working;}
    private QuestWorldState CaptureState(){var s=new QuestWorldState();foreach(string tag in Split(_stateTags.Text))s.Tags.Add(tag);foreach(DataGridViewRow r in _state.Rows){if(r.IsNewRow)continue;string key=Cell(r,"Key");if(!string.IsNullOrWhiteSpace(key))s.Values[key]=ParseValue(Cell(r,"Value"));}return s;}
    private void RestoreState(QuestWorldSnapshot? snap){_state.Rows.Clear();_stateTags.Clear();if(snap is null)return;_stateTags.Text=string.Join(",",snap.Tags);foreach(var p in snap.Values)_state.Rows.Add(p.Key,Serialize(p.Value));}

    private void RebuildList(string? selectedId){_loading=true;try{_quests.Items.Clear();foreach(var q in _session.QuestGrammars)_quests.Items.Add(new QuestListItem(q,_session.IsBuiltInQuestGrammar(q.Id)));var item=_quests.Items.Cast<QuestListItem>().FirstOrDefault(x=>x.Quest.Id.Equals(selectedId,StringComparison.OrdinalIgnoreCase));if(item is not null)_quests.SelectedItem=item;else if(_quests.Items.Count>0)_quests.SelectedIndex=0;}finally{_loading=false;}if(_quests.SelectedItem is QuestListItem chosen)BindQuest(chosen.Quest,File.Exists(ProjectPathFor(chosen.Quest.Id))?ProjectPathFor(chosen.Quest.Id):null);}
    private string ProjectPathFor(string id)=>Path.Combine(_session.Options.ProjectRoot,"data","PlayromaText","quests",Safe(id)+".playquest.json");
    private static string Safe(string v)=>string.Join("-",v.Split(Path.GetInvalidFileNameChars(),StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static string Cell(DataGridViewRow r,string n)=>Convert.ToString(r.Cells[n].Value,CultureInfo.InvariantCulture)?.Trim()??"";
    private static string[] Split(string? v)=>(v??"").Split([',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static string[] SplitSemi(string? v)=>(v??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static string[] SplitLines(string? v)=>(v??"").Split(["\r\n","\n"],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static string? Empty(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
    private static object? ParseValue(string v){if(bool.TryParse(v,out bool b))return b;if(long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out long i))return i;if(double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out double d))return d;return v;}
    private static string Serialize(object? v)=>v is null?"":v is IFormattable f?f.ToString(null,CultureInfo.InvariantCulture)??"":v.ToString()??"";
    private static string FormatEffect(QuestEffectDefinition e)=>e.Kind switch{QuestEffectKind.AddTag=>"tag:+"+e.Key,QuestEffectKind.RemoveTag=>"tag:-"+e.Key,QuestEffectKind.Set=>"set:"+e.Key+"="+Serialize(e.Value),QuestEffectKind.Increment=>"inc:"+e.Key+"="+Serialize(e.Value),_=>e.Key};
    private static QuestEffectDefinition[] ParseEffects(string text){var list=new List<QuestEffectDefinition>();foreach(string raw in SplitSemi(text)){string t=raw.Trim();if(t.StartsWith("tag:+",StringComparison.OrdinalIgnoreCase))list.Add(new(QuestEffectKind.AddTag,t[5..]));else if(t.StartsWith("tag:-",StringComparison.OrdinalIgnoreCase))list.Add(new(QuestEffectKind.RemoveTag,t[5..]));else if(t.StartsWith("set:",StringComparison.OrdinalIgnoreCase)){var p=t[4..].Split('=',2);list.Add(new(QuestEffectKind.Set,p[0],p.Length>1?ParseValue(p[1]):true));}else if(t.StartsWith("inc:",StringComparison.OrdinalIgnoreCase)){var p=t[4..].Split('=',2);list.Add(new(QuestEffectKind.Increment,p[0],p.Length>1?ParseValue(p[1]):1));}}return list.ToArray();}
    private void MarkDirty(){if(_loading)return;_working=ReadEditor();SetDirty(true);}private void SetDirty(bool v){if(_dirty==v)return;_dirty=v;DirtyStateChanged?.Invoke(this,EventArgs.Empty);}private void SetStatus(string t)=>_status.Text=t;
    private sealed record QuestListItem(QuestGrammarDefinition Quest,bool BuiltIn){public override string ToString()=>(BuiltIn?"◆ ":"  ")+Quest.Name;}
}
