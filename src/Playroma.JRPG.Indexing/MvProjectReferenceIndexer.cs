using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.RpgMakerMV.Documents;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Indexing;

public sealed class MvProjectReferenceIndexer
{
    public async ValueTask<ProjectReferenceIndex> BuildAsync(
        IMvProjectWorkspace workspace,
        IProgress<ReferenceIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ProjectReferenceIndex index = new();
        ProjectDocumentHandle[] documents = workspace.Documents
            .Where(handle => handle.Kind != MvDocumentKind.Unknown)
            .OrderBy(handle => handle.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool hasManifest = !string.IsNullOrWhiteSpace(workspace.Snapshot.Plugins.ManifestRelativePath);
        int totalDocuments = documents.Length + (hasManifest ? 1 : 0);
        int completed = 0;
        foreach (ProjectDocumentHandle handle in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ProjectReference> references = await ExtractDocumentAsync(
                workspace,
                handle.RelativePath,
                handle.Kind,
                cancellationToken).ConfigureAwait(false);
            index.ReplaceDocument(handle.RelativePath, references);
            completed++;
            progress?.Report(new ReferenceIndexProgress(
                completed,
                totalDocuments,
                handle.RelativePath,
                index.Statistics.TotalReferenceCount));
        }

        if (hasManifest)
        {
            string manifestPath = workspace.Snapshot.Plugins.ManifestRelativePath!;
            IReadOnlyList<ProjectReference> pluginReferences = await ExtractPluginParameterReferencesAsync(
                workspace,
                manifestPath,
                cancellationToken).ConfigureAwait(false);
            index.ReplaceDocument(manifestPath, pluginReferences);
            progress?.Report(new ReferenceIndexProgress(
                totalDocuments,
                totalDocuments,
                manifestPath,
                index.Statistics.TotalReferenceCount));
        }

        return index;
    }

    public async ValueTask UpdateDocumentAsync(
        IMvProjectWorkspace workspace,
        ProjectReferenceIndex index,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(index);
        ProjectDocumentHandle? handle = workspace.Documents.FirstOrDefault(item =>
            string.Equals(Normalize(item.RelativePath), Normalize(relativePath), PathComparison));
        if (handle is null)
        {
            index.RemoveDocument(relativePath);
            return;
        }

        if (handle.Kind == MvDocumentKind.Unknown)
        {
            IReadOnlyList<ProjectReference> pluginReferences = await ExtractPluginParameterReferencesAsync(
                workspace,
                handle.RelativePath,
                cancellationToken).ConfigureAwait(false);
            index.ReplaceDocument(handle.RelativePath, pluginReferences);
            return;
        }

        IReadOnlyList<ProjectReference> references = await ExtractDocumentAsync(
            workspace,
            handle.RelativePath,
            handle.Kind,
            cancellationToken).ConfigureAwait(false);
        index.ReplaceDocument(handle.RelativePath, references);
    }

    private static async ValueTask<IReadOnlyList<ProjectReference>> ExtractDocumentAsync(
        IMvProjectWorkspace workspace,
        string relativePath,
        MvDocumentKind kind,
        CancellationToken cancellationToken)
    {
        IMutableProjectDocument mutable = await workspace.OpenDocumentAsync(relativePath, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> generated = await mutable.GenerateBytesAsync(cancellationToken).ConfigureAwait(false);
        byte[] bomless = Utf8JsonInput.ToBomlessArray(generated.Span);
        using JsonDocument json = JsonDocument.Parse(bomless);
        return MvReferenceExtractor.Extract(relativePath, kind, json.RootElement);
    }

    private static async ValueTask<IReadOnlyList<ProjectReference>> ExtractPluginParameterReferencesAsync(
        IMvProjectWorkspace workspace,
        string relativePath,
        CancellationToken cancellationToken)
    {
        ProjectDocumentHandle? handle = workspace.Documents.FirstOrDefault(item =>
            string.Equals(Normalize(item.RelativePath), Normalize(relativePath), PathComparison));
        if (handle is not null)
        {
            IMutableProjectDocument document = await workspace
                .OpenDocumentAsync(handle.RelativePath, cancellationToken)
                .ConfigureAwait(false);
            if (document is MvPluginManifestDocument manifest)
            {
                MvReferenceExtractor.ExtractionContext currentContext = new(handle.RelativePath, MvDocumentKind.Unknown);
                for (int index = 0; index < manifest.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string pluginName = manifest.GetName(index);
                    foreach (string parameterName in manifest.GetParameterNames(index))
                    {
                        string? value = manifest.GetParameter(index, parameterName);
                        string escapedName = Escape(parameterName);
                        ReferenceSource source = new(
                            handle.RelativePath,
                            $"/plugins/{index}/parameters/{escapedName}",
                            Label: pluginName + ": " + parameterName);
                        HeuristicReferenceScanner.ScanText(
                            value,
                            source,
                            ReferenceUsageKind.PluginParameter,
                            currentContext);
                    }
                }

                return currentContext.References.Distinct().ToArray();
            }
        }

        MvReferenceExtractor.ExtractionContext snapshotContext = new(relativePath, MvDocumentKind.Unknown);
        foreach (MvPluginEntry entry in workspace.Snapshot.Plugins.Entries)
        {
            foreach ((string name, string value) in entry.Parameters)
            {
                ReferenceSource source = new(
                    relativePath,
                    $"/plugins/{entry.LoadOrder}/parameters/{Escape(name)}",
                    Label: entry.Name + ": " + name);
                HeuristicReferenceScanner.ScanText(
                    value,
                    source,
                    ReferenceUsageKind.PluginParameter,
                    snapshotContext);
            }
        }

        return snapshotContext.References.Distinct().ToArray();
    }

    private static string Escape(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static string Normalize(string value) => value.Replace('\\', '/');
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
