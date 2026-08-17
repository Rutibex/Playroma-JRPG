using System.ComponentModel;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Workspace;

namespace Playroma.Sound.WinForms.Models;

public sealed class ProjectInspectorModel
{
    public ProjectInspectorModel(AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Id = workspace.Project.Id;
        DisplayName = workspace.Project.DisplayName;
        ProjectFile = workspace.ProjectFilePath;
        RootDirectory = workspace.ProjectRoot;
        FormatVersion = workspace.Project.FormatVersion;
        AssetCount = workspace.Project.Assets.Count;
        OpenDocumentCount = workspace.OpenDocuments.Count;
        HasUnsavedChanges = workspace.HasUnsavedChanges;
    }

    [Category("Project")] public string Id { get; }
    [Category("Project")] public string DisplayName { get; }
    [Category("Project")] public int FormatVersion { get; }
    [Category("Project")] public int AssetCount { get; }
    [Category("Project")] public int OpenDocumentCount { get; }
    [Category("State")] public bool HasUnsavedChanges { get; }
    [Category("Paths")] public string ProjectFile { get; }
    [Category("Paths")] public string RootDirectory { get; }
}

public sealed class AssetInspectorModel
{
    public AssetInspectorModel(ProjectAssetEntry asset, AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(workspace);
        Id = asset.Id;
        DisplayName = asset.DisplayName;
        Kind = asset.Kind;
        RelativePath = asset.RelativePath;
        FullPath = Path.GetFullPath(Path.Combine(workspace.ProjectRoot, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        Exists = File.Exists(FullPath);
        Tags = string.Join(", ", asset.Tags);
        IsOpen = workspace.OpenDocuments.Any(item => StringComparer.Ordinal.Equals(item.DocumentId, asset.Id));
    }

    [Category("Asset")] public string Id { get; }
    [Category("Asset")] public string DisplayName { get; }
    [Category("Asset")] public AuthoringAssetKind Kind { get; }
    [Category("Asset")] public string Tags { get; }
    [Category("State")] public bool Exists { get; }
    [Category("State")] public bool IsOpen { get; }
    [Category("Paths")] public string RelativePath { get; }
    [Category("Paths")] public string FullPath { get; }
}

public sealed class DocumentInspectorModel
{
    public DocumentInspectorModel(IAuthoringDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Id = document.DocumentId;
        DisplayName = document.DisplayName;
        Kind = document.Kind;
        FilePath = document.FilePath;
        IsDirty = document.IsDirty;
        Revision = document.Revision;
        SavedRevision = document.SavedRevision;
        CanUndo = document.CanUndo;
        CanRedo = document.CanRedo;
        UndoDescription = document.UndoDescription;
        RedoDescription = document.RedoDescription;
        Playroma.Sound.Authoring.Diagnostics.ValidationSummary validation = document.Validate();
        ValidationErrors = validation.Diagnostics.Count(item => item.Severity == Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnosticSeverity.Error);
        ValidationWarnings = validation.Diagnostics.Count(item => item.Severity == Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnosticSeverity.Warning);
    }

    [Category("Document")] public string Id { get; }
    [Category("Document")] public string DisplayName { get; }
    [Category("Document")] public AuthoringDocumentKind Kind { get; }
    [Category("State")] public bool IsDirty { get; }
    [Category("State")] public long Revision { get; }
    [Category("State")] public long SavedRevision { get; }
    [Category("History")] public bool CanUndo { get; }
    [Category("History")] public bool CanRedo { get; }
    [Category("History")] public string UndoDescription { get; }
    [Category("History")] public string RedoDescription { get; }
    [Category("Validation")] public int ValidationErrors { get; }
    [Category("Validation")] public int ValidationWarnings { get; }
    [Category("Paths")] public string FilePath { get; }
}
