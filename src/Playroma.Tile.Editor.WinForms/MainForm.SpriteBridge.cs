using System.Text.Json;
using Playroma.Tile.Core.Services;

namespace Playroma.Tile.Editor.WinForms;

public sealed partial class MainForm
{
    private void ReturnCurrentSheetToSpriteStudio()
    {
        if (_integration is null || string.IsNullOrWhiteSpace(_integration.SpriteBridgeReturnDirectory)) return;
        if (_workspace.Project is null || _currentSheet is null)
        {
            MessageBox.Show(this, "Select a sheet to return to Sprite Studio.", "Sprite Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _workspace.Save();
            string source = ProjectPath.Resolve(_workspace.Project, _currentSheet.RelativeImagePath);
            if (!File.Exists(source)) throw new FileNotFoundException("The selected Tile Studio sheet does not exist.", source);
            string directory = Path.GetFullPath(_integration.SpriteBridgeReturnDirectory);
            Directory.CreateDirectory(directory);
            string stem = ProjectPath.SanitizeFileName(string.IsNullOrWhiteSpace(_currentSheet.DisplayName) ? Path.GetFileNameWithoutExtension(source) : _currentSheet.DisplayName);
            string destination = Path.Combine(directory, $"{stem}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
            File.Copy(source, destination, true);
            string response = Path.Combine(directory, "tile-studio-response.json");
            File.WriteAllText(response, JsonSerializer.Serialize(new
            {
                schema = "playroma-sprite-tile-bridge-response/1",
                manifest = _integration.SpriteBridgeManifestPath,
                tileStudioProject = _workspace.Project.ProjectFilePath,
                tileSetId = _currentTileSet?.Id,
                sheetId = _currentSheet.Id,
                imagePath = destination,
                returnedUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            _statusLabel.Text = $"Returned {Path.GetFileName(destination)} to Sprite Studio.";
            MessageBox.Show(this, $"Returned the selected sheet to:\n{destination}", "Sprite Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("The selected sheet could not be returned to Sprite Studio.", exception);
        }
    }
}
