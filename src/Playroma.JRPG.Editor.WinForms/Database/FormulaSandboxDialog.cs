using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.FormulaSandbox;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>UI for the isolated Phase 9B MV Core Approximation formula worker.</summary>
public sealed class FormulaSandboxDialog : Form
{
    private readonly FormulaItemSnapshot _item;
    private readonly ComboBox _user = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _target = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _critical = new() { Text = "Apply critical multiplier when the record allows it", AutoSize = true };
    private readonly NumericUpDown _samples = new() { Minimum = 1, Maximum = 10000, Value = 250, Dock = DockStyle.Fill };
    private readonly NumericUpDown _seed = new() { Minimum = int.MinValue, Maximum = int.MaxValue, Value = 1, Dock = DockStyle.Fill };
    private readonly TextBox _variables = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CodeTextEditorControl _formula = new() { Dock = DockStyle.Fill };
    private readonly TextBox _results = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly Button _evaluate = new() { Text = "Evaluate in Isolated Worker", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(8, 5, 8, 0), ForeColor = SystemColors.GrayText };

    private FormulaSandboxDialog(string formula, FormulaItemSnapshot item)
    {
        _item = item;
        Text = "Formula Sandbox — MV Core Approximation";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 650);
        Size = new Size(1080, 760);
        _formula.SetLanguage(CodeEditorLanguage.JavaScript);
        _formula.Text = formula;
        _variables.Text = "# One variable per line: ID=value\r\n1=0\r\n31=0";

        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 510, 390, 390);
        split.Panel1.Controls.Add(BuildInputPage());
        split.Panel2.Controls.Add(BuildResultsPage());
        Controls.Add(split);
        Controls.Add(_status);
        _evaluate.Click += async (_, _) => await EvaluateAsync().ConfigureAwait(true);
    }

    public static async Task ShowAsync(
        IWin32Window owner,
        MvProjectSession session,
        string formula,
        FormulaItemSnapshot item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(session);
        FormulaSandboxDialog dialog = new(formula, item);
        try
        {
            IReadOnlyList<FormulaBattlerChoice> choices = await FormulaBattlerPresetLoader.LoadAsync(session, cancellationToken).ConfigureAwait(true);
            foreach (FormulaBattlerChoice choice in choices)
            {
                dialog._user.Items.Add(choice);
                dialog._target.Items.Add(choice);
            }
            if (choices.Count > 0)
            {
                dialog._user.SelectedIndex = 0;
                dialog._target.SelectedIndex = Math.Min(1, choices.Count - 1);
            }
            dialog._status.Text = choices.Count == 0
                ? "No Actor or Enemy presets were available; formulas cannot be evaluated until a battler is selected."
                : $"Loaded {choices.Count:N0} Actor and Enemy presets. This mode does not execute project plugins.";
            dialog.ShowDialog(owner);
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private Control BuildInputPage()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(10) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddRow(table, 0, "User (a)", _user);
        AddRow(table, 1, "Target (b)", _target);
        AddRow(table, 2, "Critical", _critical);
        AddRow(table, 3, "Variance Samples", _samples);
        AddRow(table, 4, "Random Seed", _seed);
        AddRow(table, 5, "Variables v[n]", _variables);
        Label explanation = new()
        {
            Text = "Supported core expressions include battler properties (a.atk, b.def), v[n], item properties, arithmetic, comparisons, ternary expressions, and common Math functions. Arbitrary JavaScript and plugin APIs are intentionally unavailable.",
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            ForeColor = SystemColors.GrayText
        };
        AddRow(table, 6, "Security", explanation);
        table.Controls.Add(new Label { Text = "Formula", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, 0, 7);
        table.Controls.Add(_formula, 1, 7);
        AddRow(table, 8, string.Empty, _evaluate);
        return table;
    }

    private Control BuildResultsPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        Label title = new()
        {
            Text = "Results\r\nMV Core Approximation — plugin modifications are not included",
            Dock = DockStyle.Top,
            Height = 52,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
        };
        panel.Controls.Add(_results);
        panel.Controls.Add(title);
        return panel;
    }

    private async Task EvaluateAsync()
    {
        if (_user.SelectedItem is not FormulaBattlerChoice user || _target.SelectedItem is not FormulaBattlerChoice target)
        {
            MessageBox.Show(this, "Select both a user and a target.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _evaluate.Enabled = false;
            _status.Text = "Starting isolated formula worker…";
            FormulaSandboxRequest request = new(
                _formula.Text,
                user.Snapshot,
                target.Snapshot,
                _item,
                ParseVariables(_variables.Text),
                _critical.Checked,
                decimal.ToInt32(_samples.Value),
                decimal.ToInt32(_seed.Value));
            FormulaSandboxClient sandbox = FormulaSandboxClient.CreateDefault();
            FormulaSandboxResult result = await sandbox.EvaluateAsync(request).ConfigureAwait(true);
            _results.Text = FormatResult(result);
            _status.Text = result.Success
                ? $"Completed in {result.ElapsedMilliseconds:N0} ms."
                : $"Formula failed safely in {result.ElapsedMilliseconds:N0} ms.";
        }
        catch (Exception exception)
        {
            _results.Text = exception.ToString();
            _status.Text = "The formula request failed before execution.";
        }
        finally { _evaluate.Enabled = true; }
    }

    private static IReadOnlyDictionary<int, double> ParseVariables(string text)
    {
        Dictionary<int, double> result = [];
        foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf('=');
            if (separator <= 0
                || !int.TryParse(line[..separator].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || id < 0
                || !double.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"Variable line '{rawLine}' must use ID=value with a nonnegative integer ID.");
            }
            result[id] = value;
        }
        return result;
    }

    private static string FormatResult(FormulaSandboxResult result)
    {
        if (!result.Success) return $"FAILED\r\n\r\n{result.Error}";
        return string.Join("\r\n",
        [
            $"Mode:                 {result.Mode}",
            $"Raw formula result:   {result.RawResult.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Base damage:          {result.BaseDamage.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Critical result:      {result.CriticalDamage.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Variance minimum:     {result.MinimumSample.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Variance average:     {result.AverageSample.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Variance maximum:     {result.MaximumSample.ToString("0.####", CultureInfo.InvariantCulture)}",
            $"Execution time:       {result.ElapsedMilliseconds:N0} ms",
            string.Empty,
            "Referenced properties:",
            result.ReferencedProperties.Count == 0 ? "  (none)" : "  " + string.Join("\r\n  ", result.ReferencedProperties),
            string.Empty,
            "Referenced variables:",
            result.ReferencedVariables.Count == 0 ? "  (none)" : "  " + string.Join(", ", result.ReferencedVariables.Select(id => $"v[{id}]")),
            string.Empty,
            $"Samples retained: {result.Samples.Count:N0}",
            result.Samples.Count == 0 ? string.Empty : string.Join(", ", result.Samples.Take(64).Select(value => value.ToString("0.##", CultureInfo.InvariantCulture)))
        ]);
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        Label caption = new() { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
        control.Margin = new Padding(3, 3, 3, 8);
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(control, 1, row);
    }
}

public sealed record FormulaBattlerChoice(string Category, int Id, FormulaBattlerSnapshot Snapshot)
{
    public override string ToString() => $"{Category} {Id:D4} — {Snapshot.Name}";
}

public static class FormulaBattlerPresetLoader
{
    public static async ValueTask<IReadOnlyList<FormulaBattlerChoice>> LoadAsync(
        MvProjectSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<FormulaBattlerChoice> result = [];
        MutableJsonProjectDocument? classes = await TryLoadAsync(session, MvDocumentKind.Classes, cancellationToken).ConfigureAwait(false);
        MutableJsonProjectDocument? actors = await TryLoadAsync(session, MvDocumentKind.Actors, cancellationToken).ConfigureAwait(false);
        MutableJsonProjectDocument? enemies = await TryLoadAsync(session, MvDocumentKind.Enemies, cancellationToken).ConfigureAwait(false);
        if (actors?.Syntax.Root is JsonArraySyntax actorRoot)
        {
            for (int id = 1; id < actorRoot.Elements.Count; id++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (actorRoot.Elements[id] is not JsonObjectSyntax) continue;
                JsonPath root = JsonPath.Root.Append(id);
                string name = actors.GetString(root.Append("name")) ?? $"Actor {id}";
                int level = Math.Clamp(actors.GetInt32(root.Append("initialLevel")) ?? 1, 1, 99);
                int classId = actors.GetInt32(root.Append("classId")) ?? 0;
                int[] parameters = classes is null ? new int[8] : ReadClassParameters(classes, classId, level);
                result.Add(new FormulaBattlerChoice("Actor", id, CreateSnapshot(name, level, parameters)));
            }
        }
        if (enemies?.Syntax.Root is JsonArraySyntax enemyRoot)
        {
            for (int id = 1; id < enemyRoot.Elements.Count; id++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (enemyRoot.Elements[id] is not JsonObjectSyntax) continue;
                JsonPath root = JsonPath.Root.Append(id);
                string name = enemies.GetString(root.Append("name")) ?? $"Enemy {id}";
                int[] parameters = Phase9BRecordReader.ReadIntArray(enemies, root.Append("params")).Take(8).Concat(Enumerable.Repeat(0, 8)).Take(8).ToArray();
                result.Add(new FormulaBattlerChoice("Enemy", id, CreateSnapshot(name, 1, parameters)));
            }
        }
        return result;
    }

    private static async ValueTask<MutableJsonProjectDocument?> TryLoadAsync(
        MvProjectSession session,
        MvDocumentKind kind,
        CancellationToken cancellationToken)
    {
        ProjectDocumentHandle? handle = session.Workspace.Documents.FirstOrDefault(document => document.Kind == kind);
        if (handle is null) return null;
        IMutableProjectDocument document = await session.Workspace.OpenDocumentAsync(handle.RelativePath, cancellationToken).ConfigureAwait(false);
        return document as MutableJsonProjectDocument;
    }

    private static int[] ReadClassParameters(MutableJsonProjectDocument classes, int classId, int level)
    {
        int[] result = new int[8];
        if (classId <= 0 || classes.Syntax.Resolve(JsonPath.Root.Append(classId)) is not JsonObjectSyntax) return result;
        JsonPath parameters = JsonPath.Root.Append(classId).Append("params");
        for (int parameter = 0; parameter < 8; parameter++)
        {
            result[parameter] = classes.GetInt32(parameters.Append(parameter).Append(level)) ?? 0;
        }
        return result;
    }

    private static FormulaBattlerSnapshot CreateSnapshot(string name, int level, IReadOnlyList<int> parameters)
    {
        double mhp = Get(parameters, 0);
        double mmp = Get(parameters, 1);
        return new FormulaBattlerSnapshot(
            name,
            level,
            mhp,
            mhp,
            mmp,
            mmp,
            0,
            Get(parameters, 2),
            Get(parameters, 3),
            Get(parameters, 4),
            Get(parameters, 5),
            Get(parameters, 6),
            Get(parameters, 7));
    }

    private static int Get(IReadOnlyList<int> values, int index) => index < values.Count ? values[index] : 0;
}
