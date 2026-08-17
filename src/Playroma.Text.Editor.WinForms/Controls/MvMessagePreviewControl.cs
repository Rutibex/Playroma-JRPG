using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;
using Playroma.Text.RpgMakerMV;

namespace Playroma.Text.Editor.WinForms.Controls;

/// <summary>Lightweight RPG Maker MV message-window preview used by Text Studio before import.</summary>
public sealed class MvMessagePreviewControl : UserControl
{
    private readonly FlowLayoutPanel _pages = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 36, Padding = new Padding(10, 7, 10, 0), ForeColor = SystemColors.GrayText };

    public MvMessagePreviewControl()
    {
        Dock = DockStyle.Fill;
        Controls.Add(_pages); Controls.Add(_status);
        Inspect(null, null);
    }

    public void Inspect(GeneratedTextBundle? bundle, TextRecordTargetDescriptor? target)
    {
        _pages.SuspendLayout();
        try
        {
            _pages.Controls.Clear();
            if (bundle is null)
            {
                _status.Text = "Select a generated result to preview it as RPG Maker MV message text.";
                return;
            }
            string adapter = target?.AdapterKind ?? string.Empty;
            if (adapter.Contains("showchoices", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<MvGeneratedChoice> choices = MvEventTextCompiler.ExtractChoices(bundle);
                AddMessagePage("Show Choices", choices.Select(x => "▶ " + RenderControls(x.Text)).ToArray());
                _status.Text = choices.Count > MvEventTextCompiler.MaxChoices
                    ? $"Too many choices: {choices.Count}; MV supports {MvEventTextCompiler.MaxChoices}."
                    : $"{choices.Count} choice{(choices.Count == 1 ? string.Empty : "s")} — MV maximum {MvEventTextCompiler.MaxChoices}.";
                return;
            }
            if (adapter.Contains("branchingdialogue", StringComparison.OrdinalIgnoreCase))
            {
                AddPagedText(bundle.PrimaryText, "Prompt");
                IReadOnlyList<MvGeneratedChoice> choices = MvEventTextCompiler.ExtractChoices(bundle);
                AddMessagePage("Choices", choices.Select(x => "▶ " + RenderControls(x.Text)).ToArray());
                for (int i = 0; i < choices.Count; i++)
                    if (!string.IsNullOrWhiteSpace(choices[i].Response)) AddPagedText(choices[i].Response!, $"Branch {i + 1}");
                _status.Text = $"Branching dialogue: {choices.Count} choice branch(es).";
                return;
            }
            if (adapter.Contains("scrollingtext", StringComparison.OrdinalIgnoreCase) || adapter.Contains("comment", StringComparison.OrdinalIgnoreCase))
            {
                AddMessagePage(adapter.Contains("comment", StringComparison.OrdinalIgnoreCase) ? "Event Comment" : "Scrolling Text",
                    bundle.PrimaryText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(RenderControls).ToArray());
                _status.Text = adapter.Contains("comment", StringComparison.OrdinalIgnoreCase) ? "MV Comment preview (108/408)." : "MV Scrolling Text preview (105/405).";
                return;
            }
            AddPagedText(bundle.PrimaryText, "Show Text");
            MvMessageAnalysis analysis = MvMessageSyntax.Analyze(bundle.PrimaryText);
            _status.Text = $"{MvMessagePaginator.Paginate(bundle.PrimaryText).Count} message page(s), {analysis.ControlCodes.Count} MV control code(s)" + (analysis.Warnings.Count > 0 ? $", {analysis.Warnings.Count} warning(s)." : ".");
        }
        finally { _pages.ResumeLayout(true); }
    }

    private void AddPagedText(string text, string label)
    {
        IReadOnlyList<MvMessagePage> pages = MvMessagePaginator.Paginate(text);
        for (int i = 0; i < pages.Count; i++) AddMessagePage($"{label} — page {i + 1}/{pages.Count}", pages[i].Lines.Select(RenderControls).ToArray());
    }

    private void AddMessagePage(string caption, IReadOnlyList<string> lines)
    {
        GroupBox frame = new()
        {
            Text = caption,
            Width = Math.Max(520, _pages.ClientSize.Width - 42),
            Height = 154,
            Padding = new Padding(12),
            Margin = new Padding(3, 3, 3, 12)
        };
        Label message = new()
        {
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 11),
            Text = string.Join(Environment.NewLine, lines.Take(6)),
            AutoEllipsis = true
        };
        frame.Controls.Add(message); _pages.Controls.Add(frame);
    }

    private static string RenderControls(string text)
    {
        text = Regex.Replace(text, @"\\I\[(\d+)\]", "[Icon $1]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\V\[(\d+)\]", "[Variable $1]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\N\[(\d+)\]", "[Actor $1]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\P\[(\d+)\]", "[Party $1]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\G\b", "G", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\\C\[\d+\]|\\[{}$\.\|!><\^]", string.Empty, RegexOptions.IgnoreCase);
        return text.Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
