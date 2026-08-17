namespace Playroma.JRPG.Editor.Shell;

public sealed class ShellServiceContainer : IServiceProvider, IAsyncDisposable
{
    private readonly Dictionary<Type, object> _services = [];
    private readonly List<object> _ordered = [];

    public void Add<T>(T service) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
        if (!_ordered.Contains(service, ReferenceEqualityComparer.Instance)) _ordered.Add(service);
    }

    public T GetRequired<T>() where T : notnull =>
        (T)(GetService(typeof(T)) ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered."));

    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);

    public async ValueTask DisposeAsync()
    {
        for (int index = _ordered.Count - 1; index >= 0; index--)
        {
            object service = _ordered[index];
            if (service is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (service is IDisposable disposable) disposable.Dispose();
        }
        _ordered.Clear();
        _services.Clear();
    }
}
