using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.ResourcesUI;

public enum ResourceSmartCollection
{
    All,
    Missing,
    NoKnownReferences,
    Encrypted,
    Duplicates,
    PendingChanges,
    InvalidOrUnsupported,
    Warnings,
    RecentlyModified
}

public sealed record ResourceNavigationTarget(
    AssetCategory? Category = null,
    string? LogicalName = null,
    string? RelativePath = null,
    ResourceSmartCollection? SmartCollection = null,
    string? PreviewTab = null)
{
    public string SelectionToken
    {
        get
        {
            string token = Category.HasValue && !string.IsNullOrWhiteSpace(LogicalName)
                ? $"asset:{Category.Value}:{LogicalName}"
                : !string.IsNullOrWhiteSpace(RelativePath)
                    ? "path:" + RelativePath.Replace('\\', '/')
                    : SmartCollection.HasValue
                        ? "smart:" + SmartCollection.Value
                        : Category.HasValue
                            ? "category:" + Category.Value
                            : "smart:All";
            return string.IsNullOrWhiteSpace(PreviewTab)
                ? token
                : token + "|tab:" + Uri.EscapeDataString(PreviewTab);
        }
    }

    public NavigationLocation ToLocation()
    {
        DocumentRequest request = new(
            EditorDocumentKind.AssetInventory,
            "assets",
            DisplayTitle: "Resource Manager");
        return new NavigationLocation(request, SelectionPath: SelectionToken);
    }

    public static ResourceNavigationTarget FromLocation(NavigationLocation? location)
    {
        string? token = location?.SelectionPath;
        if (string.IsNullOrWhiteSpace(token)) return new ResourceNavigationTarget(SmartCollection: ResourceSmartCollection.All);
        string? previewTab = null;
        int tabSeparator = token.LastIndexOf("|tab:", StringComparison.OrdinalIgnoreCase);
        if (tabSeparator >= 0)
        {
            previewTab = Uri.UnescapeDataString(token[(tabSeparator + 5)..]);
            token = token[..tabSeparator];
        }
        if (token.StartsWith("asset:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = token.Split(':', 3);
            if (parts.Length == 3 && Enum.TryParse(parts[1], ignoreCase: true, out AssetCategory category))
            {
                return new ResourceNavigationTarget(category, parts[2], PreviewTab: previewTab);
            }
        }
        if (token.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            return new ResourceNavigationTarget(RelativePath: token[5..], PreviewTab: previewTab);
        }
        if (token.StartsWith("category:", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(token[9..], ignoreCase: true, out AssetCategory parsedCategory))
        {
            return new ResourceNavigationTarget(Category: parsedCategory, PreviewTab: previewTab);
        }
        if (token.StartsWith("smart:", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(token[6..], ignoreCase: true, out ResourceSmartCollection smart))
        {
            return new ResourceNavigationTarget(SmartCollection: smart, PreviewTab: previewTab);
        }
        return new ResourceNavigationTarget(SmartCollection: ResourceSmartCollection.All, PreviewTab: previewTab);
    }
}

public sealed record ResourceManagerViewState(
    string SelectedCategory,
    string SearchText,
    string ViewMode,
    string SortColumn,
    bool SortDescending,
    string? SelectedAsset,
    string SelectedPreviewTab,
    int CategoryPanelWidth,
    int PreviewPanelWidth,
    int ThumbnailSize,
    IReadOnlyList<string> ExpandedGroups);
