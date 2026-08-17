using Playroma.Tile.Editor.WinForms.Integration;
using Playroma.Tile.Tilesets.Integration;

namespace Playroma.Tile.Editor.WinForms;

public sealed partial class MainForm
{
    private readonly TileEditorIntegrationOptions? _integration;
    private readonly MvDatabaseTilesetService _databaseService = new();
    private bool _integratedWorkspaceInitialized;
    private bool _hostClosePrepared;

    public bool HasUnsavedChanges => _workspace.HasUnsavedChanges;

    public bool PrepareForHostClose()
    {
        if (_hostClosePrepared) return true;
        _hostClosePrepared = ConfirmDiscardOrSave();
        return _hostClosePrepared;
    }

    public void SaveIntegratedProject() => SaveProject();

    private bool IsIntegrated => _integration is not null;

    private void InitializeIntegratedWorkspace()
    {
        if (_integratedWorkspaceInitialized || _integration is null) return;
        _integratedWorkspaceInitialized = true;
        try
        {
            var authoringPath = Path.GetFullPath(_integration.DefaultAuthoringProjectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(authoringPath)!);
            var created = !File.Exists(authoringPath);
            if (created)
                _workspace.CreateNew(authoringPath, _integration.RpgMakerProjectTitle + " Tiles");
            else
                _workspace.Open(authoringPath);

            ResetCurrentSheet();
            RefreshProjectTree();
            if (created && _integration.ImportDatabaseOnFirstOpen)
            {
                var inspection = _databaseService.Inspect(_integration.RpgMakerProjectRoot);
                var imported = _databaseService.ImportRecords(
                    _workspace.Project!,
                    _integration.RpgMakerProjectRoot,
                    inspection.Records.Select(x => x.Id),
                    replaceLinkedTilesets: true);
                _workspace.MarkProjectDirty();
                _workspace.Save();
                RefreshProjectTree(imported.ImportedTileSets.SelectMany(x => x.Sheets).FirstOrDefault()?.Id);
                _statusLabel.Text = $"Loaded {imported.ImportedTileSets.Count} database tileset(s) into the new authoring project.";
                ShowDatabaseWarnings(imported.Warnings, "Initial Database Load");
            }
            else
            {
                _statusLabel.Text = $"Connected to {_integration.RpgMakerProjectTitle}.";
            }

            if (_integration.InitialImportFiles is { Count: > 0 })
            {
                string[] files = _integration.InitialImportFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length > 0)
                {
                    var imported = _workspace.ImportFiles(files);
                    _workspace.Save();
                    RefreshProjectTree(imported.ImportedSheets.FirstOrDefault()?.Id);
                    _statusLabel.Text = $"Imported {imported.ImportedSheets.Count} sprite bridge sheet(s).";
                }
            }

            ValidateProject(saveFirst: false);
            UpdateWindowTitle();
            UpdateUiState();
        }
        catch (Exception exception)
        {
            ShowError("Playroma Tile could not connect to the jRPG tileset database.", exception);
        }
    }

    private void RequestExit()
    {
        if (_integration?.RequestHostClose is { } callback) callback();
        else Close();
    }

    private void LoadFromDatabaseInteractive()
    {
        if (_integration is null || _workspace.Project is null) return;
        try
        {
            var inspection = _databaseService.Inspect(_integration.RpgMakerProjectRoot);
            using var dialog = new MvDatabaseImportDialog(inspection);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedRecordIds.Count == 0) return;

            _workspace.Save();
            _workspace.CloseSheetSessions();
            ResetCurrentSheet();
            var result = _databaseService.ImportRecords(
                _workspace.Project,
                _integration.RpgMakerProjectRoot,
                dialog.SelectedRecordIds,
                dialog.ReplaceLinkedTilesets);
            _workspace.MarkProjectDirty();
            _workspace.Save();
            RefreshProjectTree(result.ImportedTileSets.SelectMany(x => x.Sheets).FirstOrDefault()?.Id);
            ValidateProject(saveFirst: false);
            _statusLabel.Text = $"Loaded {result.ImportedTileSets.Count} tileset record(s) from data/Tilesets.json.";
            ShowDatabaseWarnings(result.Warnings, "Database Import Complete");
        }
        catch (Exception exception)
        {
            ShowError("Tilesets could not be loaded from the jRPG database.", exception);
        }
    }

    private void RefreshLinkedDatabaseTilesets()
    {
        if (_integration is null || _workspace.Project is null) return;
        var linked = _workspace.Project.TileSets
            .Where(x => x.MvDatabaseRecordId.HasValue)
            .Select(x => x.MvDatabaseRecordId!.Value)
            .Distinct()
            .ToArray();
        if (linked.Length == 0)
        {
            MessageBox.Show(this, "This authoring project has no tilesets linked to database records yet.",
                "Refresh Database Tilesets", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _workspace.Save();
            _workspace.CloseSheetSessions();
            ResetCurrentSheet();
            var result = _databaseService.ImportRecords(
                _workspace.Project,
                _integration.RpgMakerProjectRoot,
                linked,
                replaceLinkedTilesets: true);
            _workspace.MarkProjectDirty();
            _workspace.Save();
            RefreshProjectTree(result.ImportedTileSets.SelectMany(x => x.Sheets).FirstOrDefault()?.Id);
            ValidateProject(saveFirst: false);
            _statusLabel.Text = $"Refreshed {result.ImportedTileSets.Count} linked tileset(s) from the database.";
            ShowDatabaseWarnings(result.Warnings, "Database Refresh Complete");
        }
        catch (Exception exception)
        {
            ShowError("Linked tilesets could not be refreshed from the jRPG database.", exception);
        }
    }

    private void ExportSelectedToDatabase()
    {
        if (_integration is null || _workspace.Project is null || _currentTileSet is null)
        {
            MessageBox.Show(this, "Select a tileset or one of its sheets first.", "Export to Database",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ExportTileSetToDatabase(_currentTileSet, askForTarget: true, showCompletion: true);
    }

    private bool ExportTileSetToDatabase(Playroma.Tile.Core.Models.TileSetDefinition tileSet, bool askForTarget, bool showCompletion)
    {
        if (_integration is null || _workspace.Project is null) return false;
        try
        {
            _workspace.Save();
            var inspection = _databaseService.Inspect(_integration.RpgMakerProjectRoot);
            int? recordId = tileSet.MvDatabaseRecordId;
            var createNew = recordId is null;
            var overwrite = true;
            if (askForTarget)
            {
                using var dialog = new MvDatabaseExportDialog(inspection, tileSet.MvDatabaseRecordId, tileSet.Name);
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                recordId = dialog.RecordId;
                createNew = dialog.CreateNewRecord;
                overwrite = dialog.OverwriteImages;
            }

            var result = _databaseService.ExportTileSet(
                _workspace.Project,
                tileSet,
                _integration.RpgMakerProjectRoot,
                recordId,
                createNew,
                overwrite);
            _workspace.MarkProjectDirty();
            _workspace.Save();
            RefreshProjectTree(_currentSheet?.Id);
            _statusLabel.Text = $"Exported {tileSet.Name} to database record {result.RecordId}.";
            if (showCompletion)
            {
                MessageBox.Show(this,
                    $"Tileset exported to the jRPG database.\n\nRecord: {result.RecordId}\nImages: {result.CopiedImages.Count}\nBackup: {result.BackupDirectory}",
                    "Database Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception exception)
        {
            ShowError("The tileset could not be exported to the jRPG database.", exception);
            return false;
        }
    }

    private void ExportAllToDatabase()
    {
        if (_integration is null || _workspace.Project is null || _workspace.Project.TileSets.Count == 0) return;
        var unlinked = _workspace.Project.TileSets.Where(x => !x.MvDatabaseRecordId.HasValue).ToArray();
        var text = unlinked.Length == 0
            ? $"Export all {_workspace.Project.TileSets.Count} tilesets to their linked database records?"
            : $"Export all {_workspace.Project.TileSets.Count} tilesets? {unlinked.Length} unlinked tileset(s) will be added as new records.";
        if (MessageBox.Show(this, text, "Export All to Database", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;

        var completed = 0;
        foreach (var tileSet in _workspace.Project.TileSets.ToArray())
        {
            if (!ExportTileSetToDatabase(tileSet, askForTarget: false, showCompletion: false)) break;
            completed++;
        }
        _statusLabel.Text = $"Exported {completed} tileset(s) to the jRPG database.";
        MessageBox.Show(this,
            $"Exported {completed} tileset(s) to data/Tilesets.json and img/tilesets.\n\nA backup was created before each database update.",
            "Database Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowDatabaseWarnings(IReadOnlyCollection<string> warnings, string title)
    {
        if (warnings.Count == 0) return;
        var text = string.Join(Environment.NewLine, warnings.Take(20).Select(x => "• " + x));
        if (warnings.Count > 20) text += $"\n\n{warnings.Count - 20} additional warning(s) were omitted.";
        MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
