using System.Collections.Concurrent;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Indexing;

public sealed class IncrementalReferenceIndexCoordinator : IDisposable
{
    private readonly EditorCommandContext _commandContext;
    private readonly IMvProjectWorkspace _workspace;
    private readonly ProjectReferenceIndex _index;
    private readonly MvProjectReferenceIndexer _indexer;
    private readonly ConcurrentDictionary<string, byte> _pending;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    public IncrementalReferenceIndexCoordinator(
        EditorCommandContext commandContext,
        IMvProjectWorkspace workspace,
        ProjectReferenceIndex index,
        MvProjectReferenceIndexer? indexer = null)
    {
        _commandContext = commandContext ?? throw new ArgumentNullException(nameof(commandContext));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _indexer = indexer ?? new MvProjectReferenceIndexer();
        _pending = new ConcurrentDictionary<string, byte>(PathComparer);
        _commandContext.CommandApplied += OnCommandApplied;
    }

    public IReadOnlyCollection<string> PendingDocuments => _pending.Keys.ToArray();

    public async ValueTask<int> RefreshPendingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] pending = _pending.Keys.OrderBy(value => value, PathComparer).ToArray();
            int refreshed = 0;
            foreach (string relativePath in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _indexer.UpdateDocumentAsync(_workspace, _index, relativePath, cancellationToken).ConfigureAwait(false);
                _pending.TryRemove(relativePath, out _);
                refreshed++;
            }
            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Invalidate(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending[Normalize(relativePath)] = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _commandContext.CommandApplied -= OnCommandApplied;
        _refreshGate.Dispose();
        _disposed = true;
    }

    private void OnCommandApplied(object? sender, EditorCommandEvent e)
    {
        foreach (EditorMutationTarget target in e.Targets.Where(target => target.Kind == EditorMutationTargetKind.Json))
        {
            _pending[target.NormalizedRelativePath] = 0;
        }
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
