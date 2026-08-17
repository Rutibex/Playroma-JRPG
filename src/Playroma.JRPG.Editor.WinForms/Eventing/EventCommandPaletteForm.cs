using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>
/// RPG Maker MV-style three-page event-command selector with deterministic grouping,
/// Playroma search, favorites, recent commands, and complete keyboard navigation.
/// This transient owned dialog never mutates an event itself.
/// </summary>
public class EventCommandPaletteForm : Form
{
    private readonly EventCommandRegistry _registry;
    private readonly EventCommandPaletteLayout _layout;
    private readonly EventCommandUsageService _usage;
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Search event commands…",
        AccessibleName = "Search event commands",
        AccessibleDescription = "Type part of a command name or category. Press Enter to choose the selected result."
    };
    private readonly TabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Event command pages",
        AccessibleDescription = "Pages 1 through 3 follow RPG Maker MV command grouping. Favorites and Recent are Playroma enhancements."
    };
    private readonly Panel _searchHost = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ListBox _searchResults = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        AccessibleName = "Event command search results",
        AccessibleRole = AccessibleRole.List
    };
    private readonly ToolTip _toolTip = new();
    private readonly Button _insert = new()
    {
        Text = "&Insert Selected",
        AutoSize = true,
        MinimumSize = new Size(112, 30),
        AccessibleDescription = "Insert the selected event command and open its parameter editor."
    };
    private readonly Button _cancel = new()
    {
        Text = "&Cancel",
        AutoSize = true,
        MinimumSize = new Size(88, 30),
        DialogResult = DialogResult.Cancel
    };
    private readonly Dictionary<int, List<Button>> _commandButtons = [];
    private IReadOnlyList<EventCommandDefinition> _visibleSearch = [];

    public EventCommandPaletteForm(
        EventCommandRegistry registry,
        EventCommandUsageService? usage = null,
        EventCommandPaletteLayout? layout = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _usage = usage ?? new EventCommandUsageService();
        _layout = layout ?? EventCommandPaletteLayout.CreateRpgMakerMv();

        Name = nameof(EventCommandPaletteForm);
        Text = "Event Commands";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 620);
        Size = new Size(980, 760);
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AccessibleName = "Event Commands";
        AccessibleDescription = "Choose an event command using grouped pages, search, favorites, or recent commands.";

        BuildLayout();
        RebuildTabs();
        WireEvents();
        AcceptButton = _insert;
        CancelButton = _cancel;
        Shown += (_, _) =>
        {
            AccessibilityMetadata.ApplyFormDefaults(this);
            FocusInitialControl();
        };
    }

    public EventCommandDefinition? SelectedDefinition { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _search.Focus();
            _search.SelectAll();
            return true;
        }
        if (keyData is (Keys.Control | Keys.D1) or (Keys.Control | Keys.NumPad1)) return SelectPage(0);
        if (keyData is (Keys.Control | Keys.D2) or (Keys.Control | Keys.NumPad2)) return SelectPage(1);
        if (keyData is (Keys.Control | Keys.D3) or (Keys.Control | Keys.NumPad3)) return SelectPage(2);
        if (keyData == Keys.Escape && _search.TextLength > 0)
        {
            _search.Clear();
            return true;
        }
        if (ActiveControl is Button && keyData is Keys.Left or Keys.Up or Keys.Right or Keys.Down)
        {
            bool forward = keyData is Keys.Right or Keys.Down;
            SelectNextControl(ActiveControl, forward, tabStopOnly: true, nested: true, wrap: true);
            return true;
        }
        if (keyData == Keys.Home && ActiveControl is Button)
        {
            FocusFirstCommand();
            return true;
        }
        if (keyData == Keys.End && ActiveControl is Button)
        {
            FocusLastCommand();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool SelectPage(int index)
    {
        if (_tabs.TabPages.Count <= index) return false;
        _search.Clear();
        _tabs.SelectedIndex = index;
        BeginInvoke(new Action(FocusFirstCommand));
        return true;
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel searchRow = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.Controls.Add(AccessibilityMetadata.CreateLabel("&Search:", _search), 0, 0);
        searchRow.Controls.Add(_search, 1, 0);
        searchRow.Controls.Add(new Label
        {
            Text = "Ctrl+F",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 3, 3),
            AccessibleName = "Search shortcut, Control F"
        }, 2, 0);

        _searchHost.Controls.Add(_searchResults);
        Panel content = new() { Dock = DockStyle.Fill };
        content.Controls.Add(_searchHost);
        content.Controls.Add(_tabs);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.AddRange([_cancel, _insert]);

        root.Controls.Add(searchRow, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private void RebuildTabs()
    {
        int selected = Math.Clamp(_usage.LastSelectedPage, 0, 4);
        _tabs.SuspendLayout();
        try
        {
            _tabs.TabPages.Clear();
            _commandButtons.Clear();
            for (int page = 1; page <= 3; page++) _tabs.TabPages.Add(BuildPalettePage(page));
            _tabs.TabPages.Add(BuildCodeListPage("★ Favorites", _usage.Favorites, "Right-click any command button to add it here."));
            _tabs.TabPages.Add(BuildCodeListPage("Recent", _usage.Recent, "Commands appear here after they are accepted."));
            _tabs.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, _tabs.TabPages.Count - 1));
        }
        finally
        {
            _tabs.ResumeLayout();
        }
    }

    private TabPage BuildPalettePage(int page)
    {
        IReadOnlyList<EventCommandPaletteGroup> groups = _layout.ForPage(page);
        Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        TableLayoutPanel groupGrid = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = (groups.Count + 1) / 2,
            Padding = new Padding(6),
            AccessibleName = $"Event command page {page} groups"
        };
        groupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        groupGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < groupGrid.RowCount; row++) groupGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (int index = 0; index < groups.Count; index++)
        {
            EventCommandPaletteGroup group = groups[index];
            GroupBox box = BuildGroup(group);
            groupGrid.Controls.Add(box, index % 2, index / 2);
        }
        scroll.Controls.Add(groupGrid);
        TabPage tab = new(page.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            AccessibleName = $"Event command page {page}",
            Controls = { scroll }
        };
        return tab;
    }

    private GroupBox BuildGroup(EventCommandPaletteGroup group)
    {
        GroupBox box = new()
        {
            Text = group.Name,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            Margin = new Padding(5),
            AccessibleName = group.Name + " commands"
        };
        EventCommandDefinition[] definitions = group.CommandCodes
            .Select(_registry.Get)
            .Where(definition => definition.IsInsertable)
            .ToArray();
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = Math.Max(1, (definitions.Length + 1) / 2)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < grid.RowCount; row++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int index = 0; index < definitions.Length; index++)
        {
            Button button = CreateCommandButton(definitions[index]);
            grid.Controls.Add(button, index % 2, index / 2);
        }
        box.Controls.Add(grid);
        return box;
    }

    private TabPage BuildCodeListPage(string title, IReadOnlyList<int> codes, string emptyText)
    {
        Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Padding = new Padding(12)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        int count = 0;
        foreach (int code in codes)
        {
            EventCommandDefinition definition = _registry.Get(code);
            if (!definition.IsInsertable) continue;
            panel.Controls.Add(CreateCommandButton(definition), count % 3, count / 3);
            count++;
        }
        if (count == 0)
        {
            Label empty = new()
            {
                Text = emptyText,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(6),
                AccessibleName = emptyText
            };
            panel.Controls.Add(empty, 0, 0);
            panel.SetColumnSpan(empty, 3);
        }
        scroll.Controls.Add(panel);
        return new TabPage(title) { AccessibleName = title, Controls = { scroll } };
    }

    private Button CreateCommandButton(EventCommandDefinition definition)
    {
        Button button = new()
        {
            Text = definition.Name,
            Tag = definition,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(140, 32),
            Margin = new Padding(3),
            Padding = new Padding(6, 2, 6, 2),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            AccessibleName = definition.Name,
            AccessibleDescription = $"{definition.Category} command. {definition.Source}",
            AccessibleRole = AccessibleRole.PushButton,
            UseMnemonic = false,
            FlatStyle = SystemInformation.HighContrast ? FlatStyle.System : FlatStyle.Flat
        };
        if (!SystemInformation.HighContrast)
        {
            button.FlatAppearance.BorderColor = SystemColors.ControlDark;
            button.FlatAppearance.MouseOverBackColor = SystemColors.ControlLight;
            button.FlatAppearance.MouseDownBackColor = SystemColors.Highlight;
        }
        button.Click += (_, _) => Confirm(definition);
        button.Enter += (_, _) => SelectDefinition(definition);
        button.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                Confirm(definition);
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        ContextMenuStrip menu = new();
        ToolStripMenuItem favorite = new(_usage.IsFavorite(definition.Code) ? "Remove from Favorites" : "Add to Favorites");
        favorite.Click += (_, _) =>
        {
            _usage.ToggleFavorite(definition.Code);
            RebuildTabs();
        };
        menu.Items.Add(favorite);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"Command code: {definition.Code}") { Enabled = false });
        button.ContextMenuStrip = menu;
        _toolTip.SetToolTip(button, $"{definition.Category} · Command {definition.Code}\r\n{definition.Source}");
        if (!_commandButtons.TryGetValue(definition.Code, out List<Button>? instances))
        {
            instances = [];
            _commandButtons.Add(definition.Code, instances);
        }
        instances.Add(button);
        return button;
    }

    private void WireEvents()
    {
        _search.TextChanged += (_, _) => RefreshSearch();
        _searchResults.DoubleClick += (_, _) => ConfirmSearch();
        _searchResults.SelectedIndexChanged += (_, _) =>
        {
            _insert.Enabled = _searchResults.SelectedIndex >= 0;
            if (_searchResults.SelectedIndex >= 0 && _searchResults.SelectedIndex < _visibleSearch.Count)
                SelectedDefinition = _visibleSearch[_searchResults.SelectedIndex];
        };
        _searchResults.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ConfirmSearch();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _usage.LastSelectedPage = _tabs.SelectedIndex;
            _usage.Save();
            BeginInvoke(new Action(FocusFirstCommand));
        };
        _insert.Click += (_, _) =>
        {
            if (_searchHost.Visible) ConfirmSearch();
            else if (SelectedDefinition is not null) Confirm(SelectedDefinition);
        };
    }

    private void RefreshSearch()
    {
        string query = _search.Text.Trim();
        _searchHost.Visible = query.Length > 0;
        _tabs.Visible = query.Length == 0;
        if (query.Length == 0)
        {
            _visibleSearch = [];
            _searchResults.Items.Clear();
            _insert.Enabled = SelectedDefinition is not null;
            BeginInvoke(new Action(FocusFirstCommand));
            return;
        }
        _visibleSearch = _registry.Search(query).Where(item => item.IsInsertable).ToArray();
        _searchResults.BeginUpdate();
        try
        {
            _searchResults.Items.Clear();
            foreach (EventCommandDefinition definition in _visibleSearch)
                _searchResults.Items.Add($"{definition.Category,-18}  {definition.Name}");
        }
        finally
        {
            _searchResults.EndUpdate();
        }
        if (_searchResults.Items.Count > 0) _searchResults.SelectedIndex = 0;
        _insert.Enabled = _searchResults.Items.Count > 0;
    }

    private void SelectDefinition(EventCommandDefinition definition)
    {
        SelectedDefinition = definition;
        _insert.Enabled = true;
        foreach (List<Button> buttons in _commandButtons.Values)
        {
            foreach (Button button in buttons)
            {
                button.Font = Font;
                if (!SystemInformation.HighContrast) button.FlatAppearance.BorderSize = 1;
            }
        }
        if (_commandButtons.TryGetValue(definition.Code, out List<Button>? selectedButtons))
        {
            foreach (Button selected in selectedButtons)
            {
                selected.Font = new Font(selected.Font, FontStyle.Bold);
                if (!SystemInformation.HighContrast)
                {
                    selected.FlatAppearance.BorderColor = SystemColors.Highlight;
                    selected.FlatAppearance.BorderSize = 2;
                }
            }
        }
    }

    private void ConfirmSearch()
    {
        if (_searchResults.SelectedIndex < 0 || _searchResults.SelectedIndex >= _visibleSearch.Count) return;
        Confirm(_visibleSearch[_searchResults.SelectedIndex]);
    }

    private void Confirm(EventCommandDefinition definition)
    {
        SelectedDefinition = definition;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void FocusInitialControl()
    {
        if (_searchHost.Visible) _searchResults.Focus();
        else FocusFirstCommand();
    }

    private void FocusFirstCommand()
    {
        Button? button = FindButtons(_tabs.SelectedTab).FirstOrDefault();
        button?.Focus();
    }

    private void FocusLastCommand()
    {
        Button? button = FindButtons(_tabs.SelectedTab).LastOrDefault();
        button?.Focus();
    }

    private static IEnumerable<Button> FindButtons(Control? root)
    {
        if (root is null) yield break;
        foreach (Control child in root.Controls)
        {
            if (child is Button button) yield return button;
            foreach (Button descendant in FindButtons(child)) yield return descendant;
        }
    }
}
