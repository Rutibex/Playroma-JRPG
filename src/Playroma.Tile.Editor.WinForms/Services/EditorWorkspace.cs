using Playroma.Tile.Core.Models;
using Playroma.Tile.Core.Services;
using Playroma.Tile.RpgMakerMV.Import;

namespace Playroma.Tile.Editor.WinForms.Services;

public sealed class EditorWorkspace : IDisposable
{
    private readonly Dictionary<Guid, SheetSession> _sessions = new();
    private readonly MvImportService _importService = new();

    public TileProject? Project { get; private set; }
    public bool IsProjectDirty { get; private set; }
    public bool HasUnsavedChanges => IsProjectDirty || _sessions.Values.Any(session => session.IsImageDirty);

    public TileProject CreateNew(string projectFilePath, string name)
    {
        Close();
        Project = ProjectSerializer.CreateNew(projectFilePath, name);
        IsProjectDirty = false;
        return Project;
    }

    public TileProject Open(string projectFilePath)
    {
        Close();
        Project = ProjectSerializer.Load(projectFilePath);
        IsProjectDirty = false;
        return Project;
    }

    public SheetSession OpenSheet(TileSheetDefinition sheet)
    {
        EnsureProject();
        if (_sessions.TryGetValue(sheet.Id, out var existing))
        {
            return existing;
        }

        var imagePath = ProjectPath.Resolve(Project!, sheet.RelativeImagePath);
        var created = new SheetSession(sheet, imagePath);
        _sessions.Add(sheet.Id, created);
        return created;
    }

    public MvImportResult ImportFolder(string folderPath)
    {
        EnsureProject();
        var result = _importService.ImportFolder(Project!, folderPath);
        if (result.ImportedSheets.Count > 0)
        {
            IsProjectDirty = true;
        }

        return result;
    }

    public MvImportResult ImportFiles(IEnumerable<string> paths)
    {
        EnsureProject();
        var result = _importService.ImportFiles(Project!, paths);
        if (result.ImportedSheets.Count > 0)
        {
            IsProjectDirty = true;
        }

        return result;
    }

    public MvImportResult ImportArchive(string archivePath)
    {
        EnsureProject();
        var result = _importService.ImportArchive(Project!, archivePath);
        if (result.ImportedSheets.Count > 0)
        {
            IsProjectDirty = true;
        }

        return result;
    }

    public void MarkProjectDirty() => IsProjectDirty = true;

    public void Save()
    {
        EnsureProject();
        foreach (var session in _sessions.Values.Where(session => session.IsImageDirty))
        {
            var imagePath = ProjectPath.Resolve(Project!, session.Definition.RelativeImagePath);
            session.Save(imagePath);
        }

        ProjectSerializer.Save(Project!);
        IsProjectDirty = false;
    }

    public void SaveAs(string newProjectFilePath)
    {
        EnsureProject();
        Save();

        var oldProjectDirectory = Project!.ProjectDirectory;
        var newFullPath = Path.GetFullPath(newProjectFilePath);
        var newProjectDirectory = Path.GetDirectoryName(newFullPath)!;
        Directory.CreateDirectory(newProjectDirectory);

        if (!string.Equals(
                Path.GetFullPath(oldProjectDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(newProjectDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Sheets"), Path.Combine(newProjectDirectory, "Sheets"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Catalogues"), Path.Combine(newProjectDirectory, "Catalogues"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Autotiles"), Path.Combine(newProjectDirectory, "Autotiles"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Materials"), Path.Combine(newProjectDirectory, "Materials"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Tilesets"), Path.Combine(newProjectDirectory, "Tilesets"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Objects"), Path.Combine(newProjectDirectory, "Objects"));
            CopyDirectoryIfPresent(Path.Combine(oldProjectDirectory, "Snapshots"), Path.Combine(newProjectDirectory, "Snapshots"));
        }

        Project.ProjectFilePath = newFullPath;
        foreach (var session in _sessions.Values)
        {
            if (session.IsImageDirty)
            {
                var destination = ProjectPath.Resolve(Project, session.Definition.RelativeImagePath);
                session.Save(destination);
            }
        }

        ProjectSerializer.Save(Project);
        IsProjectDirty = false;
    }

    public void CloseSheetSessions()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    public void Close()
    {
        CloseSheetSessions();
        Project = null;
        IsProjectDirty = false;
    }

    public void Dispose() => Close();

    private void EnsureProject()
    {
        if (Project is null)
        {
            throw new InvalidOperationException("No project is open.");
        }
    }

    private static void CopyDirectoryIfPresent(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToArray();
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
