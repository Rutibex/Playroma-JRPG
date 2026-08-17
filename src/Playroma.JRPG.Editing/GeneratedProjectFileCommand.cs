using Playroma.JRPG.Documents.Saving;

namespace Playroma.JRPG.Editing;

public sealed class CreateGeneratedProjectFileCommand : FileOverlayCommand
{
    private readonly byte[] _content;
    private string? _temporaryPath;

    public CreateGeneratedProjectFileCommand(
        string destinationRelativePath,
        ReadOnlySpan<byte> content,
        string? description = null)
        : base(description ?? $"Create {destinationRelativePath}", destinationRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativePath);
        if (content.IsEmpty) throw new ArgumentException("Generated project content cannot be empty.", nameof(content));
        DestinationRelativePath = destinationRelativePath.Replace('\\', '/');
        _content = content.ToArray();
    }

    public string DestinationRelativePath { get; }

    protected override async ValueTask<PendingFileMutation> PrepareAsync(
        IProjectFileOverlay overlay,
        CancellationToken cancellationToken)
    {
        _temporaryPath ??= Path.Combine(Path.GetTempPath(), "playroma-map-" + Guid.NewGuid().ToString("N") + Path.GetExtension(DestinationRelativePath));
        await File.WriteAllBytesAsync(_temporaryPath, _content, cancellationToken).ConfigureAwait(false);
        return await overlay.CreateStagedMutationAsync(
            FileMutationKind.Create,
            _temporaryPath,
            existingRelativePath: null,
            DestinationRelativePath,
            Description,
            cancellationToken).ConfigureAwait(false);
    }

    public override void ReleasePreviewResources()
    {
        base.ReleasePreviewResources();
        TryDeleteTemporary();
    }

    private void TryDeleteTemporary()
    {
        if (string.IsNullOrWhiteSpace(_temporaryPath)) return;
        try { if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _temporaryPath = null;
    }
}

/// <summary>Stages generated content as either a create or replacement, depending on the current overlay.</summary>
public sealed class WriteGeneratedProjectFileCommand : FileOverlayCommand
{
    private readonly byte[] _content;
    private string? _temporaryPath;

    public WriteGeneratedProjectFileCommand(
        string destinationRelativePath,
        ReadOnlySpan<byte> content,
        string? description = null)
        : base(description ?? $"Write {destinationRelativePath}", destinationRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRelativePath);
        if (content.IsEmpty) throw new ArgumentException("Generated project content cannot be empty.", nameof(content));
        DestinationRelativePath = destinationRelativePath.Replace('\\', '/');
        _content = content.ToArray();
    }

    public string DestinationRelativePath { get; }

    protected override async ValueTask<PendingFileMutation> PrepareAsync(
        IProjectFileOverlay overlay,
        CancellationToken cancellationToken)
    {
        _temporaryPath ??= Path.Combine(
            Path.GetTempPath(),
            "playroma-generated-" + Guid.NewGuid().ToString("N") + Path.GetExtension(DestinationRelativePath));
        await File.WriteAllBytesAsync(_temporaryPath, _content, cancellationToken).ConfigureAwait(false);
        bool exists = overlay.Exists(DestinationRelativePath);
        return await overlay.CreateStagedMutationAsync(
            exists ? FileMutationKind.Replace : FileMutationKind.Create,
            _temporaryPath,
            exists ? DestinationRelativePath : null,
            DestinationRelativePath,
            Description,
            cancellationToken).ConfigureAwait(false);
    }

    public override void ReleasePreviewResources()
    {
        base.ReleasePreviewResources();
        if (string.IsNullOrWhiteSpace(_temporaryPath)) return;
        try { if (File.Exists(_temporaryPath)) File.Delete(_temporaryPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _temporaryPath = null;
    }
}
