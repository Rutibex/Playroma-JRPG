using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.WinForms.Assets;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed record AssetPickerRequest(
    IReadOnlySet<AssetCategory> AllowedCategories,
    AssetKey? CurrentSelection = null,
    bool AllowNone = false,
    bool ShowMissingAssets = true,
    AssetMediaKind? MediaKind = null,
    string Title = "Choose Asset");

/// <summary>Reusable asset picker for Phase 8+ database and event editors.</summary>
public sealed class AssetPickerDialog : Form
{
    private readonly MvProjectSession _session;
    private readonly AssetPickerRequest _request;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search assets…" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly AssetPreviewControl _preview;
    private readonly Button _ok = new() { Text = "Choose", DialogResult = DialogResult.OK, AutoSize = true };
    private IReadOnlyList<AssetRecord> _visible = [];

    public AssetPickerDialog(MvProjectSession session, AssetPickerRequest request)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _preview = new AssetPreviewControl(session);
        Text = request.Title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(980, 680);
        Padding = new Padding(8);

        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 300, 220, 360);
        Panel left = new() { Dock = DockStyle.Fill };
        left.Controls.Add(_list);
        left.Controls.Add(_search);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_preview);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([_ok, cancel]);
        if (request.AllowNone)
        {
            Button none = new() { Text = "None", AutoSize = true };
            none.Click += (_, _) => { SelectedAsset = null; DialogResult = DialogResult.OK; Close(); };
            buttons.Controls.Add(none);
        }
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = _ok;
        CancelButton = cancel;

        _search.TextChanged += (_, _) => Rebuild();
        _list.SelectedIndexChanged += async (_, _) => await ShowSelectedAsync();
        _list.DoubleClick += (_, _) => { if (_list.SelectedIndex >= 0) { SelectedAsset = _visible[_list.SelectedIndex].Key; DialogResult = DialogResult.OK; Close(); } };
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && SelectedAsset is null && !request.AllowNone && _list.SelectedIndex < 0)
            {
                e.Cancel = true;
            }
            else if (DialogResult == DialogResult.OK && SelectedAsset is null && _list.SelectedIndex >= 0)
            {
                SelectedAsset = _visible[_list.SelectedIndex].Key;
            }
        };
        Rebuild();
    }

    public AssetKey? SelectedAsset { get; private set; }

    private void Rebuild()
    {
        AssetQuery query = AssetQueryParser.Parse(_search.Text);
        _visible = _session.Assets.Search(query)
            .Where(asset => (_request.AllowedCategories.Count == 0 || _request.AllowedCategories.Contains(asset.Key.Category))
                && (!_request.MediaKind.HasValue || asset.MediaKind == _request.MediaKind.Value)
                && (_request.ShowMissingAssets || !asset.IsMissing))
            .ToArray();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (AssetRecord asset in _visible) _list.Items.Add(new AssetListItem(asset));
        _list.EndUpdate();
        int current = _request.CurrentSelection.HasValue
            ? _visible.ToList().FindIndex(asset => asset.Key.Equals(_request.CurrentSelection.Value))
            : -1;
        if (current >= 0) _list.SelectedIndex = current;
        _ok.Enabled = _list.SelectedIndex >= 0;
    }

    private async Task ShowSelectedAsync()
    {
        _ok.Enabled = _list.SelectedIndex >= 0;
        await _preview.ShowAssetAsync(_list.SelectedIndex >= 0 ? _visible[_list.SelectedIndex] : null);
    }

    private sealed record AssetListItem(AssetRecord Asset)
    {
        public override string ToString() => $"{Asset.Key.LogicalName}   [{Asset.Key.Category}]   {Asset.Status}";
    }
}
