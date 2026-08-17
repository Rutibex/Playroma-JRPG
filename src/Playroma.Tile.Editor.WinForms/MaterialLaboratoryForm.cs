using System.Drawing.Imaging;
using Playroma.Tile.Autotiles.Services;
using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.Editor.WinForms.Controls;
using Playroma.Tile.Editor.WinForms.Services;
using Playroma.Tile.Materials.Graph;
using Playroma.Tile.Materials.Integration;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Recipes;
using Playroma.Tile.Materials.Serialization;
using Playroma.Tile.Materials.Services;
using Playroma.Tile.Materials.Validation;

namespace Playroma.Tile.Editor.WinForms;

public sealed class MaterialLaboratoryForm : Form
{
    private readonly EditorWorkspace _workspace;
    private readonly MaterialNodeRegistry _registry = new();
    private readonly MaterialPresetCatalog _presets;
    private readonly MaterialRenderService _renderer;
    private readonly MaterialCandidateGenerator _candidateGenerator = new();
    private readonly MaterialValidator _validator = new();
    private readonly ListBox _assets = new();
    private readonly ComboBox _category = new();
    private readonly ComboBox _preset = new();
    private readonly ComboBox _style = new();
    private readonly ComboBox _condition = new();
    private readonly NumericUpDown _seed = new();
    private readonly NumericUpDown _candidateCount = new();
    private readonly NumericUpDown _renderSize = new();
    private readonly NumericUpDown _mutation = new();
    private readonly CheckBox _lockPalette = new() { Text = "Lock palette", AutoSize = true };
    private readonly CheckBox _lockStructure = new() { Text = "Lock structure", AutoSize = true };
    private readonly CheckBox _lockDetail = new() { Text = "Lock detail", AutoSize = true };
    private readonly MaterialCandidateGallery _gallery = new();
    private readonly MaterialPreviewControl _preview = new();
    private readonly MaterialGraphCanvas _graphCanvas = new();
    private readonly ListBox _nodePalette = new();
    private readonly DataGridView _parameterGrid = new();
    private readonly DataGridView _inputGrid = new();
    private readonly ListView _issues = new();
    private readonly Label _status = new();
    private readonly List<MaterialCandidate> _candidates = new();
    private CancellationTokenSource? _generationCts;
    private MaterialRecipe? _recipe;
    private MaterialAssetReference? _reference;
    private MaterialResult? _rendered;
    private bool _loading;

    private TileProject Project => _workspace.Project ?? throw new InvalidOperationException("No project is open.");

    public MaterialLaboratoryForm(EditorWorkspace workspace)
    {
        _workspace = workspace;
        _presets = new MaterialPresetCatalog(_registry);
        _renderer = new MaterialRenderService(_registry);
        Text = "Playroma Tile — Procedural Material Laboratory";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1540; Height = 960; MinimumSize = new Size(1160, 720);
        Controls.Add(BuildLayout());
        FormClosing += (_, _) => CancelGeneration();
        PopulateSelectors();
        RefreshAssets();
        CreateFromPreset();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { CancelGeneration(); DisposeCandidates(); _rendered?.Dispose(); }
        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildToolbar(), 0, 0);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1400, 760), SplitterDistance = 245, Panel1MinSize = 200, Panel2MinSize = 800 };
        split.Panel1.Controls.Add(BuildAssetPanel()); split.Panel2.Controls.Add(BuildTabs()); root.Controls.Add(split, 0, 1);
        _status.AutoSize = true; _status.Padding = new Padding(4); _status.Text = "Ready."; root.Controls.Add(_status, 0, 2); return root;
    }

    private ToolStrip BuildToolbar()
    {
        var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        bar.Items.Add(Button("New Preset", (_, _) => CreateFromPreset()));
        bar.Items.Add(Button("Generate", async (_, _) => await GenerateCandidatesAsync(false)));
        bar.Items.Add(Button("Surprise Me", async (_, _) => { _seed.Value = Random.Shared.Next(1, int.MaxValue); SelectRandomPreset(); await GenerateCandidatesAsync(false); }));
        bar.Items.Add(Button("Mutate Selected", async (_, _) => await GenerateCandidatesAsync(true)));
        bar.Items.Add(Button("Keep Selected", (_, _) => KeepSelected()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(Button("Save Recipe", (_, _) => SaveCurrent()));
        bar.Items.Add(Button("Import Exemplar", (_, _) => ImportExemplar()));
        bar.Items.Add(Button("Export Material", (_, _) => ExportMaterial()));
        bar.Items.Add(Button("Wang Preview", (_, _) => ExportWangPreview()));
        bar.Items.Add(Button("Send to Autotile", (_, _) => SendToAutotile()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(Button("Cancel", (_, _) => CancelGeneration()));
        return bar;
    }

    private Control BuildAssetPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(4) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "Material Recipes", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 5) }, 0, 0);
        _assets.Dock = DockStyle.Fill; _assets.DisplayMember = nameof(MaterialAssetReference.Name); _assets.SelectedIndexChanged += (_, _) => SelectReference(_assets.SelectedItem as MaterialAssetReference); panel.Controls.Add(_assets, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var duplicate = new Button { Text = "Duplicate", AutoSize = true }; duplicate.Click += (_, _) => DuplicateCurrent();
        var delete = new Button { Text = "Delete", AutoSize = true }; delete.Click += (_, _) => DeleteCurrent(); buttons.Controls.AddRange(new Control[] { duplicate, delete }); panel.Controls.Add(buttons, 0, 2);
        var note = new Label { Text = "Recipes store the graph, seed, exposed controls, style, history, and provenance. Generated pixels remain reproducible.", AutoSize = true, MaximumSize = new Size(215, 0), ForeColor = Color.DimGray, Padding = new Padding(0, 8, 0, 0) }; panel.Controls.Add(note, 0, 3); return panel;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildQuickTab()); tabs.TabPages.Add(BuildGraphTab()); tabs.TabPages.Add(BuildValidationTab()); return tabs;
    }

    private TabPage BuildQuickTab()
    {
        var page = new TabPage("Quick Generator");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.Controls.Add(BuildQuickControls(), 0, 0);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 620), SplitterDistance = 760, Panel1MinSize = 420, Panel2MinSize = 300 };
        var galleryHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(42,42,45) }; _gallery.Width = 730; _gallery.Height = 500; _gallery.SelectionChanged += (_, _) => UpdateCandidatePreview(); galleryHost.Controls.Add(_gallery); split.Panel1.Controls.Add(galleryHost);
        var previewRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6) };
        previewRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); previewRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewRoot.Controls.Add(new Label { Text = "Selected candidate — 3 × 3 seamless preview", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0); _preview.Dock = DockStyle.Fill; _preview.Tiled = true; _preview.ShowGrid = true; previewRoot.Controls.Add(_preview, 0, 1);
        var quickButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; foreach (var (text, action) in new (string, Action)[] { ("Keep",KeepSelected),("Make Similar",()=>_ = GenerateCandidatesAsync(true)),("Breed with #1",BreedSelected),("Open in Graph",OpenSelectedInGraph),("Send to Autotile",SendToAutotile) }) { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); quickButtons.Controls.Add(b); } previewRoot.Controls.Add(quickButtons, 0, 2);
        split.Panel2.Controls.Add(previewRoot); root.Controls.Add(split, 0, 1); page.Controls.Add(root); return page;
    }

    private Control BuildQuickControls()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(5), WrapContents = true };
        AddLabeled(flow, "Category", _category, 150); AddLabeled(flow, "Material", _preset, 175); AddLabeled(flow, "Style", _style, 170); AddLabeled(flow, "Condition", _condition, 145);
        ConfigureNumeric(_seed, 0, int.MaxValue, 0, 105); AddLabeled(flow, "Seed", _seed);
        ConfigureNumeric(_candidateCount, 1, 16, 8, 58); AddLabeled(flow, "Candidates", _candidateCount);
        ConfigureNumeric(_renderSize, 48, 512, 192, 65); _renderSize.Increment = 48; AddLabeled(flow, "Work size", _renderSize);
        ConfigureNumeric(_mutation, 0, 100, 20, 58); AddLabeled(flow, "Mutation %", _mutation);
        var locks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 18, 4, 4) };
        locks.Controls.Add(_lockPalette); locks.Controls.Add(_lockStructure); locks.Controls.Add(_lockDetail); flow.Controls.Add(locks);
        _category.SelectedIndexChanged += (_, _) => FilterPresets();
        return flow;
    }

    private TabPage BuildGraphTab()
    {
        var page = new TabPage("Advanced Graph"); var main = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 620), SplitterDistance = 220, Panel1MinSize = 180, Panel2MinSize = 700 };
        var paletteRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(4) }; paletteRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); paletteRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); paletteRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); paletteRoot.Controls.Add(new Label { Text = "Node Library", Font = new Font(Font, FontStyle.Bold), AutoSize = true }, 0, 0);
        _nodePalette.Dock = DockStyle.Fill; _nodePalette.DisplayMember = nameof(IMaterialNodeDefinition.DisplayName); _nodePalette.DoubleClick += (_, _) => AddSelectedNode(); paletteRoot.Controls.Add(_nodePalette, 0, 1); var add = new Button { Text = "Add Selected Node", Dock = DockStyle.Fill }; add.Click += (_, _) => AddSelectedNode(); paletteRoot.Controls.Add(add, 0, 2); main.Panel1.Controls.Add(paletteRoot);
        var right = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 620), SplitterDistance = 800, Panel1MinSize = 480, Panel2MinSize = 260 };
        var canvasRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 }; canvasRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize)); canvasRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; foreach (var (text, action) in new (string, Action)[] { ("Render Graph",RenderGraph),("Delete Node",DeleteSelectedNode),("Auto Layout",AutoLayout),("Duplicate Node",DuplicateSelectedNode) }) { var b=new Button{Text=text,AutoSize=true};b.Click+=(_,_)=>action();tools.Controls.Add(b);} canvasRoot.Controls.Add(tools,0,0); _graphCanvas.Dock=DockStyle.Fill; _graphCanvas.Registry=_registry; _graphCanvas.SelectedNodeChanged+=(_,_)=>PopulateNodeEditor(); _graphCanvas.GraphChanged+=(_,_)=>MarkRecipeChanged(); canvasRoot.Controls.Add(_graphCanvas,0,1); right.Panel1.Controls.Add(canvasRoot);
        right.Panel2.Controls.Add(BuildNodeEditor()); main.Panel2.Controls.Add(right); page.Controls.Add(main); return page;
    }

    private Control BuildNodeEditor()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var parameters = new TabPage("Parameters"); ConfigureGrid(_parameterGrid); _parameterGrid.Columns.Add("Name","Parameter"); _parameterGrid.Columns.Add("Value","Value"); _parameterGrid.CellEndEdit += (_, e) => ApplyParameterEdit(e.RowIndex); parameters.Controls.Add(_parameterGrid); tabs.TabPages.Add(parameters);
        var inputs = new TabPage("Connections"); ConfigureGrid(_inputGrid); _inputGrid.Columns.Add("Input","Input"); var sourceColumn = new DataGridViewComboBoxColumn { Name="Source",HeaderText="Source node",FlatStyle=FlatStyle.Flat }; _inputGrid.Columns.Add(sourceColumn); _inputGrid.CellValueChanged += (_, e) => { if (!_loading && e.RowIndex >= 0 && e.ColumnIndex == 1) ApplyConnectionEdit(e.RowIndex); }; inputs.Controls.Add(_inputGrid); tabs.TabPages.Add(inputs);
        return tabs;
    }

    private TabPage BuildValidationTab()
    {
        var page = new TabPage("Validation & History"); _issues.Dock=DockStyle.Fill; _issues.View=View.Details; _issues.FullRowSelect=true; _issues.GridLines=true; _issues.Columns.Add("Severity",85); _issues.Columns.Add("Code",140); _issues.Columns.Add("Message",760); page.Controls.Add(_issues); return page;
    }

    private void PopulateSelectors()
    {
        _category.DropDownStyle=_preset.DropDownStyle=_style.DropDownStyle=_condition.DropDownStyle=ComboBoxStyle.DropDownList;
        _category.Items.Add("All"); _category.Items.AddRange(Enum.GetNames<MaterialCategory>()); _category.SelectedIndex=0;
        _style.Items.AddRange(Enum.GetValues<MaterialStyle>().Cast<object>().ToArray()); _style.SelectedItem=MaterialStyle.ClassicRpgMaker;
        _condition.Items.AddRange(Enum.GetValues<MaterialCondition>().Cast<object>().ToArray()); _condition.SelectedItem=MaterialCondition.Clean;
        _nodePalette.Items.AddRange(_registry.Definitions.Cast<object>().ToArray()); FilterPresets();
    }

    private void FilterPresets()
    {
        var selected=_preset.SelectedItem?.ToString(); _preset.Items.Clear(); var category=_category.SelectedIndex<=0?(MaterialCategory?)null:Enum.Parse<MaterialCategory>(_category.SelectedItem!.ToString()!);
        foreach(var descriptor in _presets.GetPresets().Where(x=>category is null||x.Category==category))_preset.Items.Add(descriptor.Name);
        if(selected is not null&&_preset.Items.Contains(selected))_preset.SelectedItem=selected;else if(_preset.Items.Count>0)_preset.SelectedIndex=0;
    }

    private void CreateFromPreset()
    {
        var name=_preset.SelectedItem?.ToString()??"Grass"; var style=_style.SelectedItem is MaterialStyle s?s:MaterialStyle.ClassicRpgMaker; var condition=_condition.SelectedItem is MaterialCondition c?c:MaterialCondition.Clean;
        _recipe=_presets.Create(name,style,condition,(int)_seed.Value); _reference=null; _graphCanvas.Graph=_recipe.Graph; _assets.ClearSelected(); ClearResults(); AutoLayout(); _status.Text=$"Created {name}. Generate candidates or edit the graph.";
    }

    private async Task GenerateCandidatesAsync(bool fromSelected)
    {
        if (_recipe is null) CreateFromPreset();
        CancelGeneration(); _generationCts=new CancellationTokenSource(); var token=_generationCts.Token;
        try
        {
            UseWaitCursor=true; var source=fromSelected&&_gallery.SelectedCandidate is { } selected?selected.Recipe:_recipe!; var progress=new Progress<MaterialProgress>(p=>_status.Text=$"{p.Stage} — {p.Fraction:P0}");
            var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (_lockPalette.Checked) { locked.Add("Saturation"); locked.Add("Contrast"); } if (_lockStructure.Checked) locked.Add("Scale"); if (_lockDetail.Checked) locked.Add("Irregularity");
            var generated=await _candidateGenerator.GenerateAsync(source,(int)_candidateCount.Value,(int)_renderSize.Value,(int)_renderSize.Value,(int)_seed.Value,(float)_mutation.Value/100f,token,progress,locked);
            DisposeCandidates(); _candidates.AddRange(generated); _gallery.SetCandidates(_candidates); ResizeGallery(); UpdateCandidatePreview(); _status.Text=$"Generated {_candidates.Count} deterministic candidates.";
        }
        catch(OperationCanceledException){_status.Text="Generation cancelled.";}
        catch(Exception ex){ShowError("Material generation failed.",ex);}
        finally{UseWaitCursor=false;_generationCts?.Dispose();_generationCts=null;}
    }

    private void UpdateCandidatePreview(){_preview.Buffer=_gallery.SelectedCandidate?.Result.BaseColor;RefreshIssues(_gallery.SelectedCandidate?.Result);}
    private void KeepSelected()
    {
        if(_gallery.SelectedCandidate is not { } candidate)return; _recipe=MaterialSerializer.DeepClone(candidate.Recipe); _recipe.Id=_reference?.Id??Guid.NewGuid(); _recipe.Seed=candidate.Seed; candidate.IsKept=true; var hash=candidate.Result.BaseColor is null?string.Empty:MaterialGraphEvaluator.ComputeBufferHash(candidate.Result.BaseColor); _recipe.History.Add(new MaterialGenerationRecord{Seed=candidate.Seed,ResultHash=hash,IsFavourite=true,Rating=5}); _graphCanvas.Graph=_recipe.Graph; _rendered?.Dispose(); _rendered=_renderer.Render(_recipe,Math.Max(48,(int)_renderSize.Value),Math.Max(48,(int)_renderSize.Value),candidate.Seed,MaterialQuality.Final,assetResolver:ResolveAsset,frameCount:_recipe.Category==MaterialCategory.Fluid?3:1); _status.Text=$"Kept candidate seed {candidate.Seed}. Save the recipe when ready."; MarkRecipeChanged();
    }
    private void OpenSelectedInGraph(){KeepSelected();}

    private void BreedSelected()
    {
        if(_candidates.Count<2||_gallery.SelectedCandidate is not { } selected)return;var child=MaterialCandidateGenerator.Breed(_candidates[0].Recipe,selected.Recipe,unchecked(_candidates[0].Seed^selected.Seed));_recipe=child;_reference=null;_graphCanvas.Graph=child.Graph;_seed.Value=Math.Clamp(child.Seed,(int)_seed.Minimum,(int)_seed.Maximum);_status.Text="Created a hybrid recipe from candidate #1 and the selected candidate.";RenderGraph();
    }


    private void RenderGraph()
    {
        if(_recipe is null)return; try{UseWaitCursor=true;_rendered?.Dispose();_rendered=_renderer.Render(_recipe,(int)_renderSize.Value,(int)_renderSize.Value,(int)_seed.Value,MaterialQuality.Final,assetResolver:ResolveAsset,frameCount:_recipe.Category==MaterialCategory.Fluid?3:1);_preview.Buffer=_rendered.BaseColor;RefreshIssues(_rendered);_status.Text="Graph rendered at final quality.";}catch(Exception ex){ShowError("The graph could not be rendered.",ex);}finally{UseWaitCursor=false;}
    }

    private void SaveCurrent()
    {
        if(_recipe is null)return; try{if(_rendered?.BaseColor is null)RenderGraph();_recipe.Revision++;if(_reference is null)_reference=MaterialSerializer.SaveNew(Project,_recipe,_rendered?.BaseColor);else MaterialSerializer.Save(Project,_recipe,_rendered?.BaseColor,_reference);_workspace.MarkProjectDirty();RefreshAssets(_recipe.Id);_status.Text="Material recipe saved.";}catch(Exception ex){ShowError("The material recipe could not be saved.",ex);}
    }

    private void SendToAutotile()
    {
        if(_recipe is null)return; if(_rendered?.BaseColor is null)RenderGraph(); if(_rendered?.BaseColor is null)return;
        using var dialog=new MaterialAutotileSettingsDialog(_recipe.Category==MaterialCategory.Fluid?MaterialAdapterMode.AnimatedWater:MaterialAdapterMode.UniformFloor); if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{if(_reference is null)SaveCurrent();var adapter=new MaterialAutotileAdapter();var created=adapter.CreateAutotile(_recipe,_rendered,dialog.Settings);AutotileSerializer.SaveNew(Project,created.Definition,created.Source);created.Source.Dispose();_workspace.MarkProjectDirty();_status.Text=$"Created autotile '{created.Definition.Name}'. Open the Autotile Compiler with F6 to inspect and compile it.";MessageBox.Show(this,"The procedural material was converted into a non-destructive autotile definition.\n\nOpen the Autotile Compiler Workshop (F6) to inspect its corners, torture test, and target slot before compilation.","Autotile Created",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){ShowError("The autotile could not be created.",ex);}
    }

    private void ExportMaterial()
    {
        var buffer=_gallery.SelectedCandidate?.Result.BaseColor??_rendered?.BaseColor;if(buffer is null){RenderGraph();buffer=_rendered?.BaseColor;}if(buffer is null)return;using var dialog=new SaveFileDialog{Filter="PNG image (*.png)|*.png",DefaultExt="png",AddExtension=true,FileName=(_recipe?.Name??"Material")+".png"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;using var bitmap=buffer.ToBitmap();bitmap.Save(dialog.FileName,ImageFormat.Png);_status.Text=$"Exported {Path.GetFileName(dialog.FileName)}";
    }


    private void ExportWangPreview()
    {
        var buffer=_gallery.SelectedCandidate?.Result.BaseColor??_rendered?.BaseColor;if(buffer is null){RenderGraph();buffer=_rendered?.BaseColor;}if(buffer is null)return;using var dialog=new SaveFileDialog{Filter="PNG image (*.png)|*.png",DefaultExt="png",AddExtension=true,FileName=(_recipe?.Name??"Material")+"_WangPreview.png"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;var baker=new WangTileBaker();var set=baker.Bake(buffer,Math.Min(48,Math.Min(buffer.Width,buffer.Height)),2,2,(int)_seed.Value);var preview=baker.RenderPreview(set,10,8,(int)_seed.Value);using var bitmap=preview.ToBitmap();bitmap.Save(dialog.FileName,ImageFormat.Png);_status.Text="Exported a compatible Wang-tile variation preview.";
    }

    private void ImportExemplar()
    {
        using var dialog=new OpenFileDialog{Filter="Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",Title="Import Material Exemplar"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var recipe=new MaterialRecipe{Name=Path.GetFileNameWithoutExtension(dialog.FileName)+" Synthesis",Description="Example-based seamless material synthesis.",Category=MaterialCategory.Custom,Seed=(int)_seed.Value};
        var image=_registry.Create("Source.Image",40,80);image.Parameters["Path"]=new MaterialParameterValue{Kind=MaterialParameterKind.FilePath,Value=dialog.FileName};
        var quilt=_registry.Create("Synthesis.ImageQuilt",280,80);quilt.Inputs["Exemplar"]=new MaterialConnection(image.Id);
        var seam=_registry.Create("Synthesis.SeamRepair",510,80);seam.Inputs["Value"]=new MaterialConnection(quilt.Id);
        var pixel=_registry.Create("PixelArt.Finish",730,80);pixel.Inputs["Value"]=new MaterialConnection(seam.Id);pixel.Parameters["Width"]=MaterialParameterValue.From(48);pixel.Parameters["Height"]=MaterialParameterValue.From(48);
        var output=_registry.Create("Material.Output",950,80);output.Inputs["BaseColor"]=new MaterialConnection(pixel.Id);
        recipe.Graph.Nodes.AddRange(new[]{image,quilt,seam,pixel,output});recipe.Graph.OutputNodeId=output.Id;recipe.Provenance.OriginalFileName=Path.GetFileName(dialog.FileName);recipe.Provenance.SourceDescription="Imported exemplar for quilting and seamless repair.";recipe.ExposedParameters.Add(new(){DisplayName="Patch Size",NodeId=quilt.Id,ParameterName="PatchSize",Minimum=6,Maximum=64,Step=1});recipe.ExposedParameters.Add(new(){DisplayName="Overlap",NodeId=quilt.Id,ParameterName="Overlap",Minimum=1,Maximum=20,Step=1});
        _recipe=recipe;_reference=null;_graphCanvas.Graph=recipe.Graph;ClearResults();_status.Text="Imported an exemplar synthesis graph. Render it, adjust quilting, then save.";
    }

    private void SelectReference(MaterialAssetReference? reference)
    {
        if(reference is null||_loading)return;try{_reference=reference;_recipe=MaterialSerializer.Load(Project,reference);_seed.Value=Math.Clamp(_recipe.Seed,(int)_seed.Minimum,(int)_seed.Maximum);_graphCanvas.Graph=_recipe.Graph;ClearResults();RenderGraph();_status.Text=$"Loaded {_recipe.Name}.";}catch(Exception ex){ShowError("The material recipe could not be loaded.",ex);}
    }
    private void RefreshAssets(Guid? select=null){_loading=true;try{_assets.Items.Clear();foreach(var r in Project.Materials.OrderBy(x=>x.Name))_assets.Items.Add(r);if(select is Guid id)for(var i=0;i<_assets.Items.Count;i++)if(((MaterialAssetReference)_assets.Items[i]).Id==id){_assets.SelectedIndex=i;break;}}finally{_loading=false;}}
    private void DuplicateCurrent(){if(_recipe is null)return;var clone=MaterialSerializer.DeepClone(_recipe);clone.Id=Guid.NewGuid();clone.Name+=" Copy";clone.Revision=1;_recipe=clone;_reference=null;_graphCanvas.Graph=clone.Graph;SaveCurrent();}
    private void DeleteCurrent(){if(_reference is null)return;if(MessageBox.Show(this,$"Delete '{_reference.Name}' and its stored files?","Delete Material",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;MaterialSerializer.DeleteFiles(Project,_reference);Project.Materials.Remove(_reference);_workspace.MarkProjectDirty();_reference=null;_recipe=null;RefreshAssets();CreateFromPreset();}

    private void AddSelectedNode(){if(_recipe is null||_nodePalette.SelectedItem is not IMaterialNodeDefinition definition)return;var node=_registry.Create(definition.TypeId,100+_recipe.Graph.Nodes.Count*18,100+_recipe.Graph.Nodes.Count*12);_recipe.Graph.Nodes.Add(node);_graphCanvas.SelectNode(node.Id);MarkRecipeChanged();}
    private void DeleteSelectedNode(){if(_recipe is null||_graphCanvas.SelectedNode is not { } node)return;if(node.Id==_recipe.Graph.OutputNodeId){MessageBox.Show(this,"The output node cannot be deleted.");return;}_recipe.Graph.Nodes.Remove(node);foreach(var n in _recipe.Graph.Nodes)foreach(var key in n.Inputs.Where(x=>x.Value.SourceNodeId==node.Id).Select(x=>x.Key).ToArray())n.Inputs.Remove(key);_graphCanvas.SelectNode(null);MarkRecipeChanged();}
    private void DuplicateSelectedNode(){if(_recipe is null||_graphCanvas.SelectedNode is not { } node)return;var clone=node.DeepClone();clone.Id=Guid.NewGuid();clone.Name+=" Copy";clone.EditorX+=35;clone.EditorY+=35;clone.Inputs.Clear();_recipe.Graph.Nodes.Add(clone);_graphCanvas.SelectNode(clone.Id);MarkRecipeChanged();}
    private void AutoLayout(){if(_recipe is null)return;var levels=ComputeLevels(_recipe.Graph);foreach(var group in levels.GroupBy(x=>x.Value)){var row=0;foreach(var item in group){var node=_recipe.Graph.Find(item.Key)!;node.EditorX=50+group.Key*230;node.EditorY=50+row++*120;}}_graphCanvas.Invalidate();}
    private static Dictionary<Guid,int> ComputeLevels(MaterialGraph graph){var cache=new Dictionary<Guid,int>();int Level(Guid id){if(cache.TryGetValue(id,out var v))return v;var n=graph.Find(id);v=n is null||n.Inputs.Count==0?0:n.Inputs.Values.Max(x=>Level(x.SourceNodeId))+1;cache[id]=v;return v;}foreach(var n in graph.Nodes)Level(n.Id);return cache;}

    private void PopulateNodeEditor()
    {
        _loading=true;try{_parameterGrid.Rows.Clear();_inputGrid.Rows.Clear();var node=_graphCanvas.SelectedNode;if(node is null)return;foreach(var pair in node.Parameters.OrderBy(x=>x.Key))_parameterGrid.Rows.Add(pair.Key,pair.Value.Value);var definition=_registry.Get(node.TypeId);var choices=new List<string>{"<none>"};choices.AddRange(_recipe!.Graph.Nodes.Where(x=>x.Id!=node.Id).Select(x=>$"{x.Name} [{x.Id}]"));if(_inputGrid.Columns[1] is DataGridViewComboBoxColumn combo){combo.Items.Clear();combo.Items.AddRange(choices.Cast<object>().ToArray());}foreach(var port in definition.Inputs){var value="<none>";if(node.Inputs.TryGetValue(port.Name,out var connection)&&_recipe.Graph.Find(connection.SourceNodeId) is { } source)value=$"{source.Name} [{source.Id}]";_inputGrid.Rows.Add(port.Name,value);}}finally{_loading=false;}
    }
    private void ApplyParameterEdit(int row){if(_loading||_graphCanvas.SelectedNode is not { } node||row<0||row>=_parameterGrid.Rows.Count)return;var name=_parameterGrid.Rows[row].Cells[0].Value?.ToString();var value=_parameterGrid.Rows[row].Cells[1].Value?.ToString()??string.Empty;if(name is not null&&node.Parameters.TryGetValue(name,out var parameter)){parameter.Value=value;MarkRecipeChanged();}}
    private void ApplyConnectionEdit(int row){if(_loading||_graphCanvas.SelectedNode is not { } node||row<0||row>=_inputGrid.Rows.Count||_recipe is null)return;var input=_inputGrid.Rows[row].Cells[0].Value?.ToString();var sourceText=_inputGrid.Rows[row].Cells[1].Value?.ToString();if(string.IsNullOrWhiteSpace(input))return;if(string.IsNullOrWhiteSpace(sourceText)||sourceText=="<none>"){node.Inputs.Remove(input);MarkRecipeChanged();return;}var start=sourceText.LastIndexOf('[');var end=sourceText.LastIndexOf(']');if(start>=0&&end>start&&Guid.TryParse(sourceText.Substring(start+1,end-start-1),out var id)){node.Inputs[input]=new MaterialConnection(id);MarkRecipeChanged();}}

    private void RefreshIssues(MaterialResult? result){_issues.Items.Clear();if(result is null)return;foreach(var issue in _validator.Validate(result)){var item=new ListViewItem(issue.Severity.ToString());item.SubItems.Add(issue.Code);item.SubItems.Add(issue.Message);_issues.Items.Add(item);}}
    private void MarkRecipeChanged(){if(_recipe is null)return;_recipe.ModifiedUtc=DateTimeOffset.UtcNow;_graphCanvas.Invalidate();_workspace.MarkProjectDirty();}
    private void ClearResults(){DisposeCandidates();_rendered?.Dispose();_rendered=null;_preview.Buffer=null;_issues.Items.Clear();}
    private void DisposeCandidates(){foreach(var c in _candidates)c.Dispose();_candidates.Clear();_gallery.SetCandidates(Array.Empty<MaterialCandidate>());}
    private void CancelGeneration(){_generationCts?.Cancel();}
    private string ResolveAsset(string path)=>Path.IsPathRooted(path)?path:ProjectPath.Resolve(Project,path);
    private void SelectRandomPreset(){if(_preset.Items.Count>0)_preset.SelectedIndex=Random.Shared.Next(_preset.Items.Count);CreateFromPreset();}
    private void ResizeGallery(){_gallery.Width=Math.Max(420,_gallery.Parent?.ClientSize.Width-20??700);_gallery.Height=_gallery.GetPreferredSize(new Size(_gallery.Width,0)).Height;}

    private static ToolStripButton Button(string text,EventHandler handler){var button=new ToolStripButton(text);button.Click+=handler;return button;}
    private static void ConfigureNumeric(NumericUpDown box,decimal min,decimal max,decimal value,int width){box.Minimum=min;box.Maximum=max;box.Value=value;box.Width=width;box.ThousandsSeparator=true;}
    private static void AddLabeled(FlowLayoutPanel flow,string label,Control control,int width=0){var panel=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(4)};panel.Controls.Add(new Label{Text=label,AutoSize=true});if(width>0)control.Width=width;panel.Controls.Add(control);flow.Controls.Add(panel);}
    private static void ConfigureGrid(DataGridView grid){grid.Dock=DockStyle.Fill;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.SelectionMode=DataGridViewSelectionMode.CellSelect;}
    private void ShowError(string message,Exception ex)=>MessageBox.Show(this,$"{message}\n\n{ex.Message}","Playroma Tile",MessageBoxButtons.OK,MessageBoxIcon.Error);
}

internal sealed class MaterialAutotileSettingsDialog : Form
{
    private readonly ComboBox _mode=new(); private readonly NumericUpDown _darken=new(); private readonly NumericUpDown _width=new(); private readonly CheckBox _preserve=new(){Text="Preserve material alpha",AutoSize=true}; private readonly CheckBox _overlay=new(){Text="Transparent overlay",AutoSize=true};
    public MaterialAutotileSettings Settings=>new(){Mode=(MaterialAdapterMode)_mode.SelectedItem!,BoundaryDarkening=(int)_darken.Value,BoundaryWidth=(int)_width.Value,PreserveAlpha=_preserve.Checked,IsOverlay=_overlay.Checked};
    public MaterialAutotileSettingsDialog(MaterialAdapterMode initial)
    {
        Text="Create Autotile from Material";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MinimizeBox=false;MaximizeBox=false;Width=420;Height=270;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=5,Padding=new Padding(12)};root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        _mode.DropDownStyle=ComboBoxStyle.DropDownList;_mode.Items.AddRange(Enum.GetValues<MaterialAdapterMode>().Cast<object>().ToArray());_mode.SelectedItem=initial;_darken.Minimum=0;_darken.Maximum=180;_darken.Value=28;_width.Minimum=0;_width.Maximum=6;_width.Value=2;
        root.Controls.Add(new Label{Text="Conversion mode",AutoSize=true},0,0);root.Controls.Add(_mode,1,0);root.Controls.Add(new Label{Text="Edge darkening",AutoSize=true},0,1);root.Controls.Add(_darken,1,1);root.Controls.Add(new Label{Text="Edge width",AutoSize=true},0,2);root.Controls.Add(_width,1,2);root.Controls.Add(_preserve,1,3);root.Controls.Add(_overlay,1,4);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};var ok=new Button{Text="Create",DialogResult=DialogResult.OK,AutoSize=true};var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(ok);buttons.Controls.Add(cancel);Controls.Add(root);Controls.Add(buttons);AcceptButton=ok;CancelButton=cancel;
    }
}
