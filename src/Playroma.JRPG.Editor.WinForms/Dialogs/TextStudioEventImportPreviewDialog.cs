using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.Text.Core.Generation;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

internal sealed class TextStudioEventImportPreviewDialog : Form
{
    private readonly TextStudioEventBinding _binding;
    private readonly GeneratedTextBundle _bundle;
    private readonly TextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 10) };
    private readonly TextBox _raw = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly ListBox _issues = new() { Dock = DockStyle.Fill };
    private readonly Button _apply = new() { Text = "Import", AutoSize = true, DialogResult = DialogResult.OK };

    public TextStudioEventImportPreviewDialog(TextStudioEventBinding binding, GeneratedTextBundle bundle)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        Text = $"Import Generated {_binding.Kind} — Preview";
        MinimumSize = new Size(760, 560); Size = new Size(980, 720); StartPosition = FormStartPosition.CenterParent;
        Build();
        Recompile();
    }

    public MvEventTextPlan? Plan { get; private set; }

    private void Build()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("MV Preview") { Controls = { _preview } });
        tabs.TabPages.Add(new TabPage("Raw Commands") { Controls = { _raw } });
        tabs.TabPages.Add(new TabPage("Validation") { Controls = { _issues } });
        FlowLayoutPanel footer = new() { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.AddRange([_apply, cancel]);
        Controls.Add(tabs); Controls.Add(footer); AcceptButton = _apply; CancelButton = cancel;
    }

    private void Recompile()
    {
        Plan = _binding.Kind switch
        {
            MvGeneratedEventTextKind.ShowText => MvEventTextCompiler.CompileShowText(_bundle.PrimaryText, _binding.TargetIndent, _binding.ShowTextSettings),
            MvGeneratedEventTextKind.ScrollingText => MvEventTextCompiler.CompileScrollingText(_bundle.PrimaryText, _binding.TargetIndent, _binding.ScrollingSettings),
            MvGeneratedEventTextKind.Comment => MvEventTextCompiler.CompileComment(_bundle.PrimaryText, _binding.TargetIndent),
            MvGeneratedEventTextKind.ShowChoices => MvEventTextCompiler.CompileChoices(MvEventTextCompiler.ExtractChoices(_bundle), _binding.TargetIndent, _binding.ChoiceSettings, _binding.ShowTextSettings),
            MvGeneratedEventTextKind.BranchingDialogue => MvEventTextCompiler.CompileBranchingDialogue(_bundle.PrimaryText, MvEventTextCompiler.ExtractChoices(_bundle), _binding.TargetIndent, _binding.ShowTextSettings, _binding.ChoiceSettings),
            _ => MvEventTextCompiler.CompileBundle(_bundle, _binding.Kind, _binding.TargetIndent)
        };
        if (_binding.Kind == MvGeneratedEventTextKind.Comment && _binding.ReplaceExisting
            && MvTextSafety.LooksLikePluginTag(_binding.Target.SelectedField?.CurrentValue ?? string.Empty))
        {
            Plan = Plan with { Issues = Plan.Issues.Concat([new TextImportValidationIssue("PTXTMV0905", "Replacing this Event Comment could remove plugin/YEP tags. Insert generated prose separately or edit the tagged comment deliberately.", true, "text")]).ToArray() };
        }
        if (_binding.Kind == MvGeneratedEventTextKind.ShowChoices && _binding.ReplaceExisting && _binding.HasNonTextChoiceCommands)
        {
            Plan = Plan with { Issues = Plan.Issues.Concat([new TextImportValidationIssue("PTXTMV0906", "This Show Choices block contains non-text gameplay commands inside one or more branches. Whole-block generated replacement is blocked so switches, variables, transfers, battles, scripts, or other branch logic cannot be erased. Insert a new generated choice block or edit the existing branch text manually.", true, "choices")]).ToArray() };
        }
        _preview.Text = Plan.PreviewText;
        _raw.Text = string.Join(Environment.NewLine, Plan.RawCommands);
        _issues.Items.Clear();
        foreach (TextImportValidationIssue issue in Plan.Issues)
            _issues.Items.Add((issue.IsError ? "ERROR: " : "Warning: ") + issue.Message);
        if (Plan.Issues.Count == 0) _issues.Items.Add("No validation issues.");
        _apply.Enabled = Plan.CanApply;
    }
}
