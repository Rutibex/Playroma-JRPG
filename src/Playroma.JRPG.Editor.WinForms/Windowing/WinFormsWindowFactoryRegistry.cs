using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Windowing;

/// <summary>
/// Maps each editor-window kind to exactly one factory. Registration is explicit so the
/// application composition root controls which phases are currently available.
/// </summary>
public sealed class WinFormsWindowFactoryRegistry
{
    private readonly Dictionary<EditorWindowKind, IWinFormsWindowFactory> _factories = [];

    public event EventHandler? Changed;

    public IReadOnlyCollection<EditorWindowKind> RegisteredKinds => _factories.Keys.ToArray();

    public void Register(IWinFormsWindowFactory factory, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!replace && _factories.ContainsKey(factory.Kind))
        {
            throw new InvalidOperationException(
                $"A window factory for '{factory.Kind}' is already registered.");
        }

        _factories[factory.Kind] = factory;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Register(
        EditorWindowKind kind,
        Func<WindowRequest, CancellationToken, ValueTask<IWinFormsEditorWindow>> factory,
        bool replace = false) => Register(new DelegateWinFormsWindowFactory(kind, factory), replace);

    public bool Unregister(EditorWindowKind kind)
    {
        bool removed = _factories.Remove(kind);
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public bool TryResolve(EditorWindowKind kind, out IWinFormsWindowFactory? factory) =>
        _factories.TryGetValue(kind, out factory);

    public IWinFormsWindowFactory Resolve(EditorWindowKind kind) =>
        TryResolve(kind, out IWinFormsWindowFactory? factory)
            ? factory
            : throw new NotSupportedException(
                $"No top-level window factory is registered for '{kind}'. " +
                "That editor has not yet been migrated to the multi-window shell.");
}
