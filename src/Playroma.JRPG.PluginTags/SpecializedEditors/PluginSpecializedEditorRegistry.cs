namespace Playroma.JRPG.PluginTags;

public sealed record PluginSpecializedEditorDescriptor(
    string EditorId,
    string DisplayName,
    IReadOnlySet<PluginAuthoringSurface> SupportedSurfaces,
    IReadOnlySet<PluginFeatureKey> SupportedFeatures,
    bool SupportsRawFallback = true,
    string? Description = null);

public sealed class PluginSpecializedEditorRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PluginSpecializedEditorDescriptor> _editors = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PluginSpecializedEditorDescriptor> Editors
    {
        get
        {
            lock (_gate)
                return _editors.Values.OrderBy(editor => editor.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Register(PluginSpecializedEditorDescriptor descriptor, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.EditorId)) throw new ArgumentException("Editor ID cannot be empty.", nameof(descriptor));
        lock (_gate)
        {
            if (!replace && _editors.ContainsKey(descriptor.EditorId))
                throw new InvalidOperationException("Specialized editor is already registered: " + descriptor.EditorId);
            _editors[descriptor.EditorId] = descriptor;
        }
    }

    public bool TryGet(string editorId, out PluginSpecializedEditorDescriptor descriptor)
    {
        lock (_gate)
        {
            if (_editors.TryGetValue(editorId, out PluginSpecializedEditorDescriptor? found))
            {
                descriptor = found;
                return true;
            }

            descriptor = null!;
            return false;
        }
    }

    public PluginSpecializedEditorDescriptor? FindFor(PluginFeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(feature.SpecializedEditorId) && _editors.TryGetValue(feature.SpecializedEditorId, out PluginSpecializedEditorDescriptor? direct))
                return direct;
            return _editors.Values.FirstOrDefault(editor => editor.SupportedFeatures.Contains(feature.Key));
        }
    }

    public bool Unregister(string editorId)
    {
        lock (_gate)
            return _editors.Remove(editorId);
    }
}
